using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.Weather;

/// <summary>
/// A clock or calendar tile that adds the weather city's temperature when one is set. Without a city it simply
/// leaves the temperature out (no sample numbers next to a real clock); its card is where the city is set.
/// </summary>
internal abstract class WeatherClock : WeatherTile
{
    protected override WeatherData? Sample => null;
    protected override bool EveryMinute => true;
    protected override string? NoCityNote => "Pick a city to add its temperature to this tile.";

    /// <summary>The forecast if a city is set and it has arrived.</summary>
    protected WeatherData? Weather => IsSample ? null : Data;

    protected static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

internal sealed class SevenDays : WeatherClock
{
    public override string Title => DateTime.Now.ToString("D");
    public override double WidthUnits => 3.8;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var today = DateTime.Now.Date;
        var fmt = CultureInfo.CurrentCulture.DateTimeFormat;
        int back = ((int)today.DayOfWeek - (int)fmt.FirstDayOfWeek + 7) % 7;
        var first = today.AddDays(-back);
        double h = r.Height, pad = h * 0.12, right = r.Right - pad;
        var w = Weather;
        if (w != null) right -= h * 0.8;
        double col = (right - r.X - pad) / 7;
        for (int i = 0; i < 7; i++)
        {
            var day = first.AddDays(i);
            double cx = r.X + pad + col * (i + 0.5);
            bool isToday = day == today;
            if (isToday)
                dc.DrawRoundedRectangle(p.Bad, null, new Rect(cx - col / 2 + 1, r.Y + h * 0.1, col - 2, h * 0.8), h * 0.14, h * 0.14);
            Brush top = isToday ? Brushes.White : p.SubInk, num = isToday ? Brushes.White : p.Ink;
            Centered(dc, fmt.GetShortestDayName(day.DayOfWeek), new Point(cx, r.Y + h * 0.3), h * 0.16, top, p, col - 2);
            Centered(dc, day.Day.ToString(), new Point(cx, r.Y + h * 0.63), h * 0.25, num, p, col - 2,
                isToday ? FontWeights.Bold : FontWeights.SemiBold);
        }
        if (w != null)
        {
            double cx = right + h * 0.42;
            WeatherGlyphs.Draw(dc, new Point(cx, r.Y + h * 0.37), h * 0.42, w.Code, w.Day, p);
            Centered(dc, Temp(w.Temp), new Point(cx, r.Y + h * 0.76), h * 0.2, p.Ink, p, h * 0.85);
        }
    }
}

internal sealed class TodayTile : WeatherClock
{
    public override string Title => Weather is { } w ? $"{DateTime.Now:D} · {Temp(w.Temp)} in {w.City}" : DateTime.Now.ToString("D");
    public override double WidthUnits => 2.6;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var now = DateTime.Now;
        var w = Weather;
        double h = r.Height, side = w != null ? h * 0.95 : 0, left = r.X + h * 0.12, width = r.Width - side - h * 0.2;
        double cx = left + width / 2;
        Centered(dc, now.ToString("dddd").ToUpperInvariant(), new Point(cx, r.Y + h * 0.2), h * 0.15, p.Bad, p, width, FontWeights.Bold);
        var time = Text(WidgetEnv.Time(now), h * 0.33, p.Ink, p, FontWeights.SemiBold);
        var ampm = Text(WidgetEnv.AmPm(now), h * 0.15, p.SubInk, p);
        double tw = time.Width + (ampm.Text.Length > 0 ? ampm.Width + 2 : 0), ty = r.Y + h * 0.5 - time.Height / 2;
        dc.DrawText(time, new Point(cx - tw / 2, ty));
        if (ampm.Text.Length > 0) dc.DrawText(ampm, new Point(cx - tw / 2 + time.Width + 2, ty + time.Height - ampm.Height - h * 0.04));
        Centered(dc, now.ToString("M"), new Point(cx, r.Y + h * 0.81), h * 0.16, p.SubInk, p, width, FontWeights.Normal);
        if (w != null)
        {
            double wx = r.Right - side / 2 - h * 0.08;
            dc.DrawLine(new Pen(p.Track, 1), new Point(r.Right - side - h * 0.06, r.Y + h * 0.18), new Point(r.Right - side - h * 0.06, r.Bottom - h * 0.18));
            WeatherGlyphs.Draw(dc, new Point(wx, r.Y + h * 0.38), h * 0.44, w.Code, w.Day, p);
            Centered(dc, Temp(w.Temp), new Point(wx, r.Y + h * 0.77), h * 0.21, p.Ink, p, side - 4);
        }
    }
}

/// <summary>Time, date and temperature as three coloured blocks stacked in one square tile.</summary>
internal sealed class ClockWeatherTile : WeatherClock
{
    private static readonly Brush TimeBlock = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)));
    private static readonly Brush DateBlock = Frozen(new SolidColorBrush(Color.FromRgb(0xE2, 0x4E, 0x1B)));
    private static readonly Brush TempBlock = Frozen(new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D)));

    public override string Title => Weather is { } w ? $"{DateTime.Now:t} · {Temp(w.Temp)} in {w.City}" : DateTime.Now.ToString("f");

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var now = DateTime.Now;
        double pad = Math.Max(1.5, r.Height * 0.04), gap = Math.Max(1.5, r.Height * 0.04);
        double bh = (r.Height - pad * 2 - gap * 2) / 3, rad = Math.Min(8, bh * 0.38);
        dc.DrawRoundedRectangle(p.Tile, p.TileRim, r, Math.Min(14, r.Height * 0.24), Math.Min(14, r.Height * 0.24));
        var w = Weather;
        (Brush Fill, string Text)[] blocks =
        {
            (TimeBlock, WidgetEnv.Time(now)),
            (DateBlock, now.ToString("MMM d", CultureInfo.CurrentCulture).ToUpperInvariant()),
            (TempBlock, w != null ? Temp(w.Temp) : now.ToString("ddd").ToUpperInvariant())
        };
        for (int i = 0; i < 3; i++)
        {
            var b = new Rect(r.X + pad, r.Y + pad + i * (bh + gap), r.Width - pad * 2, bh);
            dc.DrawRoundedRectangle(blocks[i].Fill, null, b, rad, rad);
            Centered(dc, blocks[i].Text, new Point(b.X + b.Width / 2, b.Y + b.Height / 2), bh * 0.72, Brushes.White, p, b.Width - 4, FontWeights.Bold);
        }
    }
}
