using System.Globalization;
using System.Text.Json;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Weather;

internal sealed record WeatherHour(DateTime Time, double Temp, int Code, int RainChance, bool Day);

internal sealed record WeatherDay(DateTime Date, int Code, double Max, double Min, DateTime Sunrise, DateTime Sunset, int RainChance, double WindMax);

/// <summary>
/// One forecast for one place, always in °C and km/h (the °C/°F setting converts when drawing, so it never
/// needs a new fetch). Times are the place's own local time; <see cref="PlaceNow"/> is "now" there.
/// </summary>
internal sealed class WeatherData
{
    public required string Key { get; init; }
    public required string City { get; init; }
    public int UtcOffsetSeconds { get; init; }
    public double Temp { get; init; }
    public double FeelsLike { get; init; }
    public double Humidity { get; init; }
    public double Wind { get; init; }
    public double WindFrom { get; init; }
    public int Code { get; init; }
    public bool Day { get; init; } = true;
    public IReadOnlyList<WeatherHour> Hours { get; init; } = Array.Empty<WeatherHour>();
    public IReadOnlyList<WeatherDay> Days { get; init; } = Array.Empty<WeatherDay>();

    public DateTime PlaceNow => DateTime.UtcNow.AddSeconds(UtcOffsetSeconds);
    public WeatherDay? Today => Days.FirstOrDefault(d => d.Date == PlaceNow.Date) ?? Days.FirstOrDefault();

    /// <summary>Today and the days after it.</summary>
    public IEnumerable<WeatherDay> FromToday() => Days.Where(d => d.Date >= PlaceNow.Date);

    /// <summary>The next <paramref name="n"/> whole hours after now.</summary>
    public IEnumerable<WeatherHour> NextHours(int n) => Hours.Where(h => h.Time > PlaceNow).Take(n);
}

/// <summary>
/// One shared fetch per place for every weather tile and clock: a place's forecast is downloaded at most once
/// per <see cref="Ttl"/>, and tiles asking at the same moment share the request that's already running.
/// </summary>
internal static class WeatherCache
{
    /// <summary>A little shorter than the tiles' 20-minute interval, so their next tick finds it stale.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(19);

    private sealed class Entry
    {
        public WeatherData? Data;
        public DateTime At;
        public Task<WeatherData>? Pending;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new();

    public static Task<WeatherData> Get(WeatherPlace place, CancellationToken ct)
    {
        Task<WeatherData> task;
        lock (Gate)
        {
            if (!Entries.TryGetValue(place.Key, out var e)) Entries[place.Key] = e = new Entry();
            if (e.Data != null && DateTime.UtcNow - e.At < Ttl) return Task.FromResult(e.Data);
            // Not tied to any one tile's token: removing one tile mustn't cancel the fetch for the others.
            task = e.Pending ??= Task.Run(() => Fetch(place));
        }
        return task.WaitAsync(ct);
    }

    private static async Task<WeatherData> Fetch(WeatherPlace place)
    {
        try
        {
            // Forecast API, documented at https://open-meteo.com/en/docs (no key; °C and km/h are the defaults).
            var inv = CultureInfo.InvariantCulture;
            string url = "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={place.Lat.ToString("0.####", inv)}&longitude={place.Lon.ToString("0.####", inv)}" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m,wind_direction_10m" +
                "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max,wind_speed_10m_max" +
                $"&timezone={Uri.EscapeDataString(string.IsNullOrEmpty(place.TimeZone) ? "auto" : place.TimeZone)}&forecast_days=5";
            using var doc = await Web.GetJson(url).ConfigureAwait(false);
            var data = Parse(doc.RootElement, place);
            lock (Gate) Entries[place.Key] = new Entry { Data = data, At = DateTime.UtcNow };
            return data;
        }
        catch
        {
            // Keep the last good forecast, but never cache the failure itself.
            lock (Gate) if (Entries.TryGetValue(place.Key, out var e)) e.Pending = null;
            throw;
        }
    }

