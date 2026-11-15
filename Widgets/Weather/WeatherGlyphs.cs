using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.Weather;

internal enum Sky { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

/// <summary>Weather symbols drawn as simple vector shapes, and WMO weather codes (as Open-Meteo reports them) in words.</summary>
internal static class WeatherGlyphs
{
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static SolidColorBrush Solid(byte r, byte g, byte b) => Frozen(new SolidColorBrush(Color.FromRgb(r, g, b)));

    private static readonly Brush SunFill = Solid(0xFF, 0xB3, 0x1A);
    private static readonly Brush MoonFill = Solid(0xF4, 0xD8, 0x6B);
    private static readonly Pen MoonRim = Frozen(new Pen(Solid(0xC9, 0x9A, 0x1E), 1));
    private static readonly Brush RainInk = Solid(0x3B, 0x8B, 0xF6);
    private static readonly Brush BoltFill = Solid(0xFF, 0xC4, 0x00);
    private static readonly Brush CloudOnLight = Solid(0xD3, 0xDA, 0xE4), CloudOnDark = Solid(0xE9, 0xED, 0xF3);
    private static readonly Brush GreyOnLight = Solid(0x9A, 0xA4, 0xB2), GreyOnDark = Solid(0xA9, 0xB1, 0xBE);
    private static readonly Brush RimOnLight = Solid(0x8C, 0x97, 0xA6), RimOnDark = Solid(0xB8, 0xC0, 0xCC);

    public static Sky SkyOf(int code) => code switch
    {
        0 => Sky.Clear,
        1 or 2 => Sky.PartlyCloudy,
        45 or 48 => Sky.Fog,
        >= 51 and <= 57 => Sky.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => Sky.Rain,
        (>= 71 and <= 77) or 85 or 86 => Sky.Snow,
        >= 95 and <= 99 => Sky.Storm,
        _ => Sky.Cloudy
    };

    /// <summary>The WMO code in a few plain words.</summary>
    public static string Words(int code, bool day = true) => code switch
    {
        0 => day ? "Clear" : "Clear night",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 => "Light showers",
        81 => "Showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        >= 95 and <= 99 => code == 95 ? "Thunderstorm" : "Thunderstorm, hail",
        _ => "Cloudy"
    };

    /// <summary>Draw the symbol for <paramref name="code"/> filling a square of side <paramref name="s"/> centred on <paramref name="c"/>.</summary>
    public static void Draw(DrawingContext dc, Point c, double s, int code, bool day, WidgetPaint p)
    {
        var sky = SkyOf(code);
        Brush cloudFill = p.Light ? CloudOnLight : CloudOnDark, darkCloud = p.Light ? GreyOnLight : GreyOnDark;
        Pen cloudRim = new(p.Light ? RimOnLight : RimOnDark, Math.Max(0.8, s * 0.025));
        switch (sky)
        {
            case Sky.Clear:
                if (day) Sun(dc, c, s * 0.2);
                else Moon(dc, c, s * 0.3);
                break;
            case Sky.PartlyCloudy:
                if (day) Sun(dc, new Point(c.X + s * 0.14, c.Y - s * 0.14), s * 0.15);
                else Moon(dc, new Point(c.X + s * 0.14, c.Y - s * 0.16), s * 0.22);
                Cloud(dc, new Point(c.X - s * 0.06, c.Y + s * 0.1), s * 0.78, cloudFill, cloudRim);
                break;
            case Sky.Cloudy:
                Cloud(dc, new Point(c.X + s * 0.12, c.Y - s * 0.1), s * 0.62, darkCloud, cloudRim);
                Cloud(dc, new Point(c.X - s * 0.04, c.Y + s * 0.08), s * 0.84, cloudFill, cloudRim);
                break;
            case Sky.Fog:
                Cloud(dc, new Point(c.X, c.Y - s * 0.1), s * 0.8, cloudFill, cloudRim);
                var fog = new Pen(p.SubInk, s * 0.06) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(fog, new Point(c.X - s * 0.36, c.Y + s * 0.22), new Point(c.X + s * 0.26, c.Y + s * 0.22));
                dc.DrawLine(fog, new Point(c.X - s * 0.24, c.Y + s * 0.36), new Point(c.X + s * 0.38, c.Y + s * 0.36));
                break;
            case Sky.Drizzle:
            case Sky.Rain:
            case Sky.Snow:
            case Sky.Storm:
                var cc = new Point(c.X, c.Y - s * 0.12);
                Cloud(dc, cc, s * 0.86, sky == Sky.Storm ? darkCloud : cloudFill, cloudRim);
                if (sky == Sky.Storm) Bolt(dc, new Point(c.X + s * 0.02, c.Y + s * 0.2), s * 0.34);
                else Fall(dc, c, s, sky, p);
                break;
        }
    }

