using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Weather;

/// <summary>A city the user typed and picked, as Open-Meteo's geocoder found it.</summary>
internal sealed class WeatherPlace
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    /// <summary>IANA time zone, e.g. "Europe/London".</summary>
    public string TimeZone { get; set; } = "";

    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Lat:0.###},{Lon:0.###}");
    public string Label => Region.Length > 0 ? $"{Name}, {Region}" : Name;
}

/// <summary>
/// The one weather city and °C/°F choice that every weather tile and weather-aware clock reads, kept in
/// weather.json next to the settings. It's only ever a city typed into a card; SnowDock never works out where
/// you are. <see cref="Version"/> goes up on every change so tiles can notice.
/// </summary>
internal static class WeatherSettings
{
    private sealed class Saved
    {
        public WeatherPlace? Place { get; set; }
        public bool? Fahrenheit { get; set; }
    }

    private static Saved? _saved;
    public static int Version { get; private set; }

    private static Saved Load()
    {
        if (_saved != null) return _saved;
        try
        {
            var file = WidgetEnv.DataFile("weather.json");
            _saved = File.Exists(file) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)) ?? new() : new();
        }
        catch { _saved = new(); }
        return _saved;
    }

    private static void Save()
    {
        Version++;
        try { File.WriteAllText(WidgetEnv.DataFile("weather.json"), JsonSerializer.Serialize(Load(), new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { AppPaths.Log($"Saving weather city failed: {ex.Message}"); }
    }

    public static WeatherPlace? Place => Load().Place;

    /// <summary>°F if chosen, otherwise whatever this PC's region uses.</summary>
    public static bool Fahrenheit => Load().Fahrenheit ?? !RegionInfo.CurrentRegion.IsMetric;

    public static void SetPlace(WeatherPlace place) { Load().Place = place; Save(); }
    public static void SetFahrenheit(bool on) { Load().Fahrenheit = on; Save(); }

    public static double Convert(double celsius) => Fahrenheit ? celsius * 9 / 5 + 32 : celsius;
    public static string Temp(double celsius)
    {
        double v = Math.Round(Convert(celsius));
        return v < 0 ? $"−{-v:0}°" : $"{Math.Abs(v):0}°"; // a real minus sign, and never "-0°"
    }
    public static string Wind(double kmh) => Fahrenheit ? $"{kmh / 1.609344:0} mph" : $"{kmh:0} km/h";

    /// <summary>Cities matching what the user typed (Open-Meteo geocoding, https://open-meteo.com/en/docs/geocoding-api).</summary>
    public static async Task<List<WeatherPlace>> Search(string name, CancellationToken ct = default)
    {
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        using var doc = await Web.GetJson("https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(name)}&count=6&language={lang}&format=json", ct: ct);
        var list = new List<WeatherPlace>();
        if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
        foreach (var r in results.EnumerateArray())
        {
            string S(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            list.Add(new WeatherPlace
            {
                Name = S("name"), Region = string.Join(", ", new[] { S("admin1"), S("country") }.Where(x => x.Length > 0)),
                Lat = r.GetProperty("latitude").GetDouble(), Lon = r.GetProperty("longitude").GetDouble(), TimeZone = S("timezone")
            });
        }
        return list;
    }
}

/// <summary>The card every weather tile and weather-aware clock shares: pick the city and the unit.</summary>
internal static class WeatherCard
{
    /// <param name="detail">Optional extra content shown above the settings (a forecast summary).</param>
    /// <param name="done">Called after the city changed (redraw, close the card).</param>
    public static FrameworkElement Build(WidgetPaint p, FrameworkElement? detail, Action done, string? note = null)
    {
        var panel = new StackPanel { Width = 320 };
        if (detail != null)
        {
            panel.Children.Add(detail);
            panel.Children.Add(new Border { Height = 1, Background = p.Track, Margin = new Thickness(0, 10, 0, 10) });
        }
        var place = WeatherSettings.Place;
        panel.Children.Add(new TextBlock
        {
            Text = place != null ? $"Weather for {place.Label}" : note ?? "No city yet, so the tile shows a sample.",
            TextWrapping = TextWrapping.Wrap, Foreground = p.SubInk, Margin = new Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(new TextBlock { Text = "City", FontSize = 12 });
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 4) };
        var find = new Button { Content = "Find", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(find, Dock.Right);
        var box = LauncherWidgets.Input(p);
        box.FontSize = 13;
        row.Children.Add(find);
        row.Children.Add(box);
        panel.Children.Add(row);
        var status = new TextBlock { FontSize = 12, Foreground = p.SubInk, TextWrapping = TextWrapping.Wrap };
        var results = new StackPanel();
        panel.Children.Add(status);
        panel.Children.Add(results);

        async void Search()
        {
            string q = box.Text.Trim();
            if (q.Length < 2) return;
            results.Children.Clear();
            status.Text = "Searching…";
            try
            {
                var found = await WeatherSettings.Search(q);
                status.Text = found.Count == 0 ? $"No place called \"{q}\" found." : "Pick one:";
                foreach (var f in found)
                {
                    var b = new Button
                    {
                        Content = f.Label, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(0, 2, 0, 0), Background = p.Track, Foreground = p.Ink, BorderThickness = new Thickness(0)
                    };
                    b.Click += (_, _) => { WeatherSettings.SetPlace(f); done(); };
                    results.Children.Add(b);
                }
            }
            catch (Exception ex) { status.Text = Web.Friendly(ex); }
        }
        find.Click += (_, _) => Search();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };

        var units = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        units.Children.Add(new TextBlock { Text = "Units", FontSize = 12, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        foreach (bool f in new[] { false, true })
        {
            var rb = new RadioButton { Content = f ? "°F, mph" : "°C, km/h", IsChecked = WeatherSettings.Fahrenheit == f, Foreground = p.Ink, Margin = new Thickness(0, 0, 14, 0), GroupName = "units" };
            rb.Checked += (_, _) => { if (WeatherSettings.Fahrenheit != f) WeatherSettings.SetFahrenheit(f); };
            units.Children.Add(rb);
        }
        panel.Children.Add(units);
        panel.Children.Add(new TextBlock
        {
            Text = "Weather data by Open-Meteo.com. Only the city you pick is sent there; one city is shared by all weather tiles and clocks.",
            FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });
        panel.Loaded += (_, _) => box.Focus();
        return panel;
    }
}
