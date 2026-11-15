using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Weather;

namespace SnowDock.Widgets.CalendarWidgets;

[WidgetFamily(12)]
internal static class MoreClocks
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Clocks;
        add("world-clocks", "World Clocks", c, "Three small clock faces for three time zones you choose; dark faces mean night there.", () => new WorldClocks());
        add("analog-minimal", "Minimal Clock", c, "A dark clock face with nothing but hour marks and hands.", () => new MinimalClock());
        add("seven-days", "This Week", c, "The seven days of this week with today picked out, plus the temperature if a weather city is set.", () => new SevenDays());
        add("today", "Today", c, "Weekday, date and time together, with the temperature in your weather city.", () => new TodayTile());
        add("clock-weather", "Clock & Weather", c, "Time, date and temperature stacked as three coloured bars in a square tile.", () => new ClockWeatherTile());
    }
}

internal sealed class WorldClocks : ClockBase
{
    private static readonly (string Name, string Zone)[] Defaults =
        { ("New York", "Eastern Standard Time"), ("London", "GMT Standard Time"), ("Tokyo", "Tokyo Standard Time") };

    public override string Title => string.Join(" · ", Enumerable.Range(0, 3).Select(i => $"{Name(i)} {WidgetEnv.Time(TimeIn(i))}{Suffix(TimeIn(i))}"));
    public override double WidthUnits => 3;
    public override bool HasCard => true;

    private static string Suffix(DateTime t) => WidgetEnv.Clock24h ? "" : " " + WidgetEnv.AmPm(t);
    private string Name(int i) => Get($"name{i}", Defaults[i].Name);

    private TimeZoneInfo Zone(int i)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(Get($"zone{i}", Defaults[i].Zone)); }
        catch { return TimeZoneInfo.Utc; }
    }

    private DateTime TimeIn(int i) => TimeZoneInfo.ConvertTime(DateTime.Now, Zone(i));

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height, col = r.Width / 3, d = Math.Min(col - h * 0.14, h * 0.68);
        var day = WidgetPaint.For(true, p.PixelsPerDip);
        var night = WidgetPaint.For(false, p.PixelsPerDip);
        for (int i = 0; i < 3; i++)
        {
            var t = TimeIn(i);
            double cx = r.X + col * (i + 0.5);
            var face = new Rect(cx - d / 2, r.Y + h * 0.06, d, d);
            bool isDay = t.Hour is >= 6 and < 18;
            AnalogClock.DrawFace(dc, face, t, isDay ? day : night, seconds: false);
            Centered(dc, Name(i), new Point(cx, r.Y + h * 0.86), h * 0.15, p.SubInk, p, col - 4);
        }
    }

    /// <summary>Three rows: a name to show and one of Windows' time zones.</summary>
    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 380 };
        var zones = TimeZoneInfo.GetSystemTimeZones();
        var rows = new List<(TextBox Name, ComboBox Zone)>();
        for (int i = 0; i < 3; i++)
        {
            panel.Children.Add(new TextBlock { Text = $"Clock {i + 1}", FontSize = 12, Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 3) });
            var name = LauncherWidgets.Input(p);
            name.FontSize = 13;
            name.Text = Name(i);
            var zone = new ComboBox { ItemsSource = zones, DisplayMemberPath = "DisplayName", Margin = new Thickness(0, 3, 0, 0) };
            zone.SelectedItem = zones.FirstOrDefault(z => z.Id == Zone(i).Id);
            panel.Children.Add(name);
            panel.Children.Add(zone);
            rows.Add((name, zone));
        }
        var save = new Button { Content = "Save", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 3, 14, 3) };
        save.Click += (_, _) =>
        {
            for (int i = 0; i < 3; i++)
            {
                if (!string.IsNullOrWhiteSpace(rows[i].Name.Text)) Set($"name{i}", rows[i].Name.Text.Trim());
                if (rows[i].Zone.SelectedItem is TimeZoneInfo z) Set($"zone{i}", z.Id);
            }
            Host?.Redraw();
            Host?.CloseCard();
        };
        panel.Children.Add(save);
        return panel;
    }
}

/// <summary>A dark face with hour ticks and hands only, the same on light and dark docks.</summary>
internal sealed class MinimalClock : ClockBase
{
    public override string Title => DateTime.Now.ToString("t");
    protected override bool ShowsSeconds => true;

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static readonly Brush Face = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1A)));
    private static readonly Pen Rim = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1));
    private static readonly Brush Light = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF5)));
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(150, 0xF2, 0xF2, 0xF5)));
    private static readonly Brush Second = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)));

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var t = DateTime.Now;
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double rad = Math.Min(r.Width, r.Height) / 2 - 1;
        dc.DrawEllipse(Face, Rim, c, rad, rad);
        for (int i = 0; i < 12; i++)
        {
            bool major = i % 3 == 0;
            var pen = new Pen(major ? Light : Dim, major ? Math.Max(1.6, rad * 0.07) : Math.Max(1, rad * 0.04)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            double a = i * Math.PI / 6, inner = rad * (major ? 0.7 : 0.76), outer = rad * 0.86;
            dc.DrawLine(pen, new Point(c.X + Math.Sin(a) * inner, c.Y - Math.Cos(a) * inner), new Point(c.X + Math.Sin(a) * outer, c.Y - Math.Cos(a) * outer));
        }
        void Hand(double turns, double length, double width, Brush b, double tail = 0)
        {
            double a = turns * 2 * Math.PI;
            dc.DrawLine(new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                new Point(c.X - Math.Sin(a) * tail, c.Y + Math.Cos(a) * tail), new Point(c.X + Math.Sin(a) * length, c.Y - Math.Cos(a) * length));
        }
        Hand((t.Hour % 12 + t.Minute / 60.0) / 12, rad * 0.48, Math.Max(2.4, rad * 0.11), Light);
        Hand((t.Minute + t.Second / 60.0) / 60, rad * 0.72, Math.Max(1.8, rad * 0.075), Light);
        Hand(t.Second / 60.0, rad * 0.8, Math.Max(0.9, rad * 0.035), Second, rad * 0.16);
        dc.DrawEllipse(Second, null, c, Math.Max(1.6, rad * 0.06), Math.Max(1.6, rad * 0.06));
    }
}
