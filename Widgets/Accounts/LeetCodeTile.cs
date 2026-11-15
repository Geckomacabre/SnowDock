using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

internal sealed record LeetCodeLevel(int Solved, int Total);
internal sealed record LeetCodeStats(string User, LeetCodeLevel Easy, LeetCodeLevel Medium, LeetCodeLevel Hard, int Streak, int ActiveDays)
{
    public int Solved => Easy.Solved + Medium.Solved + Hard.Solved;
}

/// <summary>A public LeetCode profile: problems solved by difficulty and the current run of days with a submission.</summary>
internal sealed class LeetCodeTile : ServiceTile<LeetCodeStats>
{
    protected override string Service => "leetcode";
    protected override string ServiceName => "LeetCode";
    protected override string KeyLabel => "";
    protected override bool NeedsKey => false;
    protected override string Badge => "LEETCODE";
    protected override Color BadgeColor => Color.FromRgb(0xFF, 0xA1, 0x16);
    protected override string? DashboardUrl => "https://leetcode.com/";
    protected override AccountField[] Fields => new[] { new AccountField("username", "LeetCode username", "as in leetcode.com/u/<username>") };
    protected override string[] Steps => new[]
    {
        "Type your LeetCode username below and click Save. Profiles are public, so no key or sign-in is needed.",
        "LeetCode has no official public API. SnowDock reads the same public profile data the leetcode.com site uses, so this may break if LeetCode changes it."
    };
    public override double WidthUnits => 2.5;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    protected override LeetCodeStats Sample => new("snowcoder", new(142, 976), new(201, 2148), new(37, 990), 6, 118);
    public override string Title => Shown is { } d ? $"LeetCode {d.User}: {d.Solved} solved · {d.Streak}-day streak" : "LeetCode";

    // Solved counts by difficulty, the totals available, and the submission calendar (a JSON string of
    // {unix day start: submissions} for the last year).
    private const string Query = """
        query($username: String!) {
          allQuestionsCount { difficulty count }
          matchedUser(username: $username) {
            username
            submitStatsGlobal { acSubmissionNum { difficulty count } }
            userCalendar { totalActiveDays submissionCalendar }
          }
        }
        """;

    protected override async Task<LeetCodeStats> FetchAsync(CancellationToken ct)
    {
        string user = Field("username");
        // No official docs exist; this is the public GraphQL endpoint leetcode.com's own profile pages call. Checked live
        // against a well-known public profile on 2026-11-15.
        using var doc = await SvcJson.GraphQL("https://leetcode.com/graphql", Query, new { username = user },
            req => req.Headers.Referrer = new Uri("https://leetcode.com/"), ct, "LeetCode user not found: check the card").ConfigureAwait(false);
        var data = doc.RootElement.Get("data");
        var u = data.Get("matchedUser");
        if (u.ValueKind != JsonValueKind.Object) throw new WebError("LeetCode user not found: check the card");
        int Of(JsonElement list, string level) => (int)(list.Items().FirstOrDefault(x => x.Str("difficulty") == level) is var e && e.ValueKind == JsonValueKind.Object ? e.Num("count") : 0);
        var solved = u.Get("submitStatsGlobal").Get("acSubmissionNum");
        var all = data.Get("allQuestionsCount");
        var cal = u.Get("userCalendar");
        return new LeetCodeStats(u.Str("username"),
            new(Of(solved, "Easy"), Of(all, "Easy")), new(Of(solved, "Medium"), Of(all, "Medium")), new(Of(solved, "Hard"), Of(all, "Hard")),
            Streak(cal.Str("submissionCalendar")), (int)cal.Num("totalActiveDays"));
    }

    /// <summary>Days in a row (UTC, as LeetCode counts them) with a submission, ending today or yesterday.</summary>
    private static int Streak(string calendarJson)
    {
        if (string.IsNullOrEmpty(calendarJson)) return 0;
        var active = new HashSet<DateTime>();
        using (var cal = JsonDocument.Parse(calendarJson))
            foreach (var day in cal.RootElement.EnumerateObject())
                if (long.TryParse(day.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && day.Value.ValueKind == JsonValueKind.Number && day.Value.GetInt32() > 0)
                    active.Add(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.Date);
        var d = DateTime.UtcNow.Date;
        if (!active.Contains(d)) d = d.AddDays(-1);
        int n = 0;
        while (active.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, LeetCodeStats d)
    {
        double pad = Pad(r), left = r.X + r.Width * 0.47;
        Headline(dc, r, p, d.Solved.ToString("#,0", CultureInfo.CurrentCulture), d.Streak > 0 ? $"{d.Streak}-day streak" : "no streak",
            d.Streak > 0 ? p.Warn : p.SubInk, left - r.X - pad * 1.4);
        // One row per difficulty: the count, then a bar of solved / available.
        double s = Small(r) * 0.95, row = r.Height * 0.2, top = r.Y + r.Height * 0.3, numW = r.Width * 0.15;
        var rows = new[] { (d.Easy, p.Good), (d.Medium, p.Warn), (d.Hard, p.Bad) };
        for (int i = 0; i < rows.Length; i++)
        {
            var (lv, ink) = rows[i];
            double y = top + row * i;
            var t = Text(lv.Solved.ToString(CultureInfo.CurrentCulture), s, p.Ink, p);
            dc.DrawText(t, new Point(left + numW - t.Width, y + (row - t.Height) / 2));
            var bar = new Rect(left + numW + 3, y + row / 2 - 1.75, r.Right - pad - left - numW - 3, 3.5);
            Bar(dc, bar, lv.Total > 0 ? (double)lv.Solved / lv.Total : 0, ink, p);
        }
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        // The shared card ends with a note about the stored key; there's no key here.
        var card = base.BuildCard(p);
        if (card is ScrollViewer { Content: StackPanel panel } && panel.Children.Count > 0 && panel.Children[^1] is TextBlock note)
            note.Text = "Nothing secret is stored: SnowDock only reads your public profile from leetcode.com.";
        return card;
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{d.User}: {d.Solved} solved\nEasy {d.Easy.Solved} of {d.Easy.Total}  ·  Medium {d.Medium.Solved} of {d.Medium.Total}  ·  Hard {d.Hard.Solved} of {d.Hard.Total}\n"
                + $"Current streak: {d.Streak} day{(d.Streak == 1 ? "" : "s")}  ·  {d.ActiveDays} active days this year"
        }
        : null;
}
