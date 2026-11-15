using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Gumroad: successful sales (newest first, 10 a page), price less Gumroad's fee.</summary>
internal sealed class GumroadService : FinanceService
{
    public override string Id => "gumroad";
    public override string Name => "Gumroad";
    public override string KeyLabel => "Access token";
    public override string? DashboardUrl => "https://gumroad.com/settings/advanced";
    public override string[] Steps => new[]
    {
        "In Gumroad, open Settings › Advanced and create an application (any name; the redirect URI can be http://127.0.0.1).",
        "On the application's page, choose Generate access token.",
        "Copy the token and paste it below. SnowDock only uses it to read sales (the view_sales scope)."
    };
    public override string NetShort => "sales minus Gumroad's fee; refunded sales excluded";
    public override string NetLong =>
        "the price of each successful sale minus Gumroad's fee, in US dollars, dated by sale. Refunded and charged-back sales are left out; " +
        "partly refunded ones count in full. Payment processor fees taken outside Gumroad aren't included.";
    public override RevenueData Sample => RevenueData.Sample(79, 60, "USD");

    private const int MaxPages = 60; // the API returns 10 sales a page, so up to 600 sales in 60 days

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var b = new RevenueBuilder();
        // "after" is a date; ask from the day before and trim to the window here.
        string after = b.Start.AddDays(-1).ToString("yyyy-MM-dd");
        string? pageKey = null;
        var oldest = DateTimeOffset.MaxValue;
        for (int page = 1; ; page++)
        {
            // GET /v2/sales with after=YYYY-MM-DD and page_key (from next_page_key); needs the view_sales scope.
            // https://gumroad.com/api#sales — the docs pass access_token as a parameter; the API's OAuth layer also takes
            // it as a Bearer header, which keeps the token out of the URL.
            string url = $"https://api.gumroad.com/v2/sales?after={after}" + (pageKey != null ? "&page_key=" + Uri.EscapeDataString(pageKey) : "");
            using var doc = await Web.GetJson(url, r => Web.Bearer(r, key), ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == System.Text.Json.JsonValueKind.False)
                throw new WebError("Key refused: check it in the card");
            foreach (var s in root.GetProperty("sales").EnumerateArray())
            {
                var when = Time(s, "created_at");
                if (when < oldest) oldest = when;
                if (Flag(s, "refunded") || Flag(s, "chargedback")) continue;
                // price and gumroad_fee are in cents (US dollars).
                b.AddMinor(when, "USD", Long(s, "price") - Long(s, "gumroad_fee"));
            }
            pageKey = Str(root, "next_page_key");
            if (pageKey.Length == 0) break;
            if (page >= MaxPages) { b.Truncated(oldest); break; }
        }
        return b.Build("USD");
    }
}
