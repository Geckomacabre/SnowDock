using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

internal sealed record GitHubOverview(int Unread, bool MoreUnread, int OpenPullRequests, long Stars, int Repos, bool MoreRepos);

/// <summary>Three GitHub numbers side by side: unread notifications, your open pull requests, stars on your repos.</summary>
internal sealed class GitHubOverviewTile : GitHubTile<GitHubOverview>
{
    public override double WidthUnits => 2.6;
    protected override bool ShowBadge => false;
    protected override GitHubOverview Sample => new(7, false, 3, 1_284, 26, false);
    public override string Title => Shown is { } d
        ? $"GitHub: {d.Unread}{(d.MoreUnread ? "+" : "")} unread · {d.OpenPullRequests} open pull requests · {Charts.Count(d.Stars)} stars"
        : "GitHub Overview";

    // The viewer's open pull requests and the stars on repositories they own (forks left out), 100 repos a page.
    // https://docs.github.com/en/graphql/reference/users (User.pullRequests, User.repositories) and
    // https://docs.github.com/en/graphql/reference/repos (Repository.stargazerCount)
    private const string Query = """
        query($after: String) {
          viewer {
            pullRequests(states: OPEN) { totalCount }
            repositories(first: 100, after: $after, ownerAffiliations: OWNER, isFork: false) {
              totalCount
              nodes { stargazerCount }
              pageInfo { hasNextPage endCursor }
            }
          }
        }
        """;

    protected override async Task<GitHubOverview> FetchAsync(CancellationToken ct)
    {
        string token = Key;
        var (unread, more) = await UnreadAsync(token, ct).ConfigureAwait(false);
        int prs = 0, repos = 0;
        long stars = 0;
        bool moreRepos = true;
        string? after = null;
        for (int page = 0; page < 5 && moreRepos; page++)
        {
            using var doc = await QueryAsync(token, Query, new { after }, ct).ConfigureAwait(false);
            var viewer = doc.RootElement.Get("data").Get("viewer");
            if (viewer.ValueKind != JsonValueKind.Object) throw new WebError("Unexpected reply");
            prs = (int)viewer.Get("pullRequests").Num("totalCount");
            var list = viewer.Get("repositories");
            repos = (int)list.Num("totalCount");
            stars += list.Get("nodes").Items().Sum(n => (long)n.Num("stargazerCount"));
            var info = list.Get("pageInfo");
            moreRepos = info.Get("hasNextPage").ValueKind == JsonValueKind.True;
            after = info.Str("endCursor");
        }
        return new GitHubOverview(unread, more, prs, stars, repos, moreRepos);
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, GitHubOverview d)
    {
        double pad = Pad(r), w = (r.Width - pad * 2) / 3;
        (string Figure, string Label)[] cols =
        {
            (d.Unread + (d.MoreUnread ? "+" : ""), "unread"),
            (d.OpenPullRequests.ToString(), "open PRs"),
            (Charts.Count(d.Stars) + (d.MoreRepos ? "+" : ""), "stars")
        };
        for (int i = 0; i < cols.Length; i++)
        {
            double cx = r.X + pad + w * (i + 0.5);
            Centered(dc, cols[i].Figure, new Point(cx, r.Y + r.Height * 0.44), r.Height * 0.32, p.Ink, p, w - 9);
            Centered(dc, cols[i].Label, new Point(cx, r.Y + r.Height * 0.78), Small(r), p.SubInk, p, w - 2, FontWeights.Normal);
            if (i > 0)
            {
                double x = r.X + pad + w * i;
                dc.DrawLine(new Pen(p.Track, 1), new Point(x, r.Y + r.Height * 0.25), new Point(x, r.Bottom - r.Height * 0.2));
            }
        }
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Unread notifications: {d.Unread}{(d.MoreUnread ? "+" : "")}\nYour open pull requests: {d.OpenPullRequests}\n"
                + $"Stars on {d.Repos} repositories you own: {d.Stars:#,0}{(d.MoreRepos ? " (first 500 repositories)" : "")}\n"
                + "Private repositories only count if the token also has the repo scope."
        }
        : null;
}
