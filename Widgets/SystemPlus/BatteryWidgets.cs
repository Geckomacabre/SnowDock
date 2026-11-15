using System.Windows;
using System.Windows.Media;
using SnowDock.Widgets.Media;

namespace SnowDock.Widgets.SystemPlus;

[WidgetFamily(34)]
internal static class BatteryFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.System;
        add("battery-ring", "Battery Ring", c, "Charge as a coloured ring with the percentage inside.", () => new BatteryRing());
        add("battery-core", "Battery Core", c, "Twelve cells around a core that light up with the charge.", () => new BatteryCore());
        add("battery-dots", "Battery Dots", c, "The charge in big dot-matrix digits over a row of dots.", () => new BatteryDots());
        add("battery-gauge", "Battery Gauge", c, "A fuel gauge whose needle swings from E to F with the charge.", () => new BatteryGauge());
    }
}

/// <summary>Shared battery reading for the battery tiles (desktops without a battery show "AC").</summary>
internal abstract class BatteryTile : DockWidget
{
    private string _sig = "";

    protected static bool Has => SystemSampler.HasBattery;
    protected static int Pct => Math.Clamp(SystemSampler.BatteryPercent, 0, 100);
    protected static bool Charging => SystemSampler.Charging;

    public override string Title => Has
        ? $"Battery {Pct}%{(Charging ? ", plugged in" : "")}"
        : "No battery: running on AC power";

    /// <summary>Extra state that changes the drawing between battery readings (an animation step).</summary>
    protected virtual string Frame(DateTime now) => "";

    public override bool Tick(DateTime now)
    {
        SystemSampler.Sample();
        var sig = $"{Has}{Pct}{Charging}{Frame(now)}";
        if (sig == _sig) return false;
        _sig = sig;
        return true;
    }

    protected static Brush Level(WidgetPaint p) => Pct <= 15 ? p.Bad : Pct <= 30 ? p.Warn : p.Good;

    public override void OnClick() => DockWindow.ShellOpen("ms-settings:batterysaver");
}

internal sealed class BatteryRing : BatteryTile
{
    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double h = r.Height;
        Ring(dc, c, h * 0.36, Has ? Pct / 100.0 : 0, Level(p), p, Math.Max(3, h * 0.085));
        if (!Has)
        {
            Glyphs.Bolt(dc, new Point(c.X, c.Y - h * 0.12), h * 0.09, p.SubInk);
            Centered(dc, "AC", new Point(c.X, c.Y + h * 0.07), h * 0.19, p.Ink, p);
            return;
        }
        Centered(dc, $"{Pct}%", new Point(c.X, c.Y + (Charging ? h * 0.05 : 0)), h * 0.2, p.Ink, p, h * 0.5);
        if (Charging) Glyphs.Bolt(dc, new Point(c.X, c.Y - h * 0.15), h * 0.08, p.Good);
    }
}

/// <summary>Twelve cells around a core; when charging, the next cell blinks.</summary>
internal sealed class BatteryCore : BatteryTile
{
    private bool _blink;
    private static int Lit => Pct <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(Pct / 100.0 * 12), 1, 12);

    protected override string Frame(DateTime now)
    {
        _blink = Has && Charging && Lit < 12 && now.Second % 2 == 0;
        return _blink ? "b" : "";
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double h = r.Height, rad = h * 0.33, thick = h * 0.11;
        var level = Level(p);
        var glow = level.Clone();
        glow.Opacity = 0.3;
        for (int i = 0; i < 12; i++)
        {
            bool on = Has && (i < Lit || (_blink && i == Lit));
            double a0 = -90 + i * 30 + 4, a1 = a0 + 22;
            var seg = Segment(c, rad, a0, a1);
            if (on) dc.DrawGeometry(null, new Pen(glow, thick * 1.7), seg);
            dc.DrawGeometry(null, new Pen(on ? level : p.Track, thick), seg);
        }
        // The core.
        dc.DrawEllipse(null, new Pen(Has ? level : p.Track, Math.Max(1, h * 0.025)), c, h * 0.2, h * 0.2);
        if (Has) Centered(dc, $"{Pct}", c, h * 0.17, p.Ink, p, h * 0.34);
        else Centered(dc, "AC", c, h * 0.15, p.SubInk, p);
    }

    private static StreamGeometry Segment(Point c, double r, double a0, double a1)
    {
        double t0 = a0 * Math.PI / 180, t1 = a1 * Math.PI / 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(t0), c.Y + r * Math.Sin(t0)), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(t1), c.Y + r * Math.Sin(t1)), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        return g;
    }
}

