using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

internal sealed record GmailInbox(int UnreadThreads, int UnreadMessages, int Threads);

/// <summary>Unread conversations in the Gmail inbox, under a small envelope.</summary>
internal sealed class GmailTile : GoogleTile<GmailInbox>
{
    protected override string Scope => GoogleAuth.GmailScope;
    protected override string ApiName => "Gmail API";
    protected override string Badge => "GMAIL";
    protected override Color BadgeColor => Color.FromRgb(0xEA, 0x43, 0x35);
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);
    protected override GmailInbox Sample => new(12, 15, 2_318);
    public override string Title => Shown is { } d ? $"Gmail: {d.UnreadThreads} unread in your inbox" : "Gmail";

    protected override async Task<GmailInbox> FetchAsync(CancellationToken ct)
    {
        string token = await GoogleAuth.AccessTokenAsync(ct).ConfigureAwait(false);
        // users.labels.get for INBOX (gmail.readonly is enough) returns messagesUnread / threadsUnread:
        // https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.labels/get
        using var doc = await Web.GetJson("https://gmail.googleapis.com/gmail/v1/users/me/labels/INBOX", req => Web.Bearer(req, token), ct).ConfigureAwait(false);
        var e = doc.RootElement;
        return new GmailInbox((int)e.Num("threadsUnread"), (int)e.Num("messagesUnread"), (int)e.Num("threadsTotal"));
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, GmailInbox d)
    {
        // An envelope outline with its flap, then the count under it.
        double w = r.Width * 0.4, h = w * 0.64, x = r.X + (r.Width - w) / 2, y = r.Y + r.Height * 0.28;
        var pen = new Pen(d.UnreadThreads > 0 ? BadgeBrush : p.SubInk, Math.Max(1.4, r.Height * 0.035)) { LineJoin = PenLineJoin.Round };
        dc.DrawRoundedRectangle(null, pen, new Rect(x, y, w, h), 2, 2);
        var flap = new StreamGeometry();
        using (var c = flap.Open())
        {
            c.BeginFigure(new Point(x + 1, y + 1), false, false);
            c.LineTo(new Point(x + w / 2, y + h * 0.58), true, true);
            c.LineTo(new Point(x + w - 1, y + 1), true, true);
        }
        dc.DrawGeometry(null, pen, flap);
        Centered(dc, d.UnreadThreads.ToString(CultureInfo.CurrentCulture), new Point(r.X + r.Width / 2, r.Y + r.Height * 0.76),
            r.Height * 0.27, d.UnreadThreads > 0 ? p.Ink : p.SubInk, p, r.Width - Pad(r) * 2);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p)
    {
        if (Shown is not { } d) return null;
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"Inbox: {d.UnreadThreads:#,0} unread conversations ({d.UnreadMessages:#,0} messages) of {d.Threads:#,0}\n" };
        var open = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Open Gmail")) { NavigateUri = new Uri("https://mail.google.com/") };
        open.RequestNavigate += (_, e) => DockWindow.ShellOpen(e.Uri.AbsoluteUri);
        text.Inlines.Add(open);
        return text;
    }
}

internal sealed record SearchClicks(double Clicks, double Previous, double[] Daily, double Impressions, DateTime Through);

/// <summary>Clicks from Google Search over the last 28 days of data, against the 28 days before.</summary>
internal sealed class SearchConsoleTile : GoogleTile<SearchClicks>
{
    protected override string Scope => GoogleAuth.SearchConsoleScope;
    protected override string ApiName => "Google Search Console API";
    protected override string Badge => "SEARCH";
    protected override Color BadgeColor => Color.FromRgb(0x42, 0x85, 0xF4);
    protected override AccountField[] Fields => new[] { new AccountField("site", "Property", "sc-domain:example.com or https://www.example.com/") };
    public override double WidthUnits => 2.4;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(60);
    public override string Title => Shown is { } d ? $"Search Console: {Charts.Count(d.Clicks)} clicks in 28 days ({Charts.Delta(d.Clicks, d.Previous)})" : "Search Console";

    protected override SearchClicks Sample => _sample ??= new(4_870, 4_310,
        Enumerable.Range(0, 28).Select(i => 158.0 + i * 1.8 + (i % 7 is 5 or 6 ? -48 : 0) + (i * 37 % 11 - 5) * 3.5).ToArray(), 162_400, DateTime.Today.AddDays(-2));
    private SearchClicks? _sample;

    protected override async Task<SearchClicks> FetchAsync(CancellationToken ct)
    {
        string token = await GoogleAuth.AccessTokenAsync(ct).ConfigureAwait(false), site = Field("site");
        var today = DateTime.Today;
        // searchAnalytics.query by date (webmasters.readonly is enough); finished days lag 2-3 days, so ask for 60 and
        // count back from the newest day returned: https://developers.google.com/webmaster-tools/v1/searchanalytics/query
        using var doc = await Web.PostJson($"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(site)}/searchAnalytics/query",
            new { startDate = SvcJson.Day(today.AddDays(-60)), endDate = SvcJson.Day(today), dimensions = new[] { "date" }, rowLimit = 100 },
            req => Web.Bearer(req, token), ct).ConfigureAwait(false);
        var days = new Dictionary<DateTime, (double Clicks, double Impressions)>();
        foreach (var row in doc.RootElement.Get("rows").Items())
            if (DateTime.TryParseExact(row.Get("keys").Items().FirstOrDefault().ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                days[day] = (row.Num("clicks"), row.Num("impressions"));
        var last = days.Count > 0 ? days.Keys.Max() : today.AddDays(-3);
        double Sum(int from, int to, Func<(double Clicks, double Impressions), double> pick) =>
            Enumerable.Range(from, to - from + 1).Sum(i => days.TryGetValue(last.AddDays(-i), out var v) ? pick(v) : 0);
        var daily = Enumerable.Range(0, 28).Select(i => days.TryGetValue(last.AddDays(i - 27), out var v) ? v.Clicks : 0).ToArray();
        return new SearchClicks(Sum(0, 27, v => v.Clicks), Sum(28, 55, v => v.Clicks), daily, Sum(0, 27, v => v.Impressions), last);
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, SearchClicks d)
    {
        double left = r.X + r.Width * 0.52;
        var (sub, ink) = Change(d.Clicks, d.Previous, "vs prior", p);
        Headline(dc, r, p, Charts.Count(d.Clicks), sub, ink, left - r.X - Pad(r) * 1.4);
        Charts.Line(dc, ChartArea(r, left), d.Daily, BadgeBrush, 1.5);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d
        ? new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Clicks in the 28 days to {d.Through:MMM d}: {d.Clicks:#,0} ({Charts.Delta(d.Clicks, d.Previous)} on the 28 days before: {d.Previous:#,0})\n"
                + $"Impressions: {d.Impressions:#,0}"
        }
        : null;
}
