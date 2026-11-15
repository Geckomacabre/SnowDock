using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.SystemPlus;

[WidgetFamily(33)]
internal static class TemperatureFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add) =>
        add("temperature", "Temperature", WidgetCatalog.System,
            "A CPU temperature gauge with processor and memory load beside it (shows n/a if Windows won't report the heat).",
            () => new TemperatureWidget());
}

/// <summary>
/// Thermal-zone temperature from WMI, read on the thread pool at most every 10 s. Many PCs only report it to
/// administrators, and some never do; then <see cref="Celsius"/> stays null.
/// </summary>
internal static class ThermalSampler
{
    private static DateTime _last = DateTime.MinValue;
    private static int _busy;

    public static double? Celsius { get; private set; }
    public static string Source { get; private set; } = "";
    /// <summary>Bumped each time a reading arrives, so tiles know to redraw.</summary>
    public static int Version { get; private set; }

    public static void Request()
    {
        if ((DateTime.UtcNow - _last).TotalSeconds < 10 || Interlocked.Exchange(ref _busy, 1) == 1) return;
        _last = DateTime.UtcNow;
        var ui = Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            var (c, src) = Read();
            ui?.BeginInvoke(() => { Celsius = c; Source = src; Version++; });
            Interlocked.Exchange(ref _busy, 0);
        });
    }

    private static (double?, string) Read()
    {
        // ACPI thermal zones: tenths of a kelvin. Usually needs admin rights.
        try
        {
            var zones = Wmi.Query(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
                o => (double)Convert.ToDouble(o.CurrentTemperature) / 10 - 273.15);
            var ok = zones.Where(t => t > 0 && t < 125).ToList();
            if (ok.Count > 0) return (ok.Max(), "ACPI thermal zone");
        }
        catch { }
        // The same zones through the performance counters, which standard users can often read (kelvin).
        try
        {
            var zones = Wmi.Query(@"root\cimv2", "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation",
                o => (double)Convert.ToDouble(o.Temperature) - 273.15);
            var ok = zones.Where(t => t > 0 && t < 125).ToList();
            if (ok.Count > 0) return (ok.Max(), "thermal zone counter");
        }
        catch { }
        return (null, "");
    }
}

internal sealed class TemperatureWidget : DockWidget
{
    private string _sig = "";
    public override double WidthUnits => 2.2;

    public override string Title => ThermalSampler.Celsius is double c
        ? $"CPU {c:0}°C · load {SystemSampler.Cpu * 100:0}% · memory {SystemSampler.Ram * 100:0}%"
        : $"Temperature n/a (Windows didn't report it) · load {SystemSampler.Cpu * 100:0}% · memory {SystemSampler.Ram * 100:0}%";

    public override bool Tick(DateTime now)
    {
        SystemSampler.Sample();
        ThermalSampler.Request();
        var sig = $"{ThermalSampler.Version}{(int)(SystemSampler.Cpu * 100)}{(int)(SystemSampler.Ram * 100)}";
        if (sig == _sig) return false;
        _sig = sig;
        return true;
    }

    public override void OnClick() => DockWindow.ShellOpen("taskmgr.exe");

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height;
        var c = new Point(r.X + h * 0.55, r.Y + h * 0.53);
        double rad = h * 0.34, thick = Math.Max(3, h * 0.08);
        var temp = ThermalSampler.Celsius;
        double f = temp is double t ? Math.Clamp((t - 25) / 75, 0, 1) : 0;
        Brush heat = temp switch { >= 85 => p.Bad, >= 65 => p.Warn, _ => p.Good };
        Gauge(dc, c, rad, 1, new Pen(p.Track, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        if (f > 0) Gauge(dc, c, rad, f, new Pen(heat, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Centered(dc, temp is double d ? $"{d:0}°" : "n/a", new Point(c.X, c.Y - h * 0.02), h * (temp == null ? 0.17 : 0.21),
            temp == null ? p.SubInk : p.Ink, p, rad * 1.5);
        Centered(dc, "CPU °C", new Point(c.X, c.Y + rad * 0.82), h * 0.1, p.SubInk, p);

        // Load bars on the right.
        double x = r.X + h * 1.05, w = r.Right - x - h * 0.16, size = h * 0.13;
        Row(dc, p, "CPU", SystemSampler.Cpu, x, r.Y + h * 0.18, w, size);
        Row(dc, p, "MEM", SystemSampler.Ram, x, r.Y + h * 0.56, w, size);
    }

    private static void Row(DrawingContext dc, WidgetPaint p, string label, double v, double x, double y, double w, double size)
    {
        dc.DrawText(Text(label, size, p.SubInk, p), new Point(x, y));
        var value = Text($"{v * 100:0}%", size, p.Ink, p);
        dc.DrawText(value, new Point(x + w - value.Width, y));
        Bar(dc, new Rect(x, y + size * 1.45, w, Math.Max(4, size * 0.5)), v, v > 0.85 ? p.Bad : v > 0.6 ? p.Warn : p.Accent, p);
    }

    /// <summary>A 240° gauge arc opening at the bottom, filled to <paramref name="f"/>.</summary>
    private static void Gauge(DrawingContext dc, Point c, double r, double f, Pen pen)
    {
        double a0 = 150 * Math.PI / 180, a1 = (150 + 240 * f) * Math.PI / 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, 240 * f > 180,
                SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, g);
    }
}
