using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

/// <summary>
/// A follower/subscriber count with how it moved over the last week. Services only report today's number, so the
/// tile keeps one count per day in its own settings (a plain number, not a secret) and compares against a week ago.
/// </summary>
internal abstract class FollowerTile<T> : ServiceTile<T> where T : class
{
    protected abstract long Count(T d);
    /// <summary>"@name" or the channel name, for the card and the hover label.</summary>
    protected abstract string Who(T d);
    /// <summary>What the count is called: "followers", "subscribers".</summary>
    protected abstract string Noun { get; }
    /// <summary>The week's change shown on the sample.</summary>
    protected abstract long SampleGain { get; }
    /// <summary>When the count is hidden (YouTube lets channels hide it).</summary>
    protected virtual bool Hidden(T d) => false;

    public override double WidthUnits => 2;
    public override string Title => Shown is { } d ? $"{ServiceName}: {Who(d)} · {Figure(d)} {Noun}" : ServiceName;

    private T? _recorded;

    protected override bool TickLive(DateTime now)
    {
        if (IsSample || Data is not T d || ReferenceEquals(d, _recorded)) return false;
        _recorded = d;
        if (!Hidden(d)) Record(now.Date, Count(d));
        return false;
    }

    private string Figure(T d) => Hidden(d) ? "hidden" : Charts.Count(Count(d));

    private List<(DateTime Day, long Count)> History()
    {
        var list = new List<(DateTime, long)>();
        foreach (var part in Get("history", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && DateTime.TryParseExact(kv[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && long.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                list.Add((day, n));
        }
        return list.OrderBy(h => h.Item1).ToList();
    }

    private void Record(DateTime day, long count)
    {
        var list = History();
        if (list.Count > 0 && list[^1].Day == day && list[^1].Count == count) return;
        list.RemoveAll(h => h.Day == day || h.Day < day.AddDays(-15));
        list.Add((day, count));
        Set("history", string.Join(";", list.Select(h => $"{SvcJson.Day(h.Day)}={h.Count.ToString(CultureInfo.InvariantCulture)}")));
    }

    /// <summary>Change against the count about a week ago (or the oldest one kept), or null if there's none yet.</summary>
    protected (long Gain, string Span)? Gain(T d)
    {
        if (IsSample) return (SampleGain, "this week");
        if (Hidden(d)) return null;
        var today = DateTime.Today;
        var list = History().Where(h => h.Day < today).ToList();
        if (list.Count == 0) return null;
        var weekAgo = list.Where(h => h.Day <= today.AddDays(-7)).ToList();
        var (day, count) = weekAgo.Count > 0 ? weekAgo[^1] : list[0];
        string span = (today - day).TotalDays is >= 7 and <= 9 ? "this week" : $"since {day:MMM d}";
        return (Count(d) - count, span);
    }

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, T d)
    {
        var gain = Gain(d);
        string sub = gain is { } g ? $"{(g.Gain >= 0 ? "+" : "−")}{Charts.Count(Math.Abs(g.Gain))} {g.Span}" : Who(d);
        Brush ink = gain is { } g2 ? (g2.Gain > 0 ? p.Good : g2.Gain < 0 ? p.Bad : p.SubInk) : p.SubInk;
        Headline(dc, r, p, Figure(d), sub, ink, r.Width - Pad(r) * 2);
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p)
    {
        if (Shown is not T d) return null;
        var gain = Gain(d);
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{Who(d)}: {(Hidden(d) ? "count hidden" : Count(d).ToString("#,0", CultureInfo.CurrentCulture))} {Noun}"
                + (gain is { } g ? $"\n{(g.Gain >= 0 ? "+" : "−")}{Math.Abs(g.Gain):#,0} {g.Span}" : "\nThe change shows once SnowDock has seen the count on two days.")
                + Extra(d)
        };
    }

    /// <summary>More lines for the card.</summary>
    protected virtual string Extra(T d) => "";
}
