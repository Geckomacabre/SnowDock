using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Dodo Payments: the balance ledger (sales in, tax, fees and refunds out), in its US-dollar equivalents.</summary>
internal sealed class DodoService : FinanceService
{
    public override string Id => "dodo-payments";
    public override string Name => "Dodo Payments";
    public override string KeyLabel => "API key";
    public override string? DashboardUrl => "https://app.dodopayments.com";
    public override string[] Steps => new[]
    {
        "In the Dodo Payments dashboard (Live Mode on), open Developer › API Keys and choose Create New Key.",
        "Name it SnowDock. Where it asks for permissions, give read-only access to balances and nothing else.",
        "Copy the key (it's shown once, starting dodo_live_) and paste it below. A dodo_test_ key reads test data instead."
    };
    public override string NetShort => "after tax, fees, refunds and disputes";
    public override string NetLong =>
        "what your Dodo Payments balances gained from sales: payments minus the tax Dodo remits for you, its fees, refunds and disputes, " +
        "using the US-dollar equivalent Dodo gives for each entry. Payouts, reserves and Dodo credits don't count.";
    public override RevenueData Sample => RevenueData.Sample(37, 160, "USD");

    // Ledger events that aren't about sales. Event types and their credit/debit sense:
    // https://docs.dodopayments.com/features/account-summary-payout-wallet
    private static readonly HashSet<string> NotSales = new()
    {
        "payout", "payout_fees", "payout_reversal", "payout_fees_reversal", "reserve_hold", "reserve_release",
        "currency_conversion", "dodo_credits"
    };
    private const int PageSize = 100, MaxPages = 50;

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var b = new RevenueBuilder();
        // Test keys go to the test host, live keys to the live one.
        string host = key.StartsWith("dodo_test", StringComparison.OrdinalIgnoreCase) ? "https://test.dodopayments.com" : "https://live.dodopayments.com";
        var oldest = DateTimeOffset.MaxValue;
        for (int page = 0; ; page++)
        {
            // GET /balances/ledger with created_at_gte, page_size (max 100) and page_number.
            // https://docs.dodopayments.com/api-reference/balance-ledger/list-ledger-entries
            string url = $"{host}/balances/ledger?created_at_gte={Uri.EscapeDataString(Iso(b.StartUtc))}&page_size={PageSize}&page_number={page}";
            using var doc = await Web.GetJson(url, r => Web.Bearer(r, key), ct).ConfigureAwait(false);
            int n = 0;
            foreach (var e in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                n++;
                var when = Time(e, "created_at");
                if (when < oldest) oldest = when;
                if (NotSales.Contains(Str(e, "event_type"))) continue;
                long usd = Long(e, "usd_equivalent_amount");
                b.AddMinor(when, "USD", Flag(e, "is_credit") ? usd : -usd);
            }
            if (n < PageSize) break;
            if (page + 1 >= MaxPages) { b.Truncated(oldest); break; }
        }
        return b.Build("USD");
    }
}
