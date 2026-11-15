using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Weather;

/// <summary>
/// Base for everything that shows the weather city's forecast: weather tiles and the clocks with a temperature.
/// Reads the one shared city (<see cref="WeatherSettings"/>) and the shared fetch (<see cref="WeatherCache"/>).
/// </summary>
internal abstract class WeatherTile : OnlineWidget<WeatherData>
{
    private int _seen = -1;
    private string? _place;
    private string _minute = "";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(20);
    protected override bool CanFetch => WeatherSettings.Place != null;
    protected override WeatherData? Sample => WeatherSample.Data;
    protected override Task<WeatherData> FetchAsync(CancellationToken ct) => WeatherCache.Get(WeatherSettings.Place!, ct);

    /// <summary>Tiles that follow the clock (hours, the sun, clocks) redraw each minute.</summary>
    protected virtual bool EveryMinute => false;
    protected virtual string? NoCityNote => null;
    protected string City => Shown?.City ?? WeatherSettings.Place?.Name ?? "";

    public override bool HasCard => true;

    protected override bool TickLive(DateTime now)
    {
        bool changed = false;
        if (_seen != WeatherSettings.Version) { _seen = WeatherSettings.Version; changed = true; }
        // The city changed in some tile's card: drop this tile's forecast (or error) for the old one.
        string? place = WeatherSettings.Place?.Key;
        if (_place != place)
        {
            bool first = _place == null && Data == null && Error == null;
            _place = place;
            if (!first) { RefreshSoon(); changed = true; }
        }
        // A fetch for the old city that was still running when it changed.
        else if (Data != null && place != null && Data.Key != place) { RefreshSoon(); changed = true; }
        if (EveryMinute)
        {
            var m = now.ToString("yyyyMMddHHmm") + WidgetEnv.Clock24h;
            if (m != _minute) { _minute = m; changed = true; }
        }
        return changed;
    }

    public override FrameworkElement BuildCard(WidgetPaint p) =>
        WeatherCard.Build(p, Shown != null && !IsSample ? Summary(Shown, p) : null, () =>
        {
            RefreshSoon();
            Host?.Redraw();
            Host?.CloseCard();
        }, NoCityNote);

    /// <summary>The forecast in a few lines, for the card.</summary>
    private FrameworkElement Summary(WeatherData d, WidgetPaint p)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = p.Ink, LineHeight = 19 };
        string Temp(double c) => WeatherSettings.Temp(c);
        t.Inlines.Add(new System.Windows.Documents.Run($"{Temp(d.Temp)} {WeatherGlyphs.Words(d.Code, d.Day).ToLowerInvariant()} in {d.City}") { FontSize = 15, FontWeight = FontWeights.SemiBold });
        var lines = new List<string> { $"Feels like {Temp(d.FeelsLike)} · humidity {d.Humidity:0}% · wind {WeatherSettings.Wind(d.Wind)} from the {Compass(d.WindFrom)}" };
        if (d.Today is { } today)
            lines.Add($"Today {Temp(today.Min)} to {Temp(today.Max)}, {today.RainChance}% chance of rain · sun {WidgetEnv.Time(today.Sunrise)}–{WidgetEnv.Time(today.Sunset)}");
        foreach (var day in d.FromToday().Skip(1).Take(3))
            lines.Add($"{day.Date:dddd}: {WeatherGlyphs.Words(day.Code).ToLowerInvariant()}, {Temp(day.Min)} to {Temp(day.Max)}");
        foreach (var l in lines) t.Inlines.Add(new System.Windows.Documents.Run("\n" + l) { FontSize = 12 });
        if (UpdatedAt is DateTime u) t.Inlines.Add(new System.Windows.Documents.Run($"\nUpdated {WidgetEnv.Time(u)} {WidgetEnv.AmPm(u)}") { FontSize = 11, Foreground = p.SubInk });
        return t;
    }

    public static string Compass(double degrees) =>
        new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" }[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];

    protected static string Temp(double celsius) => WeatherSettings.Temp(celsius);

    /// <summary>Weather tiles: the tile, the forecast (or loading/error), and the SAMPLE mark on top.</summary>
    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (IsSample ? Shown != null : DrawState(dc, r, p)) DrawWeather(dc, r, p, Shown!);
        if (IsSample) Markets.MarketDraw.SampleBadge(dc, r, p);
    }

    protected virtual void DrawWeather(DrawingContext dc, Rect r, WidgetPaint p, WeatherData d) { }

    /// <summary>Hour label for a forecast column: "14" or "2PM".</summary>
    protected static string HourLabel(DateTime t) => WidgetEnv.Clock24h ? t.ToString("HH") : t.ToString("%h") + t.ToString("tt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();

    /// <summary>Left-aligned single-line text, vertically centred on <paramref name="cy"/>, trimmed to <paramref name="maxWidth"/>.</summary>
    protected static void Left(DrawingContext dc, string s, double x, double cy, double size, Brush ink, WidgetPaint p, double maxWidth, FontWeight? weight = null)
    {
        var t = Text(s, size, ink, p, weight);
        t.MaxTextWidth = Math.Max(1, maxWidth);
        t.MaxLineCount = 1;
        t.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(t, new Point(x, cy - t.Height / 2));
    }
}
