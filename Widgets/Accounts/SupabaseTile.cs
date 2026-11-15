using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

internal sealed record SupabaseStats(double Requests, double[] Series, string[] Down, string[] Starting, int Services);

/// <summary>A Supabase project's API traffic over the last 7 days and whether its services are healthy.</summary>
internal sealed class SupabaseTile : ServiceTile<SupabaseStats>
{
    protected override string Service => "supabase";
    protected override string ServiceName => "Supabase";
    protected override string KeyLabel => "Access token";
    protected override string Badge => "SUPABASE";
    protected override Color BadgeColor => Color.FromRgb(0x3E, 0xCF, 0x8E);
    protected override string? DashboardUrl => "https://supabase.com/dashboard/account/tokens";
    protected override AccountField[] Fields => new[] { new AccountField("project", "Project ref", "the 20 letters in your project's dashboard URL") };
    protected override string[] Steps => new[]
    {
        "In Supabase, open Account > Access Tokens and generate a new token.",
        "Make it a scoped token for this project with only two read permissions: analytics_usage_read and project_admin_read. (A classic token also works, but it can do anything your account can.)",
        "Copy the project ref from the project's dashboard address (supabase.com/dashboard/project/<ref>).",
        "Paste both below and click Save."
    };
    public override double WidthUnits => 2.4;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);
    protected override SupabaseStats Sample => new(48_230, new double[] { 5_210, 6_480, 7_920, 6_150, 7_740, 8_330, 6_400 }, Array.Empty<string>(), Array.Empty<string>(), 5);
    public override string Title => Shown is { } d ? $"Supabase: {Charts.Count(d.Requests)} API requests in 7 days · {Health(d).Word}" : "Supabase";

    private static readonly Regex RefPattern = new("^[a-z]{20}$");
    private static readonly string[] Checked = { "auth", "db", "rest", "realtime", "storage" };

    protected override async Task<SupabaseStats> FetchAsync(CancellationToken ct)
    {
        string token = Key, project = Field("project").ToLowerInvariant();
        if (!RefPattern.IsMatch(project)) throw new WebError("Project ref should be 20 letters: check the card");
        string api = $"https://api.supabase.com/v1/projects/{project}";

        // Service health (needs project_admin_read): https://supabase.com/docs/reference/api/v1-get-services-health
        using var health = await Web.GetJson($"{api}/health?services={string.Join(",", Checked)}", req => Web.Bearer(req, token), ct).ConfigureAwait(false);
        var down = new List<string>();
        var starting = new List<string>();
        int services = 0;
        foreach (var s in health.RootElement.Items())
        {
            services++;
            switch (s.Str("status"))
            {
                case "UNHEALTHY": down.Add(s.Str("name")); break;
                case "COMING_UP": starting.Add(s.Str("name")); break;
            }
        }

        // API request counts, interval=7day (needs analytics_usage_read):
        // https://supabase.com/docs/reference/api/v1-get-project-usage-api-count
        using var usage = await Web.GetJson($"{api}/analytics/endpoints/usage.api-counts?interval=7day", req => Web.Bearer(req, token), ct).ConfigureAwait(false);
        var series = usage.RootElement.Get("result").Items()
            .Select(x => (Time: SvcJson.Time(x.Str("timestamp")), Total: x.Num("total_rest_requests") + x.Num("total_auth_requests")
                + x.Num("total_realtime_requests") + x.Num("total_storage_requests")))
            .OrderBy(x => x.Time).Select(x => x.Total).ToArray();
        return new SupabaseStats(series.Sum(), series, down.ToArray(), starting.ToArray(), services);
    }

    private static (string Word, bool Ok, bool Bad) Health(SupabaseStats d) =>
        d.Down.Length > 0 ? ($"{d.Down.Length} down", false, true)
        : d.Starting.Length > 0 ? ("starting", false, false)
        : d.Services == 0 ? ("unknown", false, false)
        : ("healthy", true, false);

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, SupabaseStats d)
    {
        var h = Health(d);
        double left = r.X + r.Width * 0.55;
        Headline(dc, r, p, Charts.Count(d.Requests), "requests · 7d", null, left - r.X - Pad(r) * 1.5);
        var area = ChartArea(r, left);
        Charts.Line(dc, new Rect(area.X, r.Y + r.Height * 0.34, area.Width, r.Height * 0.3), d.Series, BadgeBrush, 1.6);
        // Health under the chart: a dot and a word.
        var ink = h.Bad ? p.Bad : h.Ok ? p.Good : p.Warn;
        double s = Small(r), y = r.Y + r.Height * 0.68;
        dc.DrawEllipse(ink, null, new Point(area.X + s * 0.3, y + s * 0.68), s * 0.27, s * 0.27);
        Fit(dc, h.Word, area.X + s * 0.75, y, s, ink, p, area.Width - s, FontWeights.SemiBold);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p)
    {
        if (Shown is not { } d) return null;
        string health = d.Down.Length > 0 ? "Unhealthy: " + string.Join(", ", d.Down)
            : d.Starting.Length > 0 ? "Starting up: " + string.Join(", ", d.Starting)
            : $"All {d.Services} checked services healthy ({string.Join(", ", Checked)})";
        return new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"API requests, last 7 days: {d.Requests:#,0}\n{health}" };
    }
}
