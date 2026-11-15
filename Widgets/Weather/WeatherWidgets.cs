using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.Weather;

[WidgetFamily(40)]
internal static class WeatherWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Weather;
        add("weather", "Weather", c, "How warm it is right now in your city, plus today's high and low.", () => new WeatherNow());
        add("weather-details", "Weather Details", c, "Wind speed and direction, the chance of rain and today's temperature range.", () => new WeatherDetails());
        add("weather-forecast", "Forecast", c, "Today and the three days after it, each with a symbol and its high and low.", () => new WeatherForecast());
        add("weather-status", "Conditions", c, "The sky in a few words, with your city and the temperature.", () => new WeatherStatus());
        add("weather-hourly", "Hourly Forecast", c, "The coming six hours, each with a symbol and a temperature.", () => new WeatherHourly());
        add("sun", "Sunrise & Sunset", c, "The sun's path from sunrise to sunset and how far along it is right now.", () => new SunWidget());
    }
}

internal sealed class WeatherNow : WeatherTile
{
    public override string Title => IsSample ? "Weather" : $"Weather in {City}";
    public override double WidthUnits => 2.1;

    protected override void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d)
    {
        double h = r.Height, x0 = r.X + h * 0.98, w = r.Right - x0 - h * 0.12, cx = x0 + w / 2;
        WeatherGlyphs.Draw(dc, new Point(r.X + h * 0.52, r.Y + h * 0.52), h * 0.68, d.Code, d.Day, p);
        Centered(dc, Temp(d.Temp), new Point(cx, r.Y + h * 0.43), h * 0.42, p.Ink, p, w);
        if (d.Today is { } t)
            Centered(dc, $"↑{Temp(t.Max)} ↓{Temp(t.Min)}", new Point(cx, r.Y + h * 0.8), h * 0.18, p.SubInk, p, w);
    }
}

internal sealed class WeatherDetails : WeatherTile
{
    public override string Title => IsSample ? "Weather Details" : $"Wind and rain in {City}";
    public override double WidthUnits => 2.7;

    protected override void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d)
    {
        double h = r.Height, col = (r.Width - h * 0.2) / 3, iy = r.Y + h * 0.36, vy = r.Y + h * 0.76, vs = h * 0.19;
        double X(int i) => r.X + h * 0.1 + col * (i + 0.5);

        // Wind: an arrow pointing where the wind blows to.
        double a = (d.WindFrom + 180) * Math.PI / 180, len = h * 0.17;
        var c = new Point(X(0), iy);
        var tip = new Point(c.X + Math.Sin(a) * len, c.Y - Math.Cos(a) * len);
        var tail = new Point(c.X - Math.Sin(a) * len, c.Y + Math.Cos(a) * len);
        var pen = new Pen(p.Accent, Math.Max(1.6, h * 0.05)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawLine(pen, tail, tip);
        foreach (double side in new[] { -1.0, 1.0 })
        {
            double b = a + Math.PI + side * 0.6;
            dc.DrawLine(pen, tip, new Point(tip.X + Math.Sin(b) * len * 0.6, tip.Y - Math.Cos(b) * len * 0.6));
        }
        Centered(dc, WeatherSettings.Wind(d.Wind), new Point(X(0), vy), vs, p.Ink, p, col - 4);

        // Rain chance today.
        WeatherGlyphs.Drop(dc, new Point(X(1), iy), h * 0.34);
        Centered(dc, $"{d.Today?.RainChance ?? 0}%", new Point(X(1), vy), vs, p.Ink, p, col - 4);

        // Today's range, with a marker for now.
        if (d.Today is { } t)
        {
            var bar = new Rect(X(2) - col * 0.36, iy - h * 0.045, col * 0.72, h * 0.09);
            var grad = new LinearGradientBrush(Color.FromRgb(0x5A, 0xA9, 0xF8), Color.FromRgb(0xFF, 0x8A, 0x3D), 0);
            dc.DrawRoundedRectangle(grad, null, bar, bar.Height / 2, bar.Height / 2);
            double f = t.Max - t.Min < 0.1 ? 0.5 : Math.Clamp((d.Temp - t.Min) / (t.Max - t.Min), 0, 1);
            dc.DrawEllipse(p.Light ? Brushes.White : p.Ink, new Pen(p.SubInk, 1), new Point(bar.X + bar.Width * f, bar.Y + bar.Height / 2), h * 0.07, h * 0.07);
            Centered(dc, $"{Temp(t.Min).TrimEnd('°')}–{Temp(t.Max)}", new Point(X(2), vy), vs, p.Ink, p, col - 4);
        }
    }
}

internal sealed class WeatherForecast : WeatherTile
{
    public override string Title => IsSample ? "Forecast" : $"Forecast for {City}";
    public override double WidthUnits => 3.3;
    protected override bool EveryMinute => true; // "today" rolls over at midnight

    protected override void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d)
    {
        var days = d.FromToday().Take(4).ToList();
        double h = r.Height, col = (r.Width - h * 0.12) / 4;
        for (int i = 0; i < days.Count; i++)
        {
            var day = days[i];
            double cx = r.X + h * 0.06 + col * (i + 0.5);
            if (i == 0)
                dc.DrawRoundedRectangle(p.Track, null, new Rect(cx - col / 2 + 2, r.Y + h * 0.08, col - 4, h * 0.84), h * 0.14, h * 0.14);
            Centered(dc, i == 0 ? "Today" : day.Date.ToString("ddd"), new Point(cx, r.Y + h * 0.2), h * 0.165, i == 0 ? p.Ink : p.SubInk, p, col - 4);
            WeatherGlyphs.Draw(dc, new Point(cx, r.Y + h * 0.49), h * 0.38, day.Code, true, p);
            var hi = Text(Temp(day.Max), h * 0.175, p.Ink, p);
            var lo = Text(Temp(day.Min), h * 0.175, p.SubInk, p, FontWeights.Normal);
            double tw = hi.Width + 3 + lo.Width, x = cx - tw / 2, y = r.Y + h * 0.8 - hi.Height / 2;
            dc.DrawText(hi, new Point(x, y));
            dc.DrawText(lo, new Point(x + hi.Width + 3, y));
        }
    }
}

