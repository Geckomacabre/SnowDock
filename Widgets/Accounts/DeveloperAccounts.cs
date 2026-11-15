using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

[WidgetFamily(70)]
internal static class DeveloperAccounts
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Developer;
        add("resend", "Resend", c, "Emails your Resend account sent in the last 7 days and how many were delivered.", () => new ResendTile());
        add("linear", "Linear", c, "Open Linear issues assigned to you, and how many are in progress.", () => new LinearTile());
        add("supabase", "Supabase", c, "API requests to a Supabase project over 7 days, with its service health.", () => new SupabaseTile());
    }
}

internal sealed record ResendWeek(int Sent, int Delivered, int Bounced, bool Capped);

internal sealed class ResendTile : ServiceTile<ResendWeek>
{
    private const int MaxPages = 10;
    protected override string Service => "resend";
    protected override string ServiceName => "Resend";
    protected override string KeyLabel => "API key (full access)";
    protected override string Badge => "RESEND";
    protected override Color BadgeColor => Color.FromRgb(0x8E, 0x8E, 0x93);
    protected override string? DashboardUrl => "https://resend.com/api-keys";
    protected override string[] Steps => new[]
    {
        "In Resend, open API Keys and click Create API key.",
        "Choose Full access. Resend has no read-only keys, and sending-only keys can't list emails. SnowDock only reads the list.",
        "Paste the key below and click Save."
    };
    public override double WidthUnits => 2.2;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    protected override ResendWeek Sample => new(1_284, 1_242, 17, false);
    public override string Title => Shown is { } d ? $"Resend: {d.Sent:#,0}{(d.Capped ? "+" : "")} emails in 7 days, {Share(d):0}% delivered" : "Resend";

    private static double Share(ResendWeek d) => d.Sent == 0 ? 100 : 100.0 * d.Delivered / d.Sent;

    protected override async Task<ResendWeek> FetchAsync(CancellationToken ct)
    {
        string key = Key;
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        int sent = 0, delivered = 0, bounced = 0;
        string? after = null;
        for (int page = 0; page < MaxPages; page++)
        {
            // List sent emails, newest first, 100 a page, `after` = last id for the next page:
            // https://resend.com/docs/api-reference/emails/list-emails
            using var doc = await Web.GetJson("https://api.resend.com/emails?limit=100" + (after != null ? "&after=" + Uri.EscapeDataString(after) : ""),
                req => Web.Bearer(req, key), ct).ConfigureAwait(false);
            bool older = false;
            foreach (var e in doc.RootElement.Get("data").Items())
            {
                after = e.Str("id");
                if (SvcJson.Time(e.Str("created_at")) is { } t && t < since) { older = true; break; }
                // Event names: https://resend.com/docs/dashboard/emails/manage-emails#understand-email-events
                switch (e.Str("last_event"))
                {
                    case "scheduled" or "canceled" or "queued": continue; // not sent (yet)
                    case "delivered" or "opened" or "clicked" or "complained": delivered++; break;
                    case "bounced" or "failed" or "suppressed": bounced++; break;
                }
                sent++;
            }
            if (older || doc.RootElement.Get("has_more").ValueKind != JsonValueKind.True) return new(sent, delivered, bounced, false);
            await Task.Delay(600, ct).ConfigureAwait(false); // Resend allows a couple of requests a second
        }
        return new(sent, delivered, bounced, true);
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, ResendWeek d)
    {
        double share = Share(d), rad = r.Height * 0.25;
        var c = new Point(r.Right - Pad(r) - rad, r.Y + r.Height * 0.58);
        Brush ink = share >= 95 ? p.Good : share >= 85 ? p.Warn : p.Bad;
        Ring(dc, c, rad, share / 100, ink, p, Math.Max(2.5, r.Height * 0.065));
        Centered(dc, $"{share:0}%", c, r.Height * 0.15, p.Ink, p, rad * 1.6);
        Headline(dc, r, p, Charts.Count(d.Sent) + (d.Capped ? "+" : ""), "sent · 7 days", null, c.X - rad - r.X - Pad(r) * 1.6);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Last 7 days: {d.Sent:#,0}{(d.Capped ? "+ (counted the latest 1,000)" : "")} emails sent\n"
                + $"Delivered: {d.Delivered:#,0} ({Share(d):0.#}%)  ·  Bounced or failed: {d.Bounced:#,0}"
        }
        : null;
}

internal sealed record LinearMine(int Open, int InProgress, int Todo, int Backlog);

internal sealed class LinearTile : ServiceTile<LinearMine>
{
    protected override string Service => "linear";
    protected override string ServiceName => "Linear";
    protected override string KeyLabel => "Personal API key";
    protected override string Badge => "LINEAR";
    protected override Color BadgeColor => Color.FromRgb(0x5E, 0x6A, 0xD2);
    protected override string? DashboardUrl => "https://linear.app/settings/account/security";
    protected override string[] Steps => new[]
    {
        "In Linear, open Settings > Account > Security & access and create a new personal API key.",
        "Give it the Read permission only (you can also limit it to your teams).",
        "Paste it below and click Save."
    };
    public override double WidthUnits => 2.2;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);
    protected override LinearMine Sample => new(14, 3, 6, 5);
    public override string Title => Shown is { } d ? $"Linear: {d.Open} open issues assigned to you, {d.InProgress} in progress" : "Linear";

