using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnowDock.Widgets;

internal static class ClockWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Clocks;
        add("clock", "Clock", c, "The time in large digits, with the date underneath.", () => new DigitalClock());
        add("flip-clock", "Flip Clock", c, "Hours and minutes on split-flap cards.", () => new FlipClock());
        add("segment-clock", "Segment Clock", c, "The time as a seven-segment display, with the weekday.", () => new SegmentClock());
        add("analog", "Analog Clock", c, "A plain clock face with hour, minute and second hands.", () => new AnalogClock());
        add("world-time", "World Time", c, "The time in another city, with its offset from yours.", () => new WorldTime());
        add("date", "Date", c, "The weekday and the full date.", () => new DateWidget());
        add("day-number", "Calendar Page", c, "Today as a tear-off calendar page.", () => new DayNumber());
    }
}

/// <summary>Base for clocks: redraw when the displayed minute (or second) changes.</summary>
internal abstract class ClockBase : DockWidget
{
    private string _last = "";
    protected virtual bool ShowsSeconds => false;
    protected virtual DateTime Now => DateTime.Now;

    public override bool Tick(DateTime now)
    {
        var key = Now.ToString(ShowsSeconds ? "HHmmss" : "HHmm") + WidgetEnv.Clock24h;
        if (key == _last) return false;
        _last = key;
        return true;
    }
}

internal sealed class DigitalClock : ClockBase
{
    public override string Title => "Clock";
    public override double WidthUnits => 2;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var now = Now;
        var time = Text(WidgetEnv.Time(now), r.Height * 0.4, p.Ink, p, FontWeights.SemiBold);
        var ampm = Text(WidgetEnv.AmPm(now), r.Height * 0.17, p.SubInk, p);
        double total = time.Width + (ampm.Text.Length > 0 ? ampm.Width + 3 : 0);
        double x = r.X + (r.Width - total) / 2, y = r.Y + r.Height * 0.12;
        dc.DrawText(time, new Point(x, y));
        if (ampm.Text.Length > 0) dc.DrawText(ampm, new Point(x + time.Width + 3, y + time.Height - ampm.Height - 2));
        Centered(dc, now.ToString("ddd d MMM"), new Point(r.X + r.Width / 2, r.Bottom - r.Height * 0.2), r.Height * 0.17, p.SubInk, p, r.Width - 8);
    }
}

internal sealed class FlipClock : ClockBase
{
    public override string Title => "Flip Clock";
    public override double WidthUnits => 2;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var now = Now;
        string hh = WidgetEnv.Clock24h ? now.ToString("HH") : now.ToString("hh"), mm = now.ToString("mm");
        double gap = r.Width * 0.04, w = (r.Width - gap) / 2;
        Card(dc, new Rect(r.X, r.Y, w, r.Height), hh, p);
        Card(dc, new Rect(r.X + w + gap, r.Y, w, r.Height), mm, p);
    }

    private static readonly Brush CardFill = Frozen(new LinearGradientBrush(Color.FromRgb(0x3A, 0x3A, 0x3E), Color.FromRgb(0x1C, 0x1C, 0x1F), 90));
    private static readonly Pen Split = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1.4));
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static void Card(DrawingContext dc, Rect r, string digits, WidgetPaint p)
    {
        dc.DrawRoundedRectangle(CardFill, null, r, 7, 7);
        Centered(dc, digits, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Height * 0.62, Brushes.White, p, r.Width - 4,
            FontWeights.Bold);
        dc.DrawLine(Split, new Point(r.X, r.Y + r.Height / 2), new Point(r.Right, r.Y + r.Height / 2));
    }
}

internal sealed class SegmentClock : ClockBase
{
    public override string Title => "Segment Clock";
    public override double WidthUnits => 2.2;
    private static readonly Brush Lit = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x3D));

    // Segments a..g for 0-9.
    private static readonly string[] Map = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var now = Now;
        Centered(dc, now.ToString("ddd d").ToUpperInvariant(), new Point(r.X + r.Width / 2, r.Y + r.Height * 0.17), r.Height * 0.15, p.SubInk, p);
        string digits = (WidgetEnv.Clock24h ? now.ToString("HH") : now.ToString("hh")) + now.ToString("mm");
        double dh = r.Height * 0.5, dw = dh * 0.5, gap = dw * 0.35, colon = dw * 0.5;
        double total = dw * 4 + gap * 2 + colon, x = r.X + (r.Width - total) / 2, y = r.Y + r.Height * 0.36;
        for (int i = 0; i < 4; i++)
        {
            Digit(dc, digits[i] - '0', new Rect(x, y, dw, dh), p);
            x += dw + (i == 1 ? colon : gap);
            if (i == 1)
            {
                double cx = x - colon / 2;
                dc.DrawEllipse(Lit, null, new Point(cx, y + dh * 0.3), 1.6, 1.6);
                dc.DrawEllipse(Lit, null, new Point(cx, y + dh * 0.7), 1.6, 1.6);
            }
        }
    }

    private static void Digit(DrawingContext dc, int d, Rect r, WidgetPaint p)
    {
        double t = Math.Max(2, r.Width * 0.17), w = r.Width, h = r.Height / 2;
        (char Seg, Rect Box)[] segs =
        {
            ('a', new Rect(r.X + t * 0.6, r.Y, w - t * 1.2, t)), ('g', new Rect(r.X + t * 0.6, r.Y + h - t / 2, w - t * 1.2, t)),
            ('d', new Rect(r.X + t * 0.6, r.Bottom - t, w - t * 1.2, t)),
            ('f', new Rect(r.X, r.Y + t * 0.6, t, h - t * 0.9)), ('b', new Rect(r.Right - t, r.Y + t * 0.6, t, h - t * 0.9)),
            ('e', new Rect(r.X, r.Y + h + t * 0.3, t, h - t * 0.9)), ('c', new Rect(r.Right - t, r.Y + h + t * 0.3, t, h - t * 0.9))
        };
        foreach (var (seg, box) in segs)
            dc.DrawRoundedRectangle(Map[d].Contains(seg) ? Lit : p.Track, null, box, t / 2, t / 2);
    }
}

