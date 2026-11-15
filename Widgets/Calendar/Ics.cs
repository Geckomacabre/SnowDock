using System.Globalization;
using System.Text;

namespace SnowDock.Widgets.CalendarWidgets;

/// <summary>One occurrence of an event, in this PC's local time. All-day events run from midnight to midnight.</summary>
internal sealed record CalEvent(string Title, DateTime Start, DateTime End, bool AllDay);

/// <summary>
/// A small reader for calendar subscription (iCalendar / .ics) files: VEVENT with DTSTART, DTEND or DURATION,
/// SUMMARY and STATUS; dates in UTC ("Z"), with a TZID, floating, or all-day; RRULE DAILY and WEEKLY (INTERVAL,
/// BYDAY, COUNT, UNTIL); and EXDATE / RECURRENCE-ID so cancelled or moved occurrences don't show twice.
/// Other recurrence rules (monthly, yearly) only contribute their first occurrence.
/// </summary>
internal static class Ics
{
    private sealed record Stamp(DateTime Wall, TimeZoneInfo? Zone, bool Utc, bool DateOnly)
    {
        /// <summary>This wall-clock time in local time (floating times are already local).</summary>
        public DateTime Local => Utc ? DateTime.SpecifyKind(Wall, DateTimeKind.Utc).ToLocalTime()
            : Zone != null ? Convert(Wall, Zone) : Wall;
        public Stamp At(DateTime wall) => this with { Wall = wall };
    }

