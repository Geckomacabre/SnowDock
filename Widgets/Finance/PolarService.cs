using System.Text.Json;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Polar: its own daily net-revenue metric, read with an organization access token.</summary>
internal sealed class PolarService : FinanceService
{
    public override string Id => "polar";
    public override string Name => "Polar";
    public override string KeyLabel => "Organization access token (polar_oat_…)";
    public override string? DashboardUrl => "https://polar.sh/dashboard";
    public override string[] Steps => new[]
    {
        "In the Polar dashboard, open your organization's Settings › Access Tokens and create a token.",
        "Name it SnowDock, pick an expiry, and select only the metrics:read scope.",
        "Copy the token and paste it below."
    };
    public override string NetShort => "after Polar and payment fees";
    public override string NetLong =>
        "Polar's own net revenue figure: order revenue after Polar's platform fee and the payment processor's fee, by day in your time zone. " +
        "Polar doesn't say whether refunds are taken off; it reports amounts in cents, shown here as US dollars.";
    public override RevenueData Sample => RevenueData.Sample(23, 95, "USD");

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var b = new RevenueBuilder();
        // GET /v1/metrics/ with start_date, end_date, interval=day, timezone; scope metrics:read; currency values in cents.
        // https://polar.sh/docs/api-reference/metrics/get
        string url = "https://api.polar.sh/v1/metrics/"
            + $"?start_date={b.Start:yyyy-MM-dd}&end_date={DateTime.Today:yyyy-MM-dd}&interval=day"
            + $"&timezone={Uri.EscapeDataString(LocalZone())}&metrics=net_revenue";
        using var doc = await Web.GetJson(url, r => Web.Bearer(r, key), ct).ConfigureAwait(false);
        var periods = doc.RootElement.GetProperty("periods");
        int count = periods.GetArrayLength(), i = 0;
        foreach (var period in periods.EnumerateArray())
        {
            // One period a day from start_date to end_date, so the last one is today.
            int daysAgo = count - 1 - i++;
            if (period.TryGetProperty("net_revenue", out var v) && v.ValueKind == JsonValueKind.Number)
                b.AddDay(daysAgo, "USD", v.GetDouble() / 100);
        }
        return b.Build("USD");
    }

    /// <summary>This PC's time zone as an IANA name ("Europe/London"), so Polar's days match ours.</summary>
    private static string LocalZone() =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";
}
