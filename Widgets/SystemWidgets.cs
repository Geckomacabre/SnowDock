using System.IO;
using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets;

[WidgetFamily(30)]
internal static class SystemWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.System;
        add("cpu", "CPU", c, "Processor load as a ring.", () => new LoadRing(ram: false));
        add("ram", "Memory", c, "Memory in use as a ring.", () => new LoadRing(ram: true));
        add("top-cpu", "Top CPU", c, "The three apps using the most processor; click for Task Manager.", () => new TopApps(ram: false));
        add("top-ram", "Top Memory", c, "The three apps holding the most memory, grouped by app.", () => new TopApps(ram: true));
        add("network", "Network", c, "Download and upload speed with a live line.", () => new NetworkWidget());
        add("storage", "Storage", c, "Free space on a drive, with a usage bar.", () => new StorageWidget());
        add("battery", "Battery", c, "Charge level and whether it's charging.", () => new BatteryWidget());
    }
}

internal sealed class LoadRing : DockWidget
{
    private readonly bool _ram;
    private int _last = -1;
    public LoadRing(bool ram) => _ram = ram;
    public override string Title => _ram ? $"Memory: {SystemSampler.RamUsedGb:0.0} of {SystemSampler.RamTotalGb:0} GB" : "CPU";

    private double Value => _ram ? SystemSampler.Ram : SystemSampler.Cpu;

    public override bool Tick(DateTime now)
    {
        SystemSampler.Sample();
        int v = (int)(Value * 100);
        if (v == _last) return false;
        _last = v;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double v = Value;
        Ring(dc, c, r.Height * 0.36, v, v > 0.85 ? p.Bad : v > 0.6 ? p.Warn : p.Good, p, Math.Max(3, r.Height * 0.08));
        Centered(dc, $"{(int)(v * 100)}%", new Point(c.X, c.Y - r.Height * 0.04), r.Height * 0.2, p.Ink, p);
        Centered(dc, _ram ? "RAM" : "CPU", new Point(c.X, c.Y + r.Height * 0.15), r.Height * 0.11, p.SubInk, p);
    }

    public override void OnClick() => DockWindow.ShellOpen("taskmgr.exe");
}

internal sealed class TopApps : DockWidget
{
    private readonly bool _ram;
    private string _sig = "";
    public TopApps(bool ram) => _ram = ram;
    public override string Title => _ram ? "Top Memory" : "Top CPU";
    public override double WidthUnits => 2.4;

    public override bool Tick(DateTime now)
    {
        SystemSampler.SampleProcesses();
        var list = _ram ? SystemSampler.TopRam : SystemSampler.TopCpu;
        var sig = string.Join("|", list.Select(x => $"{x.Name}:{Format(x.Value)}"));
        if (sig == _sig) return false;
        _sig = sig;
        return true;
    }

    private string Format(double v) => _ram ? (v >= 1073741824 ? $"{v / 1073741824:0.0} GB" : $"{v / 1048576:0} MB") : $"{v * 100:0}%";

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var list = _ram ? SystemSampler.TopRam : SystemSampler.TopCpu;
        double pad = r.Height * 0.12, line = (r.Height - pad * 2) / 3, size = Math.Min(12.5, line * 0.62);
        for (int i = 0; i < 3; i++)
        {
            double y = r.Y + pad + line * i;
            if (i >= list.Count) break;
            var name = Text(list[i].Name, size, p.Ink, p, FontWeights.Normal);
            name.MaxTextWidth = r.Width * 0.6;
            name.MaxLineCount = 1;
            name.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(name, new Point(r.X + pad, y + (line - name.Height) / 2));
            var value = Text(Format(list[i].Value), size, p.SubInk, p);
            dc.DrawText(value, new Point(r.Right - pad - value.Width, y + (line - value.Height) / 2));
        }
        if (list.Count == 0) Centered(dc, "Measuring…", new Point(r.X + r.Width / 2, r.Y + r.Height / 2), 12, p.SubInk, p);
    }

    public override void OnClick() => DockWindow.ShellOpen("taskmgr.exe");
}

internal sealed class NetworkWidget : DockWidget
{
    public override string Title => "Network";
    public override double WidthUnits => 2.2;