internal sealed class AnalogClock : ClockBase
{
    public override string Title => "Analog Clock";
    protected override bool ShowsSeconds => true;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p) => DrawFace(dc, r, Now, p, seconds: true);

    public static void DrawFace(DrawingContext dc, Rect r, DateTime t, WidgetPaint p, bool seconds)
    {
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double rad = Math.Min(r.Width, r.Height) / 2 - 2;
        dc.DrawEllipse(p.Tile, p.TileRim, c, rad, rad);
        var tick = new Pen(p.SubInk, 1.2);
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6, inner = rad * (i % 3 == 0 ? 0.76 : 0.84);
            dc.DrawLine(tick, new Point(c.X + Math.Sin(a) * inner, c.Y - Math.Cos(a) * inner),
                new Point(c.X + Math.Sin(a) * rad * 0.92, c.Y - Math.Cos(a) * rad * 0.92));
        }
        void Hand(double turns, double length, double width, Brush b)
        {
            double a = turns * 2 * Math.PI;
            dc.DrawLine(new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                c, new Point(c.X + Math.Sin(a) * length, c.Y - Math.Cos(a) * length));
        }
        Hand((t.Hour % 12 + t.Minute / 60.0) / 12, rad * 0.5, 3, p.Ink);
        Hand((t.Minute + t.Second / 60.0) / 60, rad * 0.74, 2.2, p.Ink);
        if (seconds) Hand(t.Second / 60.0, rad * 0.8, 1, p.Warn);
        dc.DrawEllipse(p.Ink, null, c, 2.2, 2.2);
    }
}

internal sealed class WorldTime : ClockBase
{
    public override string Title => $"Time in {City}";
    public override double WidthUnits => 2;
    public override bool HasCard => true;

    private TimeZoneInfo Zone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(Get("zone", "GMT Standard Time")); }
            catch { return TimeZoneInfo.Utc; }
        }
    }

    private string City => Get("city", "London");
    protected override DateTime Now => TimeZoneInfo.ConvertTime(DateTime.Now, Zone);

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var there = Now;
        double diff = Math.Round((there - DateTime.Now).TotalHours * 2) / 2;
        string offset = diff == 0 ? "same time" : $"{(diff > 0 ? "+" : "")}{diff:0.#}h";
        Centered(dc, City.ToUpperInvariant(), new Point(r.X + r.Width / 2, r.Y + r.Height * 0.2), r.Height * 0.15, p.SubInk, p, r.Width - 8);
        Centered(dc, WidgetEnv.Time(there) + (WidgetEnv.Clock24h ? "" : " " + WidgetEnv.AmPm(there)),
            new Point(r.X + r.Width / 2, r.Y + r.Height * 0.5), r.Height * 0.33, p.Ink, p, r.Width - 8);
        Centered(dc, offset, new Point(r.X + r.Width / 2, r.Bottom - r.Height * 0.18), r.Height * 0.14, p.SubInk, p);
    }

    /// <summary>Pick the city: a name to show and one of Windows' time zones.</summary>
    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 300 };
        var name = new TextBox { Text = City, Margin = new Thickness(0, 4, 0, 8) };
        var zones = new ComboBox { ItemsSource = TimeZoneInfo.GetSystemTimeZones(), DisplayMemberPath = "DisplayName" };
        zones.SelectedItem = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z => z.Id == Zone.Id);
        var save = new Button { Content = "Save", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 3, 14, 3) };
        save.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text)) Set("city", name.Text.Trim());
            if (zones.SelectedItem is TimeZoneInfo z) Set("zone", z.Id);
            Host?.Redraw();
            Host?.CloseCard();
        };
        panel.Children.Add(new TextBlock { Text = "City name" });
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "Time zone" });
        panel.Children.Add(zones);
        panel.Children.Add(save);
        return panel;
    }
}

internal sealed class DateWidget : ClockBase
{
    public override string Title => "Date";
    public override double WidthUnits => 2;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var now = Now;
        Centered(dc, now.ToString("dddd").ToUpperInvariant(), new Point(r.X + r.Width / 2, r.Y + r.Height * 0.3), r.Height * 0.17, p.Bad, p, r.Width - 8);
        Centered(dc, now.ToString("MMMM d"), new Point(r.X + r.Width / 2, r.Y + r.Height * 0.62), r.Height * 0.26, p.Ink, p, r.Width - 10);
    }
}

internal sealed class DayNumber : ClockBase
{
    public override string Title => "Calendar Page";

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var now = Now;
        double rad = Math.Min(12, r.Height * 0.2), header = r.Height * 0.3;
        dc.DrawRoundedRectangle(Brushes.White, p.TileRim, r, rad, rad);
        var top = new RectangleGeometry(new Rect(r.X, r.Y, r.Width, header + rad), rad, rad);
        dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Y, r.Width, header)));
        dc.DrawGeometry(p.Bad, null, top);
        dc.Pop();
        Centered(dc, now.ToString("MMM").ToUpperInvariant(), new Point(r.X + r.Width / 2, r.Y + header / 2), header * 0.6, Brushes.White, p);
        Centered(dc, now.Day.ToString(), new Point(r.X + r.Width / 2, r.Y + header + (r.Height - header) / 2), (r.Height - header) * 0.7,
            Brushes.Black, p, r.Width - 6, FontWeights.Bold);
    }
}
