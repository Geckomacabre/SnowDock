using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

[WidgetFamily(78)]
internal static class AnalyticsAccounts
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Analytics;
        add("plausible", "Plausible", c, "Visitors to your site over the last 7 days against the 7 before, from Plausible.", () => new PlausibleTile());
        add("datafast", "DataFast", c, "Daily visitors for two weeks from DataFast, with this week's total and its change.", () => new DataFastTile());
        add("dub", "Dub", c, "Clicks on your Dub short links over the last 7 days, with the change on the week before.", () => new DubTile());
        add("search-console", "Search Console", c, "Clicks from Google Search over the last 28 days and the change (needs your own Google OAuth client).", () => new SearchConsoleTile());
    }
}

/// <summary>This week against last week, with one value per day for both.</summary>
internal sealed record WeekCompare(double ThisWeek, double LastWeek, double[] Daily);

/// <summary>A count for the last 7 days, its change on the 7 before, and 14 daily bars.</summary>
internal abstract class WeekTile : ServiceTile<WeekCompare>
{
    /// <summary>"visitors", "clicks".</summary>
    protected abstract string Noun { get; }
    public override double WidthUnits => 2.4;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    public override string Title => Shown is { } d
        ? $"{ServiceName}: {d.ThisWeek:#,0} {Noun} this week ({Charts.Delta(d.ThisWeek, d.LastWeek)})"
        : ServiceName;

    /// <summary>Sample days scaled so they add up to the sample's weekly totals.</summary>
    protected static WeekCompare SampleWeeks(double thisWeek, double lastWeek)
    {
        double[] shape = { 0.15, 0.16, 0.155, 0.15, 0.14, 0.12, 0.125 };
        return new(thisWeek, lastWeek, shape.Select(s => Math.Round(s * lastWeek)).Concat(shape.Reverse().Select(s => Math.Round(s * thisWeek))).ToArray());
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, WeekCompare d)
    {
        double left = r.X + r.Width * 0.52;
        var (sub, ink) = Change(d.ThisWeek, d.LastWeek, "vs last wk", p);
        Headline(dc, r, p, Charts.Count(d.ThisWeek), sub, ink, left - r.X - Pad(r) * 1.4);
        TwoWeekBars(dc, ChartArea(r, left), d.Daily, p);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Last 7 days: {d.ThisWeek:#,0} {Noun}\nThe 7 days before: {d.LastWeek:#,0} ({Charts.Delta(d.ThisWeek, d.LastWeek)})"
        }
        : null;
}

internal sealed class PlausibleTile : WeekTile
{
    protected override string Service => "plausible";
    protected override string ServiceName => "Plausible";
    protected override string KeyLabel => "Stats API key";
    protected override string Badge => Field("site") is { Length: > 0 } site && !IsSample ? site.ToUpperInvariant() : "PLAUSIBLE";
    protected override Color BadgeColor => Color.FromRgb(0x58, 0x50, 0xEC);
    protected override string Noun => "visitors";
    protected override string? DashboardUrl => "https://plausible.io/settings/api-keys";
    protected override AccountField[] Fields => new[]
    {
        new AccountField("site", "Site domain", "as added to Plausible, e.g. example.com"),
        new AccountField("base", "Plausible address", "self-hosted only; blank = plausible.io")
    };
    protected override string[] Steps => new[]
    {
        "In Plausible, open Settings > API Keys and create a key for the Stats API. Stats API keys can only read statistics.",
        "Type the site's domain exactly as it appears in Plausible.",
        "Paste the key below and click Save."
    };
    // The address is optional, so only the key and the site are required.
    protected override bool CanFetch => KeyStore.Has(Service) && Field("site").Length > 0;
    protected override WeekCompare Sample => _sample ??= SampleWeeks(2_846, 2_512);
    private WeekCompare? _sample;

