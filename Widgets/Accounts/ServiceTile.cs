using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

/// <summary>
/// The layout the account tiles in this folder share: a small service name top-left, one big figure, a short line
/// under it, and room on the right for a chart. The top-right corner stays clear for the SAMPLE mark.
/// </summary>
internal abstract class ServiceTile<T> : AccountWidget<T> where T : class
{
    /// <summary>Short caps name for the corner, e.g. "RESEND".</summary>
    protected abstract string Badge { get; }
    /// <summary>The colour of the dot beside the name.</summary>
    protected abstract Color BadgeColor { get; }
    /// <summary>Square tiles have no room for the name next to the SAMPLE mark.</summary>
    protected virtual bool ShowBadge => WidthUnits >= 1.8;

    protected abstract void DrawData(DrawingContext dc, Rect r, WidgetPaint p, T d);

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (ShowBadge) DrawBadge(dc, r, p, BadgeBrush);
        if (!DrawState(dc, r, p)) return;
        DrawData(dc, r, p, Shown!);
        // Old numbers are kept when a refresh fails; a small red dot says they're stale.
        if (!IsSample && Error != null)
            dc.DrawEllipse(p.Bad, null, new Point(r.Right - r.Height * 0.12, r.Y + r.Height * 0.12), 2.5, 2.5);
    }

    private Brush? _badgeBrush;
    protected Brush BadgeBrush => _badgeBrush ??= Freeze(new SolidColorBrush(BadgeColor));

    protected static Brush Freeze(Brush b) { b.Freeze(); return b; }

    // ---- layout ----

    protected static double Pad(Rect r) => r.Height * 0.13;
    /// <summary>Smallest text on a tile; stays readable on a 52 DIP dock.</summary>
    protected static double Small(Rect r) => Math.Max(8, r.Height * 0.155);

    protected void DrawBadge(DrawingContext dc, Rect r, WidgetPaint p, Brush dot)
    {
        double s = Small(r), x = r.X + Pad(r), y = r.Y + r.Height * 0.06;
        var t = Text(Badge, s, p.SubInk, p, FontWeights.Bold);
        t.MaxTextWidth = Math.Max(10, r.Width * 0.62 - s);
        t.MaxLineCount = 1;
        t.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawEllipse(dot, null, new Point(x + s * 0.28, y + t.Height * 0.55), s * 0.27, s * 0.27);
        dc.DrawText(t, new Point(x + s * 0.75, y));
    }

    /// <summary>The big figure and the line under it, in a left column <paramref name="width"/> wide.</summary>
    protected static void Headline(DrawingContext dc, Rect r, WidgetPaint p, string figure, string sub, Brush? subInk, double width)
    {
        double x = r.X + Pad(r);
        Fit(dc, figure, x, r.Y + r.Height * 0.2, r.Height * 0.36, p.Ink, p, width, FontWeights.SemiBold);
        Fit(dc, sub, x, r.Y + r.Height * 0.68, Small(r), subInk ?? p.SubInk, p, width, FontWeights.Normal);
    }

    /// <summary>Left-aligned text from (x, top), shrunk to fit <paramref name="maxWidth"/>. Returns the drawn width.</summary>
    protected static double Fit(DrawingContext dc, string s, double x, double top, double size, Brush ink, WidgetPaint p,
        double maxWidth, FontWeight? weight = null)
    {
        var t = Text(s, size, ink, p, weight);
        if (t.Width > maxWidth && t.Width > 0) t = Text(s, size * maxWidth / t.Width, ink, p, weight);
        dc.DrawText(t, new Point(x, top + (size * 1.33 - t.Height) / 2));
        return t.Width;
    }

    /// <summary>Right-hand chart area, starting at <paramref name="left"/>.</summary>
    protected static Rect ChartArea(Rect r, double left) =>
        new(left, r.Y + r.Height * 0.4, Math.Max(4, r.Right - Pad(r) - left), r.Height * 0.46);

    /// <summary>Fourteen daily bars: the week before faint, the latest week in the accent colour.</summary>
    protected static void TwoWeekBars(DrawingContext dc, Rect area, IReadOnlyList<double> days, WidgetPaint p)
    {
        if (days.Count == 0) return;
        double max = Math.Max(1, days.Max()), w = area.Width / days.Count;
        for (int i = 0; i < days.Count; i++)
        {
            double h = Math.Max(1.5, area.Height * days[i] / max);
            var brush = i >= days.Count - 7 ? p.Accent : p.Track;
            dc.DrawRoundedRectangle(brush, null, new Rect(area.X + i * w + w * 0.14, area.Bottom - h, w * 0.72, h), 1.2, 1.2);
        }
    }

    /// <summary>"+12% vs last wk" in green or red, or a plain note when there's nothing to compare.</summary>
    protected static (string Text, Brush Ink) Change(double now, double before, string versus, WidgetPaint p) =>
        before <= 0 && now <= 0 ? ("no data yet", p.SubInk) : ($"{Charts.Delta(now, before)} {versus}", Charts.DeltaBrush(now, before, p));
}

/// <summary>JSON and request helpers for the account widgets.</summary>
internal static class SvcJson
{
    public static double Num(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
    }

    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>A child object/array, or an empty (Undefined) element.</summary>
    public static JsonElement Get(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IEnumerable<JsonElement> Items(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();

    /// <summary>
    /// POST a GraphQL query. GraphQL services answer 200 even when the query failed, so an "errors" list becomes a
    /// <see cref="WebError"/> (with <paramref name="notFound"/> when the error says something doesn't exist).
    /// </summary>
    public static async Task<JsonDocument> GraphQL(string url, string query, object? variables, Action<HttpRequestMessage> auth,
        CancellationToken ct, string notFound = "Not found: check the settings in the card")
    {
        var doc = await Web.PostJson(url, new { query, variables }, auth, ct).ConfigureAwait(false);
        var errors = doc.RootElement.Get("errors");
        if (errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            bool missing = errors.Items().Any(x => x.Str("type") == "NOT_FOUND"
                || x.Str("message").Contains("not exist", StringComparison.OrdinalIgnoreCase)
                || x.Str("message").Contains("could not resolve", StringComparison.OrdinalIgnoreCase));
            bool auth401 = errors.Items().Any(x => x.Get("extensions").Str("type").Contains("authentication", StringComparison.OrdinalIgnoreCase));
            doc.Dispose();
            throw new WebError(missing ? notFound : auth401 ? "Key refused: check it in the card" : "The service refused the request");
        }
        return doc;
    }

    /// <summary>The PC's time zone as an IANA name (for services that bucket by day), or UTC.</summary>
    public static string IanaZone() =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";

    public static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Parses ISO-ish timestamps, including Postgres style "2026-04-03 22:13:42.67+00".</summary>
    public static DateTimeOffset? Time(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Replace(' ', 'T');
        if (s.Contains('T') && s.Length > 3 && (s[^3] == '+' || s[^3] == '-') && char.IsDigit(s[^1])) s += ":00"; // "+00" -> "+00:00"
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    }
}