    private static WeatherData Parse(JsonElement root, WeatherPlace place)
    {
        var cur = root.GetProperty("current");
        var hourly = root.GetProperty("hourly");
        var daily = root.GetProperty("daily");

        var hours = new List<WeatherHour>();
        var ht = hourly.GetProperty("time");
        for (int i = 0; i < ht.GetArrayLength(); i++)
            hours.Add(new WeatherHour(Time(ht[i]), Num(hourly, "temperature_2m", i), (int)Num(hourly, "weather_code", i),
                (int)Num(hourly, "precipitation_probability", i), Num(hourly, "is_day", i, 1) > 0));

        var days = new List<WeatherDay>();
        var dt = daily.GetProperty("time");
        for (int i = 0; i < dt.GetArrayLength(); i++)
            days.Add(new WeatherDay(Time(dt[i]).Date, (int)Num(daily, "weather_code", i), Num(daily, "temperature_2m_max", i),
                Num(daily, "temperature_2m_min", i), SunTime(daily, "sunrise", i), SunTime(daily, "sunset", i),
                (int)Num(daily, "precipitation_probability_max", i), Num(daily, "wind_speed_10m_max", i)));

        return new WeatherData
        {
            Key = place.Key, City = place.Name,
            UtcOffsetSeconds = root.TryGetProperty("utc_offset_seconds", out var off) ? off.GetInt32() : 0,
            Temp = Val(cur, "temperature_2m"), FeelsLike = Val(cur, "apparent_temperature"), Humidity = Val(cur, "relative_humidity_2m"),
            Wind = Val(cur, "wind_speed_10m"), WindFrom = Val(cur, "wind_direction_10m"), Code = (int)Val(cur, "weather_code"),
            Day = Val(cur, "is_day", 1) > 0, Hours = hours, Days = days
        };
    }

    private static DateTime Time(JsonElement e) =>
        DateTime.Parse(e.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Sunrise or sunset; in polar day or night there may be none, which reads as midnight.</summary>
    private static DateTime SunTime(JsonElement daily, string name, int i)
    {
        try { return Time(daily.GetProperty(name)[i]); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        { return Time(daily.GetProperty("time")[i]); }
    }

    private static double Val(JsonElement obj, string name, double fallback = 0) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static double Num(JsonElement obj, string name, int i, double fallback = 0)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array || i >= arr.GetArrayLength()) return fallback;
        return arr[i].ValueKind == JsonValueKind.Number ? arr[i].GetDouble() : fallback;
    }
}

/// <summary>Made-up but plausible weather, shown (marked SAMPLE) until a city is chosen.</summary>
internal static class WeatherSample
{
    private static WeatherData? _data;
    private static DateTime _hour;

    /// <summary>Rebuilt each hour so its days and hours move along with the clock.</summary>
    public static WeatherData Data
    {
        get
        {
            var hour = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            if (_data == null || _hour != hour) { _data = Build(); _hour = hour; }
            return _data;
        }
    }

    private static WeatherData Build()
    {
        var now = DateTime.Now;
        var today = now.Date;
        int[] codes = { 2, 3, 61, 1, 0 };
        double[] max = { 19, 17, 15, 18, 21 }, min = { 12, 11, 10, 11, 13 };
        var days = Enumerable.Range(0, 5).Select(i => new WeatherDay(today.AddDays(i), codes[i], max[i], min[i],
            today.AddDays(i).AddHours(7).AddMinutes(12 + i), today.AddDays(i).AddHours(17).AddMinutes(48 - i),
            new[] { 10, 30, 80, 5, 0 }[i], 18 + i)).ToList();
        var hours = Enumerable.Range(-2, 30).Select(i =>
        {
            var t = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(i);
            double temp = 15.5 + 4 * Math.Sin((t.Hour - 9) / 24.0 * 2 * Math.PI);
            bool day = t.Hour is >= 7 and < 18;
            return new WeatherHour(t, temp, t.Hour % 5 == 0 ? 3 : day ? 2 : 0, t.Hour % 4 * 10, day);
        }).ToList();
        return new WeatherData
        {
            Key = "sample", City = "Lisbon", UtcOffsetSeconds = (int)TimeZoneInfo.Local.GetUtcOffset(now).TotalSeconds,
            Temp = 17, FeelsLike = 16, Humidity = 64, Wind = 14, WindFrom = 315, Code = 2,
            Day = now.Hour is >= 7 and < 18, Hours = hours, Days = days
        };
    }
}