    private static void Sun(DrawingContext dc, Point c, double r)
    {
        var ray = new Pen(SunFill, Math.Max(1.2, r * 0.26)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(ray, new Point(c.X + Math.Cos(a) * r * 1.42, c.Y + Math.Sin(a) * r * 1.42),
                new Point(c.X + Math.Cos(a) * r * 1.85, c.Y + Math.Sin(a) * r * 1.85));
        }
        dc.DrawEllipse(SunFill, null, c, r, r);
    }

    public static void Moon(DrawingContext dc, Point c, double r)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r, r),
            new EllipseGeometry(new Point(c.X + r * 0.55, c.Y - r * 0.35), r * 0.82, r * 0.82));
        dc.DrawGeometry(MoonFill, MoonRim, moon);
    }

    /// <summary>A cloud about <paramref name="w"/> wide, its centre at <paramref name="c"/>.</summary>
    private static void Cloud(DrawingContext dc, Point c, double w, Brush fill, Pen rim)
    {
        Geometry g = new RectangleGeometry(new Rect(c.X - w * 0.42, c.Y - w * 0.02, w * 0.84, w * 0.26), w * 0.13, w * 0.13);
        foreach (var (x, y, r) in new[] { (-0.2, 0.06, 0.17), (0.04, -0.06, 0.25), (0.25, 0.08, 0.15) })
            g = Geometry.Combine(g, new EllipseGeometry(new Point(c.X + w * x, c.Y + w * y), w * r, w * r), GeometryCombineMode.Union, null);
        dc.DrawGeometry(fill, rim, g);
    }

    private static void Fall(DrawingContext dc, Point c, double s, Sky sky, WidgetPaint p)
    {
        double y0 = c.Y + s * 0.24;
        for (int i = 0; i < 3; i++)
        {
            double x = c.X + (i - 1) * s * 0.22 + s * 0.02, y = y0 + (i % 2) * s * 0.08;
            if (sky == Sky.Snow)
            {
                var flake = new Pen(p.Light ? RainInk : Brushes.White, Math.Max(1, s * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                double f = s * 0.07;
                for (int k = 0; k < 3; k++)
                {
                    double a = k * Math.PI / 3 + Math.PI / 2;
                    dc.DrawLine(flake, new Point(x - Math.Cos(a) * f, y + s * 0.04 - Math.Sin(a) * f), new Point(x + Math.Cos(a) * f, y + s * 0.04 + Math.Sin(a) * f));
                }
            }
            else
            {
                double len = sky == Sky.Drizzle ? s * 0.07 : s * 0.16;
                var drop = new Pen(RainInk, Math.Max(1.1, s * (sky == Sky.Drizzle ? 0.05 : 0.055))) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(drop, new Point(x + len * 0.3, y), new Point(x - len * 0.3, y + len));
            }
        }
    }

    private static void Bolt(DrawingContext dc, Point c, double h)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + h * 0.12, c.Y - h * 0.5), true, true);
            ctx.LineTo(new Point(c.X - h * 0.22, c.Y + h * 0.05), true, false);
            ctx.LineTo(new Point(c.X + h * 0.02, c.Y + h * 0.05), true, false);
            ctx.LineTo(new Point(c.X - h * 0.1, c.Y + h * 0.5), true, false);
            ctx.LineTo(new Point(c.X + h * 0.26, c.Y - h * 0.1), true, false);
            ctx.LineTo(new Point(c.X + h * 0.02, c.Y - h * 0.1), true, false);
        }
        dc.DrawGeometry(BoltFill, null, g);
    }

    /// <summary>A raindrop (for rain chance), its tip up.</summary>
    public static void Drop(DrawingContext dc, Point c, double h)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - h * 0.5), true, true);
            ctx.BezierTo(new Point(c.X + h * 0.12, c.Y - h * 0.25), new Point(c.X + h * 0.36, c.Y - h * 0.02), new Point(c.X + h * 0.34, c.Y + h * 0.16), true, false);
            ctx.ArcTo(new Point(c.X - h * 0.34, c.Y + h * 0.16), new Size(h * 0.34, h * 0.34), 0, true, SweepDirection.Clockwise, true, false);
            ctx.BezierTo(new Point(c.X - h * 0.36, c.Y - h * 0.02), new Point(c.X - h * 0.12, c.Y - h * 0.25), new Point(c.X, c.Y - h * 0.5), true, false);
        }
        dc.DrawGeometry(RainInk, null, g);
    }
}