    private static DateTime Convert(DateTime wall, TimeZoneInfo zone)
    {
        try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local); }
        catch (ArgumentException) { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall.AddHours(1), DateTimeKind.Unspecified), zone, TimeZoneInfo.Local); } // a skipped (spring-forward) hour
    }

    /// <summary>Occurrences that end after <paramref name="from"/> and start before <paramref name="to"/>.</summary>
    public static List<CalEvent> Read(string text, DateTime from, DateTime to)
    {
        var events = new List<Dictionary<string, List<(Dictionary<string, string> Params, string Value)>>>();
        Dictionary<string, List<(Dictionary<string, string>, string)>>? cur = null;
        int depth = 0; // nested components inside a VEVENT (VALARM) are skipped
        foreach (var line in Unfold(text))
        {
            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
            {
                if (cur != null) depth++;
                else if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) cur = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                if (cur == null) continue;
                if (depth > 0) { depth--; continue; }
                events.Add(cur);
                cur = null;
                continue;
            }
            if (cur == null || depth > 0) continue;
            var (name, ps, value) = Split(line);
            if (!cur.TryGetValue(name, out var list)) cur[name] = list = new();
            list.Add((ps, value));
        }

        // Moved or cancelled occurrences of a series, by UID, as local start times.
        var overridden = new Dictionary<string, HashSet<DateTime>>();
        foreach (var e in events)
            if (First(e, "RECURRENCE-ID") is { } rid && Stamp_(rid) is { } s)
                Add(overridden, Value(e, "UID"), s.Local);

        var result = new List<CalEvent>();
        foreach (var e in events)
        {
            if (First(e, "DTSTART") is not { } ds || Stamp_(ds) is not { } start) continue;
            if (Value(e, "STATUS").Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            string title = Unescape(Value(e, "SUMMARY"));
            if (title.Length == 0) title = "Busy";
            TimeSpan length = start.DateOnly ? TimeSpan.FromDays(1) : TimeSpan.Zero;
            if (First(e, "DTEND") is { } de && Stamp_(de) is { } end) length = end.DateOnly && start.DateOnly ? end.Wall - start.Wall : end.Local - start.Local;
            else if (Value(e, "DURATION") is { Length: > 0 } dur && Duration(dur) is TimeSpan d) length = d;
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;

            var skip = new HashSet<DateTime>();
            if (Value(e, "RECURRENCE-ID").Length == 0 && overridden.TryGetValue(Value(e, "UID"), out var o)) skip.UnionWith(o);
            if (e.TryGetValue("EXDATE", out var exdates))
                foreach (var (ps, v) in exdates)
                    foreach (var part in v.Split(','))
                        if (Stamp_((ps, part)) is { } x) skip.Add(x.Local);

            foreach (var wall in Occurrences(start, Value(e, "RRULE"), from - length, to))
            {
                var at = start.At(wall);
                if (skip.Contains(at.Local)) continue;
                DateTime s = at.Local, en = start.DateOnly ? at.Local + length : s + length;
                if (en > from && s < to) result.Add(new CalEvent(title, s, en, start.DateOnly));
            }
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    /// <summary>Wall-clock starts (in the event's own zone) from DTSTART and RRULE, within roughly [from, to].</summary>
    private static IEnumerable<DateTime> Occurrences(Stamp start, string rrule, DateTime from, DateTime to)
    {
        var rule = rrule.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].ToUpperInvariant());
        string freq = rule.GetValueOrDefault("FREQ", "");
        if (freq is not ("DAILY" or "WEEKLY")) { yield return start.Wall; yield break; }
        int interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        int? count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var c) ? c : null;
        DateTime until = rule.TryGetValue("UNTIL", out var u) && Stamp_((new(), u)) is { } us
            ? (us.DateOnly ? us.Wall.AddDays(1).AddTicks(-1) : us.Local) : DateTime.MaxValue;
        // Search window in the event's wall-clock terms, with a day of slack for time-zone differences.
        DateTime lo = from.AddDays(-1), hi = to.AddDays(1);

        var days = freq == "DAILY" ? new List<int> { 0 }
            : (rule.GetValueOrDefault("BYDAY", "") is { Length: > 0 } by
                ? by.Split(',').Where(d => d.Trim().Length >= 2).Select(d => Array.IndexOf(new[] { "MO", "TU", "WE", "TH", "FR", "SA", "SU" }, d.Trim()[^2..])).Where(i => i >= 0).ToList()
                : new List<int> { ((int)start.Wall.DayOfWeek + 6) % 7 });
        int stepDays = freq == "DAILY" ? interval : 7 * interval;
        // The first period: the start day (daily) or the Monday of the start's week (weekly; RFC 5545's default week start).
        DateTime period0 = freq == "DAILY" ? start.Wall : start.Wall.AddDays(-(((int)start.Wall.DayOfWeek + 6) % 7));
        long k = 0;
        // Without COUNT we can jump straight to the window; with COUNT every occurrence must be counted.
        if (count == null && lo > period0) k = Math.Max(0, (long)((lo - period0).TotalDays / stepDays) - 1);
        int seen = 0;
        for (int guard = 0; guard < 20000; guard++, k++)
        {
            var period = period0.AddDays(k * stepDays);
            if (period > hi || period > until) yield break;
            foreach (int dow in days.OrderBy(d => d))
            {
                var wall = freq == "DAILY" ? period : period.AddDays(dow);
                if (wall < start.Wall) continue;
                if (count != null && seen >= count) yield break;
                if (start.At(wall).Local > until) yield break;
                seen++;
                if (wall >= lo && wall <= hi) yield return wall;
            }
        }
    }

    private static void Add(Dictionary<string, HashSet<DateTime>> map, string key, DateTime value)
    {
        if (!map.TryGetValue(key, out var set)) map[key] = set = new();
        set.Add(value);
    }

    private static (Dictionary<string, string>, string)? First(Dictionary<string, List<(Dictionary<string, string>, string)>> e, string name) =>
        e.TryGetValue(name, out var l) && l.Count > 0 ? l[0] : null;

    private static string Value(Dictionary<string, List<(Dictionary<string, string>, string)>> e, string name) =>
        First(e, name)?.Item2.Trim() ?? "";

    /// <summary>A DATE or DATE-TIME value with its TZID, if any.</summary>
    private static Stamp? Stamp_((Dictionary<string, string> Params, string Value)? prop)
    {
        if (prop is not { } p) return null;
        string v = p.Value.Trim();
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new Stamp(d, null, false, true);
        bool utc = v.EndsWith('Z');
        if (!DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        var zone = !utc && p.Params.TryGetValue("TZID", out var tzid) ? Zone(tzid) : null;
        return new Stamp(t, zone, utc, false);
    }

    /// <summary>A TZID as Windows understands it: a Windows id, an IANA id, or IANA after a vendor prefix.</summary>
    private static TimeZoneInfo? Zone(string tzid)
    {
        string id = tzid.Trim('"').Trim();
        var candidates = new List<string> { id };
        if (id.StartsWith('/')) candidates.Add(string.Join('/', id.Split('/').TakeLast(2))); // "/mozilla.org/…/Europe/London"
        foreach (var c in candidates)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(c); } catch { }
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(c, out var win))
                try { return TimeZoneInfo.FindSystemTimeZoneById(win); } catch { }
        }
        return null; // unknown zone: treat the time as local
    }

    private static TimeSpan? Duration(string s)
    {
        // RFC 5545 duration, e.g. PT1H30M, P1D, P2W.
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
        if (!m.Success) return null;
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        var d = new TimeSpan(G(2) * 7 + G(3), G(4), G(5), G(6));
        return m.Groups[1].Value == "-" ? -d : d;
    }

    /// <summary>Lines with folded continuations (CRLF + space or tab) joined back up.</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { sb.Append(raw, 1, raw.Length - 1); continue; }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear().Append(raw);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>"DTSTART;TZID=Europe/London:20250101T090000" into name, parameters and value (quotes respected).</summary>
    private static (string, Dictionary<string, string>, string) Split(string line)
    {
        bool quoted = false;
        int colon = -1;
        for (int i = 0; i < line.Length && colon < 0; i++)
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) colon = i;
        string head = colon < 0 ? line : line[..colon], value = colon < 0 ? "" : line[(colon + 1)..];
        var parts = head.Split(';');
        var ps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts.Skip(1))
            if (p.Split('=', 2) is { Length: 2 } kv) ps[kv[0].Trim()] = kv[1].Trim().Trim('"');
        return (parts[0].Trim().ToUpperInvariant(), ps, value);
    }

    private static string Unescape(string s) =>
        s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
}
