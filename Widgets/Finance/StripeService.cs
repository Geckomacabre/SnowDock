using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Stripe: sales as they moved through the Stripe balance, read with a restricted key.</summary>
internal sealed class StripeService : FinanceService
{
    public override string Id => "stripe";
    public override string Name => "Stripe";
    public override string KeyLabel => "Restricted API key (rk_live_…)";
    public override string? DashboardUrl => "https://dashboard.stripe.com/apikeys";
    public override string[] Steps => new[]
    {
        "In the Stripe Dashboard, open Developers › API keys and choose Create restricted key.",
        "Name it SnowDock and set Balance to Read. Leave every other permission at None.",
        "Create the key, copy it (it starts with rk_live_) and paste it below."
    };
    public override string NetShort => "after Stripe fees, refunds and disputes";
    public override string NetLong =>
        "payments after Stripe's processing fees, minus refunds and disputes, as they reached your Stripe balance, in your balance currency. " +
        "Payouts and transfers don't count, and neither do separate Stripe charges such as Billing fees.";
    public override RevenueData Sample => RevenueData.Sample(11, 420, "USD");

    // Balance transaction types that are sales or undo them (adjustments cover disputes and failed refunds).
    // Types: https://docs.stripe.com/api/balance_transactions/object
    private static readonly HashSet<string> SaleTypes = new()
        { "charge", "payment", "refund", "payment_refund", "payment_failure_refund", "payment_reversal", "adjustment" };
    private const int MaxPages = 50; // 5,000 transactions in 60 days

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var b = new RevenueBuilder();
        long since = b.StartUtc.ToUnixTimeSeconds();
        string? after = null;
        for (int page = 1; ; page++)
        {
            // GET /v1/balance_transactions: newest first, up to 100 a page, cursor starting_after, filter created[gte].
            // https://docs.stripe.com/api/balance_transactions/list — a restricted key needs balance_read:
            // https://docs.stripe.com/keys/permissions-reference
            string url = $"https://api.stripe.com/v1/balance_transactions?limit=100&created%5Bgte%5D={since}"
                + (after != null ? "&starting_after=" + Uri.EscapeDataString(after) : "");
            using var doc = await Web.GetJson(url, r => Web.Basic(r, key, ""), ct).ConfigureAwait(false);
            var root = doc.RootElement;
            var oldest = DateTimeOffset.MaxValue;
            foreach (var t in root.GetProperty("data").EnumerateArray())
            {
                var when = DateTimeOffset.FromUnixTimeSeconds(Long(t, "created"));
                oldest = when;
                after = Str(t, "id");
                if (SaleTypes.Contains(Str(t, "type"))) b.AddMinor(when, Str(t, "currency"), Long(t, "net"));
            }
            if (!Flag(root, "has_more") || string.IsNullOrEmpty(after)) break;
            if (page >= MaxPages) { b.Truncated(oldest); break; }
        }
        return b.Build("USD");
    }
}
