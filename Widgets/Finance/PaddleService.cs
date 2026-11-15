using System.Text.Json;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Paddle Billing: completed transactions' earnings after adjustments, in your payout currency.</summary>
internal sealed class PaddleService : FinanceService
{
    public override string Id => "paddle";
    public override string Name => "Paddle";
    public override string KeyLabel => "API key (pdl_live_apikey_…)";
    public override string? DashboardUrl => "https://vendors.paddle.com";
    public override string[] Steps => new[]
    {
        "In Paddle Billing, go to Paddle › My account › Settings › Authentication and open the API keys tab.",
        "Create a new API key named SnowDock with only the transaction.read permission.",
        "Copy the key and paste it below. A sandbox key (pdl_sdbx_…) reads your sandbox account."
    };
    public override string NetShort => "after tax, Paddle fees and refunds";
    public override string NetLong =>
        "the earnings Paddle pays out for each completed transaction, after tax, Paddle's fee and any refunds, credits or chargebacks on it, " +
        "in your payout currency and dated by when the transaction was billed.";
    public override RevenueData Sample => RevenueData.Sample(67, 340, "EUR");

    private const int MaxPages = 60; // 30 a page, so 1,800 transactions in 60 days

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var b = new RevenueBuilder();
        // Sandbox keys only work against the sandbox host.
        string host = key.StartsWith("pdl_sdbx_", StringComparison.Ordinal) ? "https://sandbox-api.paddle.com" : "https://api.paddle.com";
        // GET /transactions: status, billed_at[GTE], order_by, per_page (max 30); needs transaction.read; Bearer key.
        // https://developer.paddle.com/api-reference/transactions/list-transactions
        // https://developer.paddle.com/api-reference/about/authentication
        string? url = $"{host}/transactions?status=completed&billed_at%5BGTE%5D={Uri.EscapeDataString(Iso(b.StartUtc))}"
            + "&order_by=billed_at%5BDESC%5D&per_page=30";
        var oldest = DateTimeOffset.MaxValue;
        for (int page = 1; url != null; page++)
        {
            using var doc = await Web.GetJson(url, r =>
            {
                Web.Bearer(r, key);
                r.Headers.Add("Skip-Count", "true");
            }, ct).ConfigureAwait(false);
            var root = doc.RootElement;
            foreach (var t in root.GetProperty("data").EnumerateArray())
            {
                if (Str(t, "billed_at").Length == 0) continue;
                var when = Time(t, "billed_at");
                if (when < oldest) oldest = when;
                // details.adjusted_payout_totals: earnings after refunds/chargebacks, in the payout currency (null until completed).
                // Amounts are strings in the currency's smallest unit.
                // https://developer.paddle.com/api-reference/transactions/get-transaction
                var details = t.GetProperty("details");
                if (!details.TryGetProperty("adjusted_payout_totals", out var totals) || totals.ValueKind != JsonValueKind.Object)
                    if (!details.TryGetProperty("payout_totals", out totals) || totals.ValueKind != JsonValueKind.Object) continue;
                b.AddMinor(when, Str(totals, "currency_code"), Long(totals, "earnings"));
            }
            url = null;
            if (root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("pagination", out var pg) && Flag(pg, "has_more"))
            {
                string next = Str(pg, "next");
                // Only ever follow Paddle's own next-page link back to the same host.
                if (next.StartsWith(host + "/", StringComparison.Ordinal)) url = next;
            }
            if (url != null && page >= MaxPages) { b.Truncated(oldest); break; }
        }
        return b.Build("USD");
    }
}
