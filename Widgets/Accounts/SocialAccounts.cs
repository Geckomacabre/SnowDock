using System.Net;
using System.Text.Json;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

[WidgetFamily(86)]
internal static class SocialAccounts
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Social;
        add("youtube", "YouTube", c, "Your channel's subscriber count and how it moved this week.", () => new YouTubeTile());
        add("instagram", "Instagram", c, "Followers on your Instagram professional account, with the week's change.", () => new InstagramTile());
        add("x-profile", "X Profile", c, "Followers on your X profile, with the week's change (needs paid X API credits).", () => new XProfileTile());
    }
}

internal sealed record YouTubeStats(string Name, long Subscribers, bool Hidden, long Views, long Videos);

internal sealed class YouTubeTile : FollowerTile<YouTubeStats>
{
    protected override string Service => "youtube";
    protected override string ServiceName => "YouTube";
    protected override string KeyLabel => "Google API key (YouTube Data API v3)";
    protected override string Badge => "YOUTUBE";
    protected override Color BadgeColor => Color.FromRgb(0xFF, 0x00, 0x00);
    protected override string Noun => "subscribers";
    protected override long SampleGain => 140;
    protected override string? DashboardUrl => "https://www.youtube.com/account_advanced"; // shows your channel ID
    protected override AccountField[] Fields => new[] { new AccountField("channel", "Channel ID or @handle", "UC… or @yourname") };
    protected override string[] Steps => new[]
    {
        "In Google Cloud Console, pick or create a project and enable the YouTube Data API v3.",
        "Under APIs & Services > Credentials, create an API key and restrict it to the YouTube Data API v3. An API key only reads public data.",
        "Find your channel ID in YouTube Studio > Settings > Channel > Advanced settings (it starts with UC), or just type your @handle.",
        "Paste both below and click Save. YouTube rounds public subscriber counts to three figures."
    };
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    protected override YouTubeStats Sample => new("Snowfield Studio", 48_200, false, 3_912_455, 86);
    protected override long Count(YouTubeStats d) => d.Subscribers;
    protected override bool Hidden(YouTubeStats d) => d.Hidden;
    protected override string Who(YouTubeStats d) => d.Name;
    protected override string Extra(YouTubeStats d) => $"\n{d.Videos:#,0} videos · {d.Views:#,0} views";

    protected override async Task<YouTubeStats> FetchAsync(CancellationToken ct)
    {
        string key = Key, channel = Field("channel");
        string filter = channel.StartsWith('@') ? "forHandle=" + Uri.EscapeDataString(channel) : "id=" + Uri.EscapeDataString(channel);
        try
        {
            // channels.list, part=snippet,statistics, 1 quota unit: https://developers.google.com/youtube/v3/docs/channels/list
            // The key goes in the X-goog-api-key header rather than the URL: https://cloud.google.com/docs/authentication/api-keys-use
            using var doc = await Web.GetJson($"https://www.googleapis.com/youtube/v3/channels?part=snippet,statistics&{filter}",
                req => req.Headers.Add("x-goog-api-key", key), ct).ConfigureAwait(false);
            var item = doc.RootElement.Get("items").Items().FirstOrDefault();
            if (item.ValueKind != JsonValueKind.Object) throw new WebError("Channel not found: check the card");
            var s = item.Get("statistics");
            return new YouTubeStats(item.Get("snippet").Str("title"), (long)s.Num("subscriberCount"),
                s.Get("hiddenSubscriberCount").ValueKind == JsonValueKind.True, (long)s.Num("viewCount"), (long)s.Num("videoCount"));
        }
        catch (WebError e) when (e.Status == HttpStatusCode.BadRequest) { throw new WebError("Key or channel refused: check the card"); }
    }
}

internal sealed record InstagramStats(string Username, long Followers, long Posts, string? RenewedToken);

internal sealed class InstagramTile : FollowerTile<InstagramStats>
{
    protected override string Service => "instagram";
    protected override string ServiceName => "Instagram";
    protected override string KeyLabel => "Long-lived Instagram user access token";
    protected override string Badge => "INSTAGRAM";
    protected override Color BadgeColor => Color.FromRgb(0xE1, 0x30, 0x6C);
    protected override string Noun => "followers";
    protected override long SampleGain => 63;
    protected override string? DashboardUrl => "https://developers.facebook.com/apps/";
    protected override string[] Steps => new[]
    {
        "This needs an Instagram professional account (Business or Creator).",
        "In Meta for Developers, create an app, add the Instagram product (API setup with Instagram login) and add your account as an Instagram tester.",
        "Generate an access token for your account. It only needs the instagram_business_basic permission, which can read your profile but not post.",
        "Paste the token below and click Save. Tokens last 60 days; while SnowDock runs it renews the token about once a week."
    };
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    protected override InstagramStats Sample => new("snowfield.studio", 12_480, 214, null);
    protected override long Count(InstagramStats d) => d.Followers;
    protected override string Who(InstagramStats d) => "@" + d.Username;
    protected override string Extra(InstagramStats d) => $"\n{d.Posts:#,0} posts";