/// <summary>The percentage in dot-matrix digits on an LED panel, over a row of ten charge dots.</summary>
internal sealed class BatteryDots : BatteryTile
{
    public override double WidthUnits => 1.5;

    // 3×5 dot font, rows top to bottom, bits left to right.
    private static readonly Dictionary<char, string> Font = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
        ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001001001",
        ['8'] = "111101111101111", ['9'] = "111101111001111", ['%'] = "101001010100101", ['A'] = "010101111101101",
        ['C'] = "111100100100111"
    };

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        string text = Has ? $"{Pct}%" : "AC";
        int cols = text.Length * 4 - 1;
        double h = r.Height, pad = h * 0.16;
        double pitch = Math.Min((r.Width - pad * 2) / cols, h * 0.095), dot = pitch * 0.36;
        double left = r.X + (r.Width - pitch * (cols - 1)) / 2, top = r.Y + h * 0.2;
        for (int k = 0; k < text.Length; k++)
        {
            var bits = Font[text[k]];
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 4; col++)
                {
                    int x = k * 4 + col;
                    if (x >= cols) continue;
                    bool on = col < 3 && bits[row * 3 + col] == '1';
                    dc.DrawEllipse(on ? p.Ink : p.Track, null, new Point(left + x * pitch, top + row * pitch), dot, dot);
                }
        }
        // Ten charge dots.
        int lit = Has ? (int)Math.Round(Pct / 10.0) : 0;
        double step = (r.Width - pad * 2) / 10, d = Math.Min(step * 0.3, h * 0.05), y = r.Bottom - h * 0.2;
        for (int i = 0; i < 10; i++)
            dc.DrawEllipse(i < lit ? Level(p) : p.Track, null, new Point(r.X + pad + step * (i + 0.5), y), d, d);
    }
}

/// <summary>A fuel gauge: E on the left, F on the right, the needle at the charge.</summary>
internal sealed class BatteryGauge : BatteryTile
{
    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height, rad = h * 0.4;
        var o = new Point(r.X + r.Width / 2, r.Y + h * 0.6);
        const double from = -150, sweep = 120;
        Point At(double f, double radius)
        {
            double a = (from + sweep * f) * Math.PI / 180;
            return new Point(o.X + radius * Math.Cos(a), o.Y + radius * Math.Sin(a));
        }
        // Dial: a faint arc, a red reserve zone, and five ticks.
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(At(0, rad), false, false);
            g.ArcTo(At(1, rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(p.Track, Math.Max(2, h * 0.05)), arc);
        var reserve = new StreamGeometry();
        using (var g = reserve.Open())
        {
            g.BeginFigure(At(0, rad), false, false);
            g.ArcTo(At(0.15, rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(p.Bad, Math.Max(2, h * 0.05)), reserve);
        var tick = Glyphs.Stroke(p.SubInk, Math.Max(1, h * 0.025));
        for (int i = 0; i <= 4; i++)
            dc.DrawLine(tick, At(i / 4.0, rad * (i % 4 == 0 ? 0.72 : 0.8)), At(i / 4.0, rad * 0.92));
        Centered(dc, "E", At(0, rad * 0.62) + new Vector(0, h * 0.13), h * 0.13, p.SubInk, p);
        Centered(dc, "F", At(1, rad * 0.62) + new Vector(0, h * 0.13), h * 0.13, p.SubInk, p);

        if (Has)
        {
            var needle = Glyphs.Stroke(Pct <= 15 ? p.Bad : p.Warn, Math.Max(1.6, h * 0.04));
            dc.DrawLine(needle, o, At(Pct / 100.0, rad * 0.86));
        }
        dc.DrawEllipse(p.Ink, null, o, h * 0.055, h * 0.055);
        var label = Text(Has ? $"{Pct}%" : "AC", h * 0.15, Has ? p.Ink : p.SubInk, p);
        double bolt = Has && Charging ? h * 0.12 : 0, y = r.Bottom - h * 0.16;
        double x = o.X - (label.Width + bolt) / 2;
        if (bolt > 0) Glyphs.Bolt(dc, new Point(x + bolt * 0.4, y), h * 0.06, p.Good);
        dc.DrawText(label, new Point(x + bolt, y - label.Height / 2));
    }
}