    protected override async Task<WeekCompare> FetchAsync(CancellationToken ct)
    {
        string key = Key, site = Field("site"), baseUrl = Field("base").TrimEnd('/');
        if (baseUrl.Length == 0) baseUrl = "https://plausible.io";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
            throw new WebError("Plausible address should start with https://");
        var today = DateTime.Today;

        // Stats API v2, POST /api/v2/query with a Bearer key: https://plausible.io/docs/stats-api
        async Task<System.Text.Json.JsonDocument> Query(object body) =>
            await Web.PostJson($"{baseUrl}/api/v2/query", body, req => Web.Bearer(req, key), ct).ConfigureAwait(false);
        static double First(System.Text.Json.JsonDocument doc) =>
            doc.RootElement.Get("results").Items().FirstOrDefault().Get("metrics").Items().FirstOrDefault() is { ValueKind: System.Text.Json.JsonValueKind.Number } n ? n.GetDouble() : 0;

        string[] metrics = { "visitors" };
        using var now = await Query(new { site_id = site, metrics, date_range = new[] { SvcJson.Day(today.AddDays(-6)), SvcJson.Day(today) } });
        using var before = await Query(new { site_id = site, metrics, date_range = new[] { SvcJson.Day(today.AddDays(-13)), SvcJson.Day(today.AddDays(-7)) } });
        using var daily = await Query(new { site_id = site, metrics, date_range = new[] { SvcJson.Day(today.AddDays(-13)), SvcJson.Day(today) }, dimensions = new[] { "time:day" } });
        var byDay = daily.RootElement.Get("results").Items().ToDictionary(
            x => x.Get("dimensions").Items().FirstOrDefault().ToString(),
            x => x.Get("metrics").Items().FirstOrDefault() is { ValueKind: System.Text.Json.JsonValueKind.Number } n ? n.GetDouble() : 0);
        var days = Enumerable.Range(0, 14).Select(i => byDay.GetValueOrDefault(SvcJson.Day(today.AddDays(i - 13)))).ToArray();
        return new WeekCompare(First(now), First(before), days);
    }
}

internal sealed class DataFastTile : WeekTile
{
    protected override string Service => "datafast";
    protected override string ServiceName => "DataFast";
    protected override string KeyLabel => "API key (df_… or dft_…)";
    protected override string Badge => "DATAFAST";
    protected override Color BadgeColor => Color.FromRgb(0xE7, 0x84, 0x68);
    protected override string Noun => "visitors";
    protected override string? DashboardUrl => "https://datafa.st/dashboard";
    protected override AccountField[] Fields => new[] { new AccountField("website", "Website ID", "only for dft_ account tokens") };
    protected override string[] Steps => new[]
    {
        "Read-only: in DataFast, open Account settings > API and create an account token (dft_) with only the analytics:read permission, limited to this website. Paste it with the website's ID.",
        "Or: open the website's settings > Developer and create a website API key (df_). It covers one site and needs no ID, but it can do more than read.",
        "Paste the key below and click Save."
    };
    protected override bool CanFetch => KeyStore.Has(Service);
    protected override WeekCompare Sample => _sample ??= SampleWeeks(1_932, 2_107);
    private WeekCompare? _sample;

    protected override async Task<WeekCompare> FetchAsync(CancellationToken ct)
    {
        string key = Key, website = Field("website"), zone = SvcJson.IanaZone();
        if (key.StartsWith("dft_", StringComparison.Ordinal) && website.Length == 0) throw new WebError("Account tokens need the website ID: see the card");
        var today = DateTime.Today;
        async Task<(double Total, double[] Days)> Week(DateTime from)
        {
            // Time series of visitors per day; `totals.visitors` counts each visitor once for the range:
            // https://datafa.st/docs/api/website/analytics/timeseries
            string url = $"https://datafa.st/api/v1/analytics/timeseries?fields=visitors&interval=day&startAt={SvcJson.Day(from)}&endAt={SvcJson.Day(from.AddDays(6))}"
                + $"&timezone={Uri.EscapeDataString(zone)}" + (key.StartsWith("dft_", StringComparison.Ordinal) ? $"&websiteId={Uri.EscapeDataString(website)}" : "");
            using var doc = await Web.GetJson(url, req => Web.Bearer(req, key), ct).ConfigureAwait(false);
            var days = doc.RootElement.Get("data").Items().Select(x => x.Num("visitors")).ToList();
            while (days.Count < 7) days.Insert(0, 0);
            return (doc.RootElement.Get("totals").Num("visitors"), days.TakeLast(7).ToArray());
        }
        var now = await Week(today.AddDays(-6)).ConfigureAwait(false);
        var before = await Week(today.AddDays(-13)).ConfigureAwait(false);
        return new WeekCompare(now.Total, before.Total, before.Days.Concat(now.Days).ToArray());
    }
}

internal sealed class DubTile : WeekTile
{
    protected override string Service => "dub";
    protected override string ServiceName => "Dub";
    protected override string KeyLabel => "API key (read only)";
    protected override string Badge => "DUB";
    protected override Color BadgeColor => Color.FromRgb(0x8E, 0x8E, 0x93);
    protected override string Noun => "clicks";
    protected override string? DashboardUrl => "https://app.dub.co/settings/tokens";
    protected override string[] Steps => new[]
    {
        "In Dub, open Settings > API Keys and create a key with the Read only permission.",
        "Dub's docs list API analytics as a Pro plan (or higher) feature.",
        "Paste the key below and click Save."
    };
    protected override WeekCompare Sample => _sample ??= SampleWeeks(5_318, 4_206);
    private WeekCompare? _sample;

    protected override async Task<WeekCompare> FetchAsync(CancellationToken ct)
    {
        string key = Key;
        var today = DateTime.UtcNow.Date; // days in UTC, Dub's default time zone
        // Click analytics as a time series since a start date: https://dub.co/docs/api-reference/endpoint/retrieve-analytics
        using var doc = await Web.GetJson($"https://api.dub.co/analytics?event=clicks&groupBy=timeseries&start={SvcJson.Day(today.AddDays(-13))}",
            req => Web.Bearer(req, key), ct).ConfigureAwait(false);
        var byDay = new Dictionary<DateTime, double>();
        foreach (var b in doc.RootElement.Items())
            if (SvcJson.Time(b.Str("start")) is { } t)
                byDay[t.UtcDateTime.Date] = byDay.GetValueOrDefault(t.UtcDateTime.Date) + b.Num("clicks");
        var days = Enumerable.Range(0, 14).Select(i => byDay.GetValueOrDefault(today.AddDays(i - 13))).ToArray();
        return new WeekCompare(days.Skip(7).Sum(), days.Take(7).Sum(), days);
    }
}