    private DateTime _renewTried;

    protected override async Task<InstagramStats> FetchAsync(CancellationToken ct)
    {
        string token = Key;
        string? renewed = null;
        if (DateTime.TryParse(Get("renewed", ""), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var last)
            && DateTime.Today - last >= TimeSpan.FromDays(7) && _renewTried != DateTime.Today)
        {
            _renewTried = DateTime.Today;
            try
            {
                // Refresh a long-lived token (must be 24h+ old, not expired; valid 60 days from refresh):
                // https://developers.facebook.com/docs/instagram-platform/reference/refresh_access_token
                using var r = await Web.GetJson("https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token="
                    + Uri.EscapeDataString(token), null, ct).ConfigureAwait(false);
                if (r.RootElement.Str("access_token") is { Length: > 0 } fresh) token = renewed = fresh;
            }
            catch (WebError) { /* keep using the current token; try again tomorrow */ }
        }
        try
        {
            // The app user's profile fields (Instagram API with Instagram Login), token as documented in the query string:
            // https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/get-started
            using var doc = await Web.GetJson("https://graph.instagram.com/me?fields=username,followers_count,media_count&access_token="
                + Uri.EscapeDataString(token), null, ct).ConfigureAwait(false);
            var e = doc.RootElement;
            return new InstagramStats(e.Str("username"), (long)e.Num("followers_count"), (long)e.Num("media_count"), renewed);
        }
        catch (WebError e) when (e.Status == HttpStatusCode.BadRequest) { throw new WebError("Token refused or expired: paste a new one in the card"); }
    }

    /// <summary>On the UI thread: keep a renewed token and remember when, so the next renewal is a week later.</summary>
    protected override bool TickLive(DateTime now)
    {
        if (!IsSample && Data is { } d && !ReferenceEquals(d, _handled))
        {
            _handled = d;
            if (d.RenewedToken != null) KeyStore.Set(Service, d.RenewedToken);
            if (d.RenewedToken != null || Get("renewed", "") == "") Set("renewed", SvcJson.Day(now));
        }
        return base.TickLive(now);
    }

    private InstagramStats? _handled;
}

internal sealed record XStats(string Handle, long Followers, long Following, long Posts);

internal sealed class XProfileTile : FollowerTile<XStats>
{
    protected override string Service => "x-bearer";
    protected override string ServiceName => "X";
    protected override string KeyLabel => "Bearer token (app-only)";
    protected override string Badge => "X";
    protected override Color BadgeColor => Color.FromRgb(0x8E, 0x8E, 0x93);
    protected override string Noun => "followers";
    protected override long SampleGain => -12;
    protected override string? DashboardUrl => "https://developer.x.com/";
    protected override AccountField[] Fields => new[] { new AccountField("username", "X username", "without the @") };
    protected override string[] Steps => new[]
    {
        "The X API is paid: there's no free tier for this. Each lookup is billed to prepaid pay-per-use credits on your X developer account.",
        "SnowDock looks your profile up every 2 hours, about 12 billed reads a day. Check X's current prices before you connect.",
        "In the X Developer Console, create an app and add credits, then open the app's Keys and tokens page.",
        "Copy the app's Bearer Token (app-only: it reads public profiles and can't post), paste it below with your username and click Save."
    };
    protected override TimeSpan Interval => TimeSpan.FromHours(2);
    protected override XStats Sample => new("snowfield", 3_418, 512, 9_870);
    protected override long Count(XStats d) => d.Followers;
    protected override string Who(XStats d) => "@" + d.Handle;
    protected override string Extra(XStats d) => $"\nFollowing {d.Following:#,0} · {d.Posts:#,0} posts";

    protected override async Task<XStats> FetchAsync(CancellationToken ct)
    {
        string token = Key, user = Field("username").TrimStart('@');
        try
        {
            // User lookup by username with public_metrics, app-only Bearer token accepted:
            // https://docs.x.com/x-api/users/get-user-by-username
            using var doc = await Web.GetJson($"https://api.x.com/2/users/by/username/{Uri.EscapeDataString(user)}?user.fields=public_metrics",
                req => Web.Bearer(req, token), ct).ConfigureAwait(false);
            var data = doc.RootElement.Get("data");
            if (data.ValueKind != JsonValueKind.Object) throw new WebError("User not found: check the card");
            var m = data.Get("public_metrics");
            double posts = m.Num("post_count") > 0 ? m.Num("post_count") : m.Num("tweet_count");
            return new XStats(data.Str("username"), (long)m.Num("followers_count"), (long)m.Num("following_count"), (long)posts);
        }
        catch (WebError e) when (e.Status == HttpStatusCode.PaymentRequired) { throw new WebError("No X API credits left: see the card"); }
    }
}
