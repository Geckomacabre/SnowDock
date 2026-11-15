using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.CalendarWidgets;

[WidgetFamily(51)]
internal static class CalendarFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add) =>
        add("calendar", "Next Event", WidgetCatalog.Productivity,
            "Your next calendar event and how soon it starts, from the private iCal links of Google, Outlook or iCloud calendars.", () => new CalendarWidget());
}

/// <summary>
/// The next event across one or more calendar subscription (.ics) links. The links are secret (anyone with one
/// can read the calendar), so they're kept in the encrypted <see cref="KeyStore"/>, never in config.json or a log.
/// </summary>
internal sealed class CalendarWidget : OnlineWidget<List<CalEvent>>
{
    private const string Service = "calendar-ics";
    private string _minute = "";

    private static List<string> Links => (KeyStore.Get(Service) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    private static void SaveLinks(List<string> links) => KeyStore.Set(Service, links.Count == 0 ? null : string.Join("\n", links));

    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);
    protected override bool CanFetch => KeyStore.Has(Service);
    public override bool HasCard => true;
    public override double WidthUnits => 2.9;
    public override string Title => !IsSample && Next(Data) is { } e ? $"{e.Title} · {When(e, DateTime.Now)}" : "Next Event";

    protected override List<CalEvent>? Sample
    {
        get
        {
            var hour = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            return new List<CalEvent>
            {
                new("Design review", hour.AddMinutes(90), hour.AddMinutes(150), false),
                new("Lunch with Sam", hour.Date.AddDays(1).AddHours(12.5), hour.Date.AddDays(1).AddHours(13.5), false),
                new("Team offsite", hour.Date.AddDays(3), hour.Date.AddDays(4), true)
            };
        }
    }

    protected override async Task<List<CalEvent>> FetchAsync(CancellationToken ct)
    {
        DateTime from = DateTime.Now.AddDays(-1), to = DateTime.Now.AddDays(60);
        var all = new List<CalEvent>();
        Exception? first = null;
        int ok = 0;
        foreach (var link in Links)
        {
            try
            {
                all.AddRange(Ics.Read(await Download(link, ct).ConfigureAwait(false), from, to));
                ok++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { first ??= ex; }
        }
        if (ok == 0 && first != null) throw first;
        all.Sort((a, b) => a.Start.CompareTo(b.Start));
        return all;
    }

    /// <summary>GET an .ics link (webcal:// is plain https). Errors never include the link itself.</summary>
    private static async Task<string> Download(string link, CancellationToken ct)
    {
        if (link.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) link = "https://" + link["webcal://".Length..];
        HttpResponseMessage resp;
        try { resp = await Web.Http.GetAsync(link, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new WebError("Timed out"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException) { throw new WebError("Calendar link unreachable"); }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw new WebError(resp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    ? "Calendar link refused: check it in the card" : Web.Describe(resp.StatusCode), resp.StatusCode);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var text = Encoding.UTF8.GetString(bytes);
            if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) throw new WebError("That link isn't a calendar (.ics)");
            return text;
        }
    }

    /// <summary>
    /// The event to show: the next timed one still running or to come, unless an all-day event falls on an
    /// earlier day than it.
    /// </summary>
    private static CalEvent? Next(List<CalEvent>? events)
    {
        if (events == null) return null;
        var now = DateTime.Now;
        var timed = events.Where(e => !e.AllDay && e.End > now).MinBy(e => e.Start);
        var allDay = events.Where(e => e.AllDay && e.End > now).MinBy(e => e.Start);
        if (timed == null) return allDay;
        if (allDay == null) return timed;
        return allDay.Start.Date < timed.Start.Date ? allDay : timed;
    }

    private static string When(CalEvent e, DateTime now)
    {
        string At(DateTime t) => WidgetEnv.Time(t) + (WidgetEnv.Clock24h ? "" : WidgetEnv.AmPm(t).ToLowerInvariant());
        string Day(DateTime d) => d.Date == now.Date ? "today" : d.Date == now.Date.AddDays(1) ? "tomorrow"
            : d.Date < now.Date.AddDays(7) ? d.ToString("dddd") : d.ToString("MMM d");
        if (e.AllDay) return e.Start <= now ? "all day today" : $"{Day(e.Start)}, all day";
        if (e.Start <= now) return $"now, until {At(e.End)}";
        var left = e.Start - now;
        if (left.TotalMinutes < 1) return "starting now";
        if (left.TotalMinutes < 60) return $"in {Math.Ceiling(left.TotalMinutes):0} min";
        if (left.TotalHours < 6 && e.Start.Date == now.Date) return $"in {(int)left.TotalHours} h {left.Minutes} min";
        return $"{Day(e.Start)} {At(e.Start)}";
    }

    protected override bool TickLive(DateTime now)
    {
        var m = now.ToString("yyyyMMddHHmm");
        if (m == _minute) return false;
        _minute = m;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (IsSample ? Shown != null : DrawState(dc, r, p))
        {
            double h = r.Height, x = r.X + h * 0.36, w = r.Right - x - h * 0.16;
            var now = DateTime.Now;
            var e = Next(Shown);
            var soon = e != null && !e.AllDay && e.Start - now < TimeSpan.FromMinutes(15);
            dc.DrawRoundedRectangle(e == null ? p.Track : soon ? p.Bad : p.Accent, null, new Rect(r.X + h * 0.17, r.Y + h * 0.2, h * 0.08, h * 0.6), h * 0.04, h * 0.04);
            if (e == null)
                Line(dc, "Nothing coming up", x, r.Y + h * 0.5, h * 0.2, p.SubInk, p, w, FontWeights.Normal);
            else
            {
                Line(dc, When(e, now), x, r.Y + h * 0.32, h * 0.18, soon ? p.Bad : p.Accent, p, w, FontWeights.Bold);
                Line(dc, e.Title, x, r.Y + h * 0.63, h * 0.22, p.Ink, p, w, FontWeights.SemiBold);
            }
        }
        if (IsSample) Markets.MarketDraw.SampleBadge(dc, r, p);
    }

    private static void Line(DrawingContext dc, string s, double x, double cy, double size, Brush ink, WidgetPaint p, double maxWidth, FontWeight weight)
    {
        var t = Text(s, size, ink, p, weight);
        t.MaxTextWidth = Math.Max(1, maxWidth);
        t.MaxLineCount = 1;
        t.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(t, new Point(x, cy - t.Height / 2));
    }

    public override FrameworkElement BuildCard(WidgetPaint p) => CalendarCard.Build(p, this);

    // For the card.
    internal List<CalEvent>? Upcoming => Shown?.Where(e => e.End > DateTime.Now).Take(6).ToList();
    internal string WhenText(CalEvent e) => When(e, DateTime.Now);
    internal bool Sampled => IsSample;
    internal string? LastError => Error;
    internal static List<string> SavedLinks => Links;
    internal static void StoreLinks(List<string> links) => SaveLinks(links);
    internal void Changed()
    {
        RefreshSoon();
        Host?.Redraw();
    }
}
