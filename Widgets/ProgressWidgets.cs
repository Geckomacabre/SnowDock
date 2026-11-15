using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets;

internal static class ProgressWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Clocks;
        add("day-progress", "Day Progress", c, "How much of today has gone, as a ring.", () => new PeriodRing(PeriodRing.Span.Day));
        add("month-progress", "Month Progress", c, "How much of this month has gone, as a ring.", () => new PeriodRing(PeriodRing.Span.Month));
        add("year-progress", "Year Progress", c, "The year's weeks as dots, filling in as they pass.", () => new YearDots());
    }
}

internal sealed class PeriodRing : DockWidget
{
    public enum Span { Day, Month }
    private readonly Span _span;
    private int _lastPercent = -1;

    public PeriodRing(Span span) => _span = span;
    public override string Title => _span == Span.Day ? "Day Progress" : "Month Progress";

    private double Fraction(DateTime now)
    {
        if (_span == Span.Day) return now.TimeOfDay.TotalSeconds / 86400;
        var start = new DateTime(now.Year, now.Month, 1);
        return (now - start).TotalSeconds / (start.AddMonths(1) - start).TotalSeconds;
    }

    public override bool Tick(DateTime now)
    {
        int pct = (int)(Fraction(now) * 100);
        if (pct == _lastPercent) return false;
        _lastPercent = pct;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var now = DateTime.Now;
        double f = Fraction(now);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        Ring(dc, c, r.Height * 0.36, f, _span == Span.Day ? p.Warn : p.Accent, p, Math.Max(3, r.Height * 0.08));
        Centered(dc, $"{(int)(f * 100)}%", new Point(c.X, c.Y - r.Height * 0.04), r.Height * 0.2, p.Ink, p);
        Centered(dc, _span == Span.Day ? "DAY" : now.ToString("MMM").ToUpperInvariant(), new Point(c.X, c.Y + r.Height * 0.15),
            r.Height * 0.11, p.SubInk, p);
    }
}

internal sealed class YearDots : DockWidget
{
    private int _lastWeek = -1;
    public override string Title => $"Year Progress ({DateTime.Now.Year})";
    public override double WidthUnits => 2;

    public override bool Tick(DateTime now)
    {
        int week = now.DayOfYear / 7;
        if (week == _lastWeek) return false;
        _lastWeek = week;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var now = DateTime.Now;
        int passed = now.DayOfYear / 7;
        const int cols = 13, rows = 4; // 52 weeks
        double pad = r.Height * 0.16, cw = (r.Width - pad * 2) / cols, ch = (r.Height - pad * 2 - r.Height * 0.12) / rows;
        double dot = Math.Min(cw, ch) * 0.32;
        for (int i = 0; i < cols * rows; i++)
        {
            var c = new Point(r.X + pad + cw * (i % cols + 0.5), r.Y + pad + ch * (i / cols + 0.5));
            dc.DrawEllipse(i < passed ? p.Ink : i == passed ? p.Warn : p.Track, null, c, dot, dot);
        }
        Centered(dc, $"{now.Year} · {(int)(now.DayOfYear * 100.0 / (DateTime.IsLeapYear(now.Year) ? 366 : 365))}%",
            new Point(r.X + r.Width / 2, r.Bottom - pad * 0.9), r.Height * 0.12, p.SubInk, p);
    }
}
