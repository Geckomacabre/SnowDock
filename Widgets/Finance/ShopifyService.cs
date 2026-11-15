using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Shopify: orders from the GraphQL Admin API, with a static token or a Dev Dashboard app's client credentials.</summary>
internal sealed class ShopifyService : FinanceService
{
    public override string Id => "shopify";
    public override string Name => "Shopify";
    public override string KeyLabel => "Client ID:Client secret, or an Admin API access token (shpat_…)";
    public override string? DashboardUrl => "https://dev.shopify.com/dashboard";
    public override AccountField[] Fields => new[] { new AccountField("shop", "Store domain", "your-store.myshopify.com") };
    public override string[] Steps => new[]
    {
        "In the Shopify Dev Dashboard, create an app for your store. On its version, select only the read_orders scope, release it and install the app on the store.",
        "From the app's Settings, copy the Client ID and Client secret and paste them below as ClientID:ClientSecret. SnowDock swaps them for a 24-hour token when it needs one.",
        "Already have a custom app with read_orders and an Admin API access token (shpat_…)? Paste that token instead.",
        "Enter the store's .myshopify.com domain."
    };
    public override string NetShort => "paid minus refunded, tax and shipping included, before Shopify's fees";
    public override string NetLong =>
        "what customers paid for the orders placed each day minus what was refunded on them, in your shop currency (Shopify's net payment). " +
        "Tax and shipping are included and Shopify's fees aren't taken off. Test orders are left out.";
    public override RevenueData Sample => RevenueData.Sample(41, 780, "USD");

    private const string ApiVersion = "2026-10";
    private const int MaxPages = 20; // 5,000 orders in 60 days

    private const string Query = """
        query Orders($cursor: String, $q: String) {
          orders(first: 250, after: $cursor, query: $q, sortKey: CREATED_AT, reverse: true) {
            pageInfo { hasNextPage endCursor }
            nodes { createdAt test netPaymentSet { shopMoney { amount currencyCode } } }
          }
        }
        """;

    /// <summary>Client-credential tokens last 24 hours; kept in memory only, per shop and credentials.</summary>
    private static readonly Dictionary<string, (string Token, DateTime Expires)> Tokens = new();

