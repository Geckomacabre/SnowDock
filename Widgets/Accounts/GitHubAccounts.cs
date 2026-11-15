using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

[WidgetFamily(74)]
internal static class OnlineAccounts
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Online;
        add("github", "GitHub Inbox", c, "How many GitHub notifications you haven't read yet.", () => new GitHubInboxTile());
        add("github-activity", "GitHub Activity", c, "Your GitHub contributions over the last 17 weeks as a grid, with your current streak.", () => new GitHubActivityTile());
        add("github-overview", "GitHub Overview", c, "Unread GitHub notifications, your open pull requests and the stars on repositories you own.", () => new GitHubOverviewTile());
        add("leetcode", "LeetCode", c, "LeetCode problems you've solved by difficulty, with your current daily streak.", () => new LeetCodeTile());
        add("gmail", "Gmail", c, "Unread conversations in your Gmail inbox (needs your own Google OAuth client).", () => new GmailTile());
    }
}

/// <summary>What every GitHub tile shares: one classic token, saved once for all three.</summary>
internal abstract class GitHubTile<T> : ServiceTile<T> where T : class
{
    protected override string Service => "github";
    protected override string ServiceName => "GitHub";
    protected override string KeyLabel => "Personal access token (classic)";
    protected override string Badge => "GITHUB";
    protected override Color BadgeColor => Color.FromRgb(0x8B, 0x94, 0x9E);
    protected override string? DashboardUrl => "https://github.com/settings/tokens";
    protected override string[] Steps => new[]
    {
        "On GitHub, open Settings > Developer settings > Personal access tokens > Tokens (classic) and generate a new token.",
        "Tick only the notifications scope (GitHub's notifications API takes classic tokens only). Add read:user if the activity grid should count private contributions.",
        "There's no read-only notifications scope, but SnowDock only reads: it never marks anything as read.",
        "Paste the token below and click Save. All GitHub widgets share it."
    };
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected static void Auth(HttpRequestMessage req, string token)
    {
        Web.Bearer(req, token);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>Unread notifications, counted page by page (50 a page) up to 250.</summary>
    protected static async Task<(int Count, bool More)> UnreadAsync(string token, CancellationToken ct)
    {
        int count = 0;
        for (int page = 1; page <= 5; page++)
        {
            // List notifications for the authenticated user (unread only by default, per_page max 50; classic PAT with
            // the notifications or repo scope): https://docs.github.com/en/rest/activity/notifications
            using var doc = await Web.GetJson($"https://api.github.com/notifications?per_page=50&page={page}", req => Auth(req, token), ct).ConfigureAwait(false);
            int n = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
            count += n;
            if (n < 50) return (count, false);
        }
        return (count, true);
    }

    /// <summary>GitHub's GraphQL endpoint: https://docs.github.com/en/graphql/guides/forming-calls-with-graphql</summary>
    protected static Task<JsonDocument> QueryAsync(string token, string query, object? variables, CancellationToken ct) =>
        SvcJson.GraphQL("https://api.github.com/graphql", query, variables, req => Auth(req, token), ct, "GitHub user not found: check the card");
}

internal sealed record GitHubInbox(int Unread, bool More);

internal sealed class GitHubInboxTile : GitHubTile<GitHubInbox>
{
    protected override GitHubInbox Sample => new(7, false);
    public override string Title => Shown is { } d ? $"GitHub: {Figure(d)} unread notification{(d.Unread == 1 ? "" : "s")}" : "GitHub";
    private static string Figure(GitHubInbox d) => d.Unread + (d.More ? "+" : "");

    protected override async Task<GitHubInbox> FetchAsync(CancellationToken ct)
    {
        var (n, more) = await UnreadAsync(Key, ct).ConfigureAwait(false);
        return new GitHubInbox(n, more);
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, GitHubInbox d)
    {
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height * 0.45);
        Centered(dc, Figure(d), c, r.Height * 0.38, d.Unread > 0 ? p.Ink : p.SubInk, p, r.Width - Pad(r) * 2);
        Centered(dc, "GitHub", new Point(c.X, r.Y + r.Height * 0.8), Small(r), p.SubInk, p, r.Width - 6, FontWeights.Normal);
        if (d.Unread > 0) dc.DrawEllipse(p.Accent, null, new Point(r.X + r.Height * 0.16, r.Y + r.Height * 0.16), 3, 3);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p)
    {
        if (Shown is not { } d) return null;
        var text = new TextBlock { Text = $"Unread notifications: {Figure(d)}\n" };
        var open = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Open notifications")) { NavigateUri = new Uri("https://github.com/notifications") };
        open.RequestNavigate += (_, e) => DockWindow.ShellOpen(e.Uri.AbsoluteUri);
        text.Inlines.Add(open);
        return text;
    }
}

internal sealed record GitHubDay(DateTime Date, int Count, int Level);
internal sealed record GitHubCalendar(string Login, GitHubDay[] Days, int Total, int Streak);