    // Issues assigned to the key's owner that aren't done or canceled, with their workflow state type.
    private const string Query = """
        query Mine($after: String) {
          viewer {
            assignedIssues(first: 100, after: $after, filter: { state: { type: { nin: ["completed", "canceled"] } } }) {
              nodes { state { type } }
              pageInfo { hasNextPage endCursor }
            }
          }
        }
        """;

    protected override async Task<LinearMine> FetchAsync(CancellationToken ct)
    {
        string key = Key;
        int open = 0, doing = 0, todo = 0, backlog = 0;
        string? after = null;
        for (int page = 0; page < 5; page++)
        {
            // GraphQL endpoint; personal keys go in Authorization with no "Bearer": https://linear.app/developers/graphql
            // Filtering by state type: https://linear.app/developers/filtering · paging: https://linear.app/developers/pagination
            using var doc = await SvcJson.GraphQL("https://api.linear.app/graphql", Query, new { after },
                req => req.Headers.TryAddWithoutValidation("Authorization", key), ct).ConfigureAwait(false);
            var issues = doc.RootElement.Get("data").Get("viewer").Get("assignedIssues");
            foreach (var n in issues.Get("nodes").Items())
            {
                open++;
                switch (n.Get("state").Str("type"))
                {
                    case "started": doing++; break;
                    case "unstarted": todo++; break;
                    default: backlog++; break; // backlog, triage
                }
            }
            var info = issues.Get("pageInfo");
            if (info.Get("hasNextPage").ValueKind != JsonValueKind.True) break;
            after = info.Str("endCursor");
        }
        return new LinearMine(open, doing, todo, backlog);
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, LinearMine d)
    {
        double pad = Pad(r), left = r.X + r.Width * 0.5;
        Headline(dc, r, p, d.Open.ToString(), $"{d.InProgress} in progress", d.InProgress > 0 ? p.Warn : p.SubInk, left - r.X - pad * 1.5);
        // One bar split into in progress / to do / backlog.
        var bar = new Rect(left, r.Y + r.Height * 0.48, r.Right - pad - left, Math.Max(5, r.Height * 0.12));
        dc.DrawRoundedRectangle(p.Track, null, bar, bar.Height / 2, bar.Height / 2);
        double total = Math.Max(1, d.Open), x = bar.X;
        dc.PushClip(new RectangleGeometry(bar, bar.Height / 2, bar.Height / 2));
        foreach (var (n, ink) in new[] { (d.InProgress, p.Warn), (d.Todo, p.Accent) })
        {
            double w = bar.Width * n / total;
            if (w > 0) dc.DrawRectangle(ink, null, new Rect(x, bar.Y, w, bar.Height));
            x += w;
        }
        dc.Pop();
        Fit(dc, $"{d.Todo} to do · {d.Backlog} later", left, bar.Bottom + r.Height * 0.04, Small(r) * 0.92, p.SubInk, p, bar.Width, FontWeights.Normal);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock { Text = $"Assigned to you and still open: {d.Open}\nIn progress: {d.InProgress}  ·  To do: {d.Todo}  ·  Backlog/triage: {d.Backlog}" }
        : null;
}