    public override async Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        string shop = ShopHost(Field(fields, "shop"));
        string token = await AccessToken(shop, key, ct).ConfigureAwait(false);
        var b = new RevenueBuilder();
        string filter = $"created_at:>='{Iso(b.StartUtc)}'";
        string? cursor = null;
        var oldest = DateTimeOffset.MaxValue;
        for (int page = 1, retries = 0; ; )
        {
            // GraphQL Admin API, orders query (newest first, 250 a page); needs read_orders, which reaches the last 60 days.
            // https://shopify.dev/docs/api/admin-graphql/latest/queries/orders
            // Fields (createdAt, test, netPaymentSet): https://shopify.dev/docs/api/admin-graphql/latest/objects/Order
            var body = new { query = Query, variables = new { cursor, q = filter } };
            using var doc = await Web.PostJson($"https://{shop}/admin/api/{ApiVersion}/graphql.json", body,
                r => r.Headers.Add("X-Shopify-Access-Token", token), ct).ConfigureAwait(false);
            if (Throttled(doc.RootElement) && retries++ < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * retries), ct).ConfigureAwait(false); // the bucket refills each second
                continue;
            }
            retries = 0;
            var orders = Data(doc.RootElement).GetProperty("orders");
            foreach (var o in orders.GetProperty("nodes").EnumerateArray())
            {
                var when = Time(o, "createdAt");
                if (when < oldest) oldest = when;
                if (Flag(o, "test")) continue;
                var money = o.GetProperty("netPaymentSet").GetProperty("shopMoney");
                b.Add(when, Str(money, "currencyCode"), Decimal(money, "amount"));
            }
            var info = orders.GetProperty("pageInfo");
            cursor = Str(info, "endCursor");
            if (!Flag(info, "hasNextPage") || cursor.Length == 0) break;
            if (page++ >= MaxPages) { b.Truncated(oldest); break; }
            await WaitForPoints(doc.RootElement, ct).ConfigureAwait(false);
        }
        return b.Build("USD");
    }

    // Query cost and the leaky bucket (extensions.cost.throttleStatus, restoreRate in points a second):
    // https://shopify.dev/docs/apps/build/apis/graphql-admin/rate-limits
    private static bool Throttled(JsonElement root) =>
        root.TryGetProperty("errors", out var errors) && errors.ToString().Contains("THROTTLED", StringComparison.OrdinalIgnoreCase);

    /// <summary>Before the next page, wait until the bucket holds enough points for another query like this one.</summary>
    private static Task WaitForPoints(JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("extensions", out var ext) || !ext.TryGetProperty("cost", out var cost)
            || !cost.TryGetProperty("throttleStatus", out var status)) return Task.CompletedTask;
        double need = Decimal(cost, "requestedQueryCost"), have = Decimal(status, "currentlyAvailable"), rate = Decimal(status, "restoreRate");
        if (have >= need || rate <= 0) return Task.CompletedTask;
        return Task.Delay(TimeSpan.FromSeconds(Math.Min(20, (need - have) / rate + 0.5)), ct);
    }

    /// <summary>GraphQL reports problems inside a 200 reply.</summary>
    private static JsonElement Data(JsonElement root)
    {
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            string text = errors.ToString();
            if (text.Contains("THROTTLED", StringComparison.OrdinalIgnoreCase)) throw new WebError("Rate limited: will retry");
            if (text.Contains("ACCESS_DENIED", StringComparison.OrdinalIgnoreCase) || text.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
                throw new WebError("Key needs the read_orders scope");
            throw new WebError("Unexpected reply");
        }
        return root.GetProperty("data");
    }

    /// <summary>A pasted shpat_ token as is, or a 24-hour token for ClientID:ClientSecret.</summary>
    private static async Task<string> AccessToken(string shop, string key, CancellationToken ct)
    {
        int colon = key.IndexOf(':');
        if (colon <= 0 || key.StartsWith("shpat_", StringComparison.Ordinal)) return key;
        string cacheKey = shop + "\n" + key;
        lock (Tokens)
            if (Tokens.TryGetValue(cacheKey, out var t) && t.Expires > DateTime.UtcNow.AddMinutes(10)) return t.Token;

        // Client credentials grant: form-encoded POST to the shop's token endpoint; tokens expire after 24 hours.
        // https://shopify.dev/docs/apps/build/authentication-authorization/client-credentials-grant
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = key[..colon].Trim(), ["client_secret"] = key[(colon + 1)..].Trim()
        };
        JsonDocument doc;
        try
        {
            doc = await Web.Send(HttpMethod.Post, $"https://{shop}/admin/oauth/access_token", null,
                r => r.Content = new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        }
        catch (WebError e) when (e.Status is System.Net.HttpStatusCode.BadRequest)
        {
            throw new WebError("Client ID/secret refused", e.Status); // or the app isn't installed on this store
        }
        using (doc)
        {
            string token = Str(doc.RootElement, "access_token");
            if (token.Length == 0) throw new WebError("Unexpected reply");
            long seconds = Math.Max(600, Long(doc.RootElement, "expires_in"));
            lock (Tokens) Tokens[cacheKey] = (token, DateTime.UtcNow.AddSeconds(seconds));
            return token;
        }
    }

    /// <summary>"your-store", "https://your-store.myshopify.com/admin" → "your-store.myshopify.com". Nothing else is accepted,
    /// so the token is only ever sent to Shopify.</summary>
    private static string ShopHost(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.StartsWith("https://")) s = s[8..];
        else if (s.StartsWith("http://")) s = s[7..];
        s = s.Split('/')[0];
        if (!s.Contains('.')) s += ".myshopify.com";
        if (!Regex.IsMatch(s, @"^[a-z0-9][a-z0-9-]*\.myshopify\.com$"))
            throw new WebError("Store domain: use *.myshopify.com");
        return s;
    }
}