internal sealed class WeatherStatus : WeatherTile
{
    public override string Title => IsSample ? "Conditions" : $"Conditions in {City}";
    public override double WidthUnits => 2.8;

    protected override void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d)
    {
        double h = r.Height, x = r.X + h * 0.98, w = r.Right - x - h * 0.15;
        WeatherGlyphs.Draw(dc, new Point(r.X + h * 0.52, r.Y + h * 0.52), h * 0.68, d.Code, d.Day, p);
        Left(dc, WeatherGlyphs.Words(d.Code, d.Day), x, r.Y + h * 0.38, h * 0.25, p.Ink, p, w);
        Left(dc, $"{d.City} · {Temp(d.Temp)}", x, r.Y + h * 0.68, h * 0.19, p.SubInk, p, w, FontWeights.Normal);
    }
}

internal sealed class WeatherHourly : WeatherTile
{
    public override string Title => IsSample ? "Hourly Forecast" : $"Next hours in {City}";
    public override double WidthUnits => 3.5;
    protected override bool EveryMinute => true;

    protected override void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d)
    {
        var hours = d.NextHours(6).ToList();
        double h = r.Height, col = (r.Width - h * 0.16) / 6;
        for (int i = 0; i < hours.Count; i++)
        {
            var hr = hours[i];
            double cx = r.X + h * 0.08 + col * (i + 0.5);
            Centered(dc, HourLabel(hr.Time), new Point(cx, r.Y + h * 0.2), h * 0.16, p.SubInk, p, col - 2);
            WeatherGlyphs.Draw(dc, new Point(cx, r.Y + h * 0.5), h * 0.36, hr.Code, hr.Day, p);
            Centered(dc, Temp(hr.Temp), new Point(cx, r.Y + h * 0.8), h * 0.18, p.Ink, p, col - 2);
        }
    }
}

internal sealed class SunWidget : WeatherTile
{
    public override string Title => !IsSample && Data?.Today is { } t
        ? $"Sunrise {WidgetEnv.Time(t.Sunrise)} {WidgetEnv.AmPm(t.Sunrise)}, sunset {WidgetEnv.Time(t.Sunset)} {WidgetEnv.AmPm(t.Sunset)}"
        : "Sunrise & Sunset";
    public override double WidthUnits => 2.2;
    protected override bool EveryMinute => true;

    private static readonly Brush SunInk = new SolidColorBrush(Color.FromRgb(0xFF, 0xA4, 0x1B));

    protected override void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d)
    {
        if (d.Today is not { } t) return;
        if (t.Sunset <= t.Sunrise) // polar day or night: Open-Meteo gives no times
        {
            Centered(dc, d.Day ? "Sun up all day" : "Sun stays down today", new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Height * 0.18, p.SubInk, p, r.Width - 10);
            return;
        }
        double h = r.Height, x0 = r.X + h * 0.32, x1 = r.Right - h * 0.32, yb = r.Y + h * 0.66, rx = (x1 - x0) / 2, ry = h * 0.44;
        double cx = (x0 + x1) / 2;
        var now = d.PlaceNow;
        double f = (now - t.Sunrise).TotalMinutes / Math.Max(1, (t.Sunset - t.Sunrise).TotalMinutes);
        bool up = f is > 0 and < 1;

        dc.DrawLine(new Pen(p.Track, 1.2), new Point(r.X + h * 0.12, yb), new Point(r.Right - h * 0.12, yb));
        var track = new Pen(p.SubInk, 1.2) { DashStyle = new DashStyle(new[] { 1.5, 2.5 }, 0), DashCap = PenLineCap.Round };
        dc.DrawGeometry(null, track, Arc(new Point(x0, yb), new Point(x1, yb), rx, ry, large: false));
        if (up)
        {
            double a = Math.PI * (1 - f);
            var sun = new Point(cx + rx * Math.Cos(a), yb - ry * Math.Sin(a));
            dc.DrawGeometry(null, new Pen(SunInk, Math.Max(2, h * 0.055)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Arc(new Point(x0, yb), sun, rx, ry, large: false));
            dc.DrawEllipse(SunInk, new Pen(p.Tile, 1.5), sun, h * 0.1, h * 0.1);
        }
        else WeatherGlyphs.Moon(dc, new Point(cx, yb - ry * 0.55), h * 0.13);

        Centered(dc, WidgetEnv.Time(t.Sunrise), new Point(x0, yb + h * 0.17), h * 0.17, p.SubInk, p);
        Centered(dc, WidgetEnv.Time(t.Sunset), new Point(x1, yb + h * 0.17), h * 0.17, p.SubInk, p);
    }

    private static Geometry Arc(Point from, Point to, double rx, double ry, bool large)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(from, false, false);
            c.ArcTo(to, new Size(rx, ry), 0, large, SweepDirection.Clockwise, true, false);
        }
        return g;
    }
}