internal sealed class GitHubActivityTile : GitHubTile<GitHubCalendar>
{
    private const int Weeks = 17;
    public override double WidthUnits => 3.2;
    protected override bool ShowBadge => false;
    protected override AccountField[] Fields => new[] { new AccountField("login", "GitHub username", "leave blank for the token's owner") };
    // The username is optional: blank means the token's owner.
    protected override bool CanFetch => KeyStore.Has(Service);
    public override string Title => Shown is { } d ? $"GitHub: {d.Login} · {d.Total:#,0} contributions in a year · {d.Streak}-day streak" : "GitHub Activity";

    private GitHubCalendar? _sample;
    protected override GitHubCalendar Sample => _sample ??= MakeSample();

    private static GitHubCalendar MakeSample()
    {
        // A plausible year: busier on weekdays, some quiet days, and a current run of 9 days.
        var today = DateTime.Today;
        var days = new List<GitHubDay>();
        for (int i = 364; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            int seed = (d.DayOfYear * 7919 + d.Year) % 23;
            int n = i < 9 ? 1 + seed % 6 : i == 9 ? 0
                : d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? (seed > 17 ? seed % 3 : 0) : (seed > 5 ? seed % 9 : 0);
            days.Add(new GitHubDay(d, n, n == 0 ? 0 : n < 3 ? 1 : n < 5 ? 2 : n < 7 ? 3 : 4));
        }
        var all = days.ToArray();
        return new GitHubCalendar("octo-snow", all, all.Sum(x => x.Count), Streak(all));
    }

    private const string Days = "login contributionsCollection { contributionCalendar { totalContributions weeks { contributionDays { date contributionCount contributionLevel } } } }";

    protected override async Task<GitHubCalendar> FetchAsync(CancellationToken ct)
    {
        string login = Field("login").TrimStart('@');
        // User.contributionsCollection defaults to the last year; contributionCalendar gives every day with a count and
        // a quartile level: https://docs.github.com/en/graphql/reference/users (ContributionCalendar / ContributionCalendarDay)
        using var doc = login.Length > 0
            ? await QueryAsync(Key, $"query($login: String!) {{ user(login: $login) {{ {Days} }} }}", new { login }, ct).ConfigureAwait(false)
            : await QueryAsync(Key, $"query {{ viewer {{ {Days} }} }}", null, ct).ConfigureAwait(false);
        var data = doc.RootElement.Get("data");
        var user = login.Length > 0 ? data.Get("user") : data.Get("viewer");
        if (user.ValueKind != JsonValueKind.Object) throw new WebError("GitHub user not found: check the card");
        var cal = user.Get("contributionsCollection").Get("contributionCalendar");
        var days = cal.Get("weeks").Items().SelectMany(w => w.Get("contributionDays").Items())
            .Select(x => new GitHubDay(
                DateTime.TryParseExact(x.Str("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue,
                (int)x.Num("contributionCount"), Level(x.Str("contributionLevel"))))
            .Where(x => x.Date != DateTime.MinValue).OrderBy(x => x.Date).ToArray();
        return new GitHubCalendar(user.Str("login"), days, (int)cal.Num("totalContributions"), Streak(days));
    }

    private static int Level(string s) => s switch
    {
        "FIRST_QUARTILE" => 1, "SECOND_QUARTILE" => 2, "THIRD_QUARTILE" => 3, "FOURTH_QUARTILE" => 4, _ => 0
    };

    /// <summary>Days in a row with contributions, ending today (or yesterday, when today has none yet).</summary>
    private static int Streak(GitHubDay[] days)
    {
        int i = days.Length - 1, n = 0;
        if (i >= 0 && days[i].Count == 0) i--;
        for (; i >= 0 && days[i].Count > 0; i--) n++;
        return n;
    }

    private static readonly Brush[] LevelInk = new byte[] { 80, 140, 200, 255 }
        .Select(a => Freeze(new SolidColorBrush(Color.FromArgb(a, 0x34, 0xC7, 0x59)))).ToArray();

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, GitHubCalendar d)
    {
        double pad = Pad(r), pitch = (r.Height - pad * 2) / 7, cell = pitch * 0.8;
        if (d.Days.Length > 0)
        {
            // Columns are weeks starting on Sunday, like GitHub's own calendar; the last column is this week.
            var last = d.Days[^1].Date;
            var start = last.AddDays(-(int)last.DayOfWeek - 7 * (Weeks - 1));
            foreach (var day in d.Days.Where(x => x.Date >= start))
            {
                int col = (int)((day.Date - start).TotalDays / 7), row = (int)day.Date.DayOfWeek;
                var box = new Rect(r.X + pad + col * pitch, r.Y + pad + row * pitch, cell, cell);
                dc.DrawRoundedRectangle(day.Level == 0 ? p.Track : LevelInk[day.Level - 1], null, box, cell * 0.25, cell * 0.25);
            }
        }
        double x = r.X + pad * 2 + Weeks * pitch, w = r.Right - pad - x;
        Fit(dc, d.Streak.ToString(), x, r.Y + r.Height * 0.22, r.Height * 0.36, d.Streak > 0 ? p.Ink : p.SubInk, p, w, FontWeights.SemiBold);
        Fit(dc, "day streak", x, r.Y + r.Height * 0.68, Small(r), p.SubInk, p, w, FontWeights.Normal);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"{d.Login}: {d.Total:#,0} contributions in the last year\nCurrent streak: {d.Streak} day{(d.Streak == 1 ? "" : "s")}" }
        : null;
}