    public override bool Tick(DateTime now)
    {
        SystemSampler.Sample();
        return true; // the line moves every second
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double pad = r.Height * 0.12;
        var chart = new Rect(r.X + pad, r.Y + r.Height * 0.48, r.Width - pad * 2, r.Height * 0.38);
        double max = Math.Max(1024, SystemSampler.DownHistory.Concat(SystemSampler.UpHistory).DefaultIfEmpty(0).Max());
        Line(dc, chart, SystemSampler.DownHistory, max, p.Accent);
        Line(dc, chart, SystemSampler.UpHistory, max, p.Good);
        double size = r.Height * 0.15;
        dc.DrawText(Text("↓ " + SystemSampler.Speed(SystemSampler.DownBps), size, p.Ink, p), new Point(r.X + pad, r.Y + pad * 0.7));
        var up = Text("↑ " + SystemSampler.Speed(SystemSampler.UpBps), size, p.SubInk, p);
        dc.DrawText(up, new Point(r.X + pad, r.Y + pad * 0.7 + size * 1.25));
    }

    private static void Line(DrawingContext dc, Rect area, IEnumerable<double> values, double max, Brush b)
    {
        var v = values.ToList();
        if (v.Count < 2) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i < v.Count; i++)
            {
                var pt = new Point(area.X + area.Width * i / 39.0 + area.Width * (40 - v.Count) / 39.0, area.Bottom - area.Height * v[i] / max);
                if (i == 0) c.BeginFigure(pt, false, false); else c.LineTo(pt, true, true);
            }
        }
        dc.DrawGeometry(null, new Pen(b, 1.5) { LineJoin = PenLineJoin.Round }, g);
    }
}

internal sealed class StorageWidget : DockWidget
{
    private string _sig = "";
    private string Drive => Get("drive", "C");
    public override string Title => $"Drive {Drive}:";
    public override double WidthUnits => 2;

    private (double Free, double Total) Space()
    {
        try
        {
            var d = new DriveInfo(Drive);
            return (d.AvailableFreeSpace / 1073741824.0, d.TotalSize / 1073741824.0);
        }
        catch { return (0, 0); }
    }

    public override bool Tick(DateTime now)
    {
        if (now.Second % 10 != 0 && _sig != "") return false; // free space barely moves
        var (free, total) = Space();
        var sig = $"{free:0}/{total:0}";
        if (sig == _sig) return false;
        _sig = sig;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var (free, total) = Space();
        double pad = r.Height * 0.16, used = total <= 0 ? 0 : 1 - free / total;
        Centered(dc, $"{Drive}:  {free:0} GB free", new Point(r.X + r.Width / 2, r.Y + r.Height * 0.32), r.Height * 0.16, p.Ink, p, r.Width - pad * 2);
        Bar(dc, new Rect(r.X + pad, r.Y + r.Height * 0.55, r.Width - pad * 2, Math.Max(5, r.Height * 0.1)), used,
            used > 0.9 ? p.Bad : used > 0.75 ? p.Warn : p.Accent, p);
        Centered(dc, $"of {total:0} GB", new Point(r.X + r.Width / 2, r.Bottom - r.Height * 0.18), r.Height * 0.12, p.SubInk, p);
    }

    public override void OnClick() => DockWindow.ShellOpen(Drive + @":\");
}

internal sealed class BatteryWidget : DockWidget
{
    private string _sig = "";
    public override string Title => SystemSampler.HasBattery ? $"Battery {SystemSampler.BatteryPercent}%" : "No battery (plugged in)";

    public override bool Tick(DateTime now)
    {
        SystemSampler.Sample();
        var sig = $"{SystemSampler.HasBattery}{SystemSampler.BatteryPercent}{SystemSampler.Charging}";
        if (sig == _sig) return false;
        _sig = sig;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double w = r.Width * 0.56, h = r.Height * 0.3;
        var body = new Rect(r.X + (r.Width - w) / 2 - 2, r.Y + r.Height * 0.26, w, h);
        dc.DrawRoundedRectangle(null, new Pen(p.Ink, 1.6), body, 4, 4);
        dc.DrawRoundedRectangle(p.Ink, null, new Rect(body.Right + 1.5, body.Y + h * 0.3, 3, h * 0.4), 1, 1);
        if (!SystemSampler.HasBattery)
        {
            Centered(dc, "AC", new Point(r.X + r.Width / 2, r.Y + r.Height * 0.74), r.Height * 0.16, p.SubInk, p);
            return;
        }
        int pct = SystemSampler.BatteryPercent;
        var fill = new Rect(body.X + 2.5, body.Y + 2.5, Math.Max(0, (body.Width - 5) * pct / 100.0), body.Height - 5);
        dc.DrawRoundedRectangle(pct <= 15 ? p.Bad : SystemSampler.Charging ? p.Good : p.Ink, null, fill, 2, 2);
        Centered(dc, $"{pct}%{(SystemSampler.Charging ? " ⚡" : "")}", new Point(r.X + r.Width / 2, r.Y + r.Height * 0.74), r.Height * 0.17, p.Ink, p);
    }
}
