using System.Net.Http.Headers;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Lemon Squeezy: orders (newest first), using the US-dollar amounts it records on each.</summary>
internal sealed class LemonSqueezyService : FinanceService
{
    public override string Id => "lemon-squeezy";
    public override string Name => "Lemon Squeezy";
    public override string KeyLabel => "API key";
    public override string? DashboardUrl => "https://app.lemonsqueezy.com/settings/api";
    public override AccountField[] Fields => new[] { new AccountField("store", "Store ID", "optional; empty counts every store") };
    public override string[] OptionalFields => new[] { "store" };
    public override string[] Steps => new[]
    {
        "In Lemon Squeezy, open Settings › API and create a new API key named SnowDock.",
        "Copy the key (it's only shown once) and paste it below.",
        "To count a single store, enter its numeric store ID; leave it empty to add up every store on the account."
    };
    public override string NetShort => "order totals less tax and refunds, in USD";
    public override string NetLong =>
        "each paid order's total minus tax and minus whatever share of it was refunded, using the US-dollar amounts Lemon Squeezy records, " +
        "dated by when the order was placed. Lemon Squeezy's own fees aren't in its API, so they aren't taken off.";
    public override RevenueData Sample => RevenueData.Sample(53, 130, "USD");

    private static readonly HashSet<string> Counted = new() { "paid", "refunded", "partial_refund" };
    private const int MaxPages = 40; // 4,000 orders in 60 days

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var b = new RevenueBuilder();
        string store = Field(fields, "store");
        var oldest = DateTimeOffset.MaxValue;
        for (int page = 1; ; page++)
        {
            // GET /v1/orders, newest first, filter[store_id]: https://docs.lemonsqueezy.com/api/orders/list-all-orders
            // page[size] up to 100 and page[number]: https://docs.lemonsqueezy.com/api/getting-started/requests
            // Order amounts (total_usd, tax_usd, refunded_amount_usd, in cents): https://docs.lemonsqueezy.com/api/orders/the-order-object
            string url = $"https://api.lemonsqueezy.com/v1/orders?page%5Bsize%5D=100&page%5Bnumber%5D={page}"
                + (store.Length > 0 ? "&filter%5Bstore_id%5D=" + Uri.EscapeDataString(store) : "");
            using var doc = await Web.GetJson(url, r =>
            {
                Web.Bearer(r, key);
                r.Headers.Accept.Clear();
                r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            }, ct).ConfigureAwait(false);
            var root = doc.RootElement;
            bool pastWindow = false;
            int n = 0;
            foreach (var order in root.GetProperty("data").EnumerateArray())
            {
                n++;
                var a = order.GetProperty("attributes");
                var when = Time(a, "created_at");
                if (when < oldest) oldest = when;
                if (when < b.StartUtc) { pastWindow = true; continue; }
                if (!Counted.Contains(Str(a, "status"))) continue;
                double total = Long(a, "total_usd"), tax = Long(a, "tax_usd"), refunded = Long(a, "refunded_amount_usd");
                double kept = total > 0 ? Math.Clamp(1 - refunded / total, 0, 1) : 0;
                b.Add(when, "USD", (total - tax) * kept / 100);
            }
            int lastPage = root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("page", out var pg) ? (int)Long(pg, "lastPage") : page;
            if (pastWindow || n == 0 || page >= lastPage) break;
            if (page >= MaxPages) { b.Truncated(oldest); break; }
        }
        return b.Build("USD");
    }
}
