using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.SystemPlus;

[WidgetFamily(21)]
internal static class ScreenTimeFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add) =>
        add("screen-time", "Screen Time", WidgetCatalog.Productivity,
            "How long you've been at the PC today, hour by hour; the card totals each app for today and this week.",
            () => new ScreenTimeWidget());
}

[WidgetFamily(22)]
internal static class DayTimelineFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add) =>
        add("day-timeline", "Day Timeline", WidgetCatalog.Productivity,
            "Your day on one line, each hour coloured by the kind of app you used: browsing, chat, coding, media or games.",
            () => new DayTimelineWidget());
}

/// <summary>Shared tracking and summing for the two screen-time tiles.</summary>
internal abstract class ScreenTimeTile : DockWidget
{
    private string _sig = "";
    public override bool HasCard => true;

    public override bool Tick(DateTime now)
    {
        AppTracker.Sample(this, now);
        // The tiles change a minute at a time.
        var sig = $"{AppTracker.Today.Date}|{TotalSeconds(AppTracker.Today) / 60}|{now:HH:mm}";
        if (sig == _sig) return false;
        _sig = sig;
        return true;
    }

    public override void OnRemoved() => AppTracker.Release(this);

    public static int TotalSeconds(DayRecord d) => d.Apps.Values.Sum(a => a.Total);

    public static int[] HourTotals(DayRecord d)
    {
        var h = new int[24];
        foreach (var a in d.Apps.Values) for (int i = 0; i < 24; i++) h[i] += a.Hours[i];
        return h;
    }

    public static string Duration(int seconds)
    {
        int m = seconds / 60;
        return m < 60 ? $"{m}m" : $"{m / 60}h {m % 60:00}m";
    }
}

/// <summary>Today's time at the PC: the total, and a bar for every hour.</summary>
internal sealed class ScreenTimeWidget : ScreenTimeTile
{
    public override double WidthUnits => 2.4;
    public override string Title => $"Screen time today: {Duration(TotalSeconds(AppTracker.Today))}";

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height, pad = h * 0.16;
        var today = AppTracker.Today;
        var total = Text(Duration(TotalSeconds(today)), h * 0.24, p.Ink, p, FontWeights.Bold);
        dc.DrawText(total, new Point(r.X + pad, r.Y + h * 0.07));
        var label = Text("TODAY", h * 0.11, p.SubInk, p);
        dc.DrawText(label, new Point(r.Right - pad - label.Width, r.Y + h * 0.07 + (total.Height - label.Height) * 0.7));

        var hours = HourTotals(today);
        var area = new Rect(r.X + pad, r.Y + h * 0.5, r.Width - pad * 2, h * 0.34);
        double step = area.Width / 24, w = Math.Max(1.5, step * 0.62);
        int now = DateTime.Now.Hour;
        for (int i = 0; i < 24; i++)
        {
            double x = area.X + step * i + (step - w) / 2;
            double v = Math.Min(1, hours[i] / 3600.0), bh = hours[i] > 0 ? Math.Max(w, area.Height * v) : w;
            dc.DrawRoundedRectangle(hours[i] > 0 ? (i == now ? p.Accent : p.Ink) : p.Track, null,
                new Rect(x, area.Bottom - bh, w, bh), w / 2, w / 2);
        }
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var week = new DayRecord { Date = "week" };
        foreach (var day in AppTracker.ThisWeek())
            foreach (var (exe, app) in day.Apps)
            {
                if (!week.Apps.TryGetValue(exe, out var sum)) week.Apps[exe] = sum = new AppDay { Label = app.Label, Kind = app.Kind };
                for (int i = 0; i < 24; i++) sum.Hours[i] += app.Hours[i];
            }
        return ScreenTimeCards.Build(p, ("Today", AppTracker.Today), ("This week", week));
    }
}

/// <summary>The day as one line from midnight to midnight, each hour split by the kinds of app used in it.</summary>
internal sealed class DayTimelineWidget : ScreenTimeTile
{
    public override double WidthUnits => 3.2;

    private static AppKind? Mostly(DayRecord d) => d.Apps.Values.GroupBy(a => a.Kind)
        .Select(g => (g.Key, Secs: g.Sum(a => a.Total))).Where(x => x.Secs >= 60)
        .OrderByDescending(x => x.Secs).Select(x => (AppKind?)x.Key).FirstOrDefault();

    public override string Title => Mostly(AppTracker.Today) is AppKind k
        ? $"Today: {Duration(TotalSeconds(AppTracker.Today))}, mostly {AppKinds.Label(k).ToLowerInvariant()}"
        : "Today's timeline";

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height, pad = h * 0.16;
        var today = AppTracker.Today;
        dc.DrawText(Text(Duration(TotalSeconds(today)), h * 0.19, p.Ink, p, FontWeights.Bold), new Point(r.X + pad, r.Y + h * 0.08));
        if (Mostly(today) is AppKind k)
        {
            var mostly = Text(AppKinds.Label(k), h * 0.14, AppKinds.Brush(k), p);
            dc.DrawText(mostly, new Point(r.Right - pad - mostly.Width, r.Y + h * 0.12));
        }

        var line = new Rect(r.X + pad, r.Y + h * 0.46, r.Width - pad * 2, Math.Max(5, h * 0.17));
        double hw = line.Width / 24;
        dc.DrawRoundedRectangle(p.Track, null, line, line.Height / 2, line.Height / 2);
        dc.PushClip(new RectangleGeometry(line, line.Height / 2, line.Height / 2));
        for (int hour = 0; hour < 24; hour++)
        {
            double x = line.X + hw * hour;
            foreach (var g in today.Apps.Values.GroupBy(a => a.Kind).OrderBy(g => g.Key))
            {
                double secs = g.Sum(a => a.Hours[hour]);
                if (secs <= 0) continue;
                double w = hw * Math.Min(1, secs / 3600.0);
                dc.DrawRectangle(AppKinds.Brush(g.Key), null, new Rect(x, line.Y, w + 0.4, line.Height));
                x += w;
            }
        }
        dc.Pop();
        // Now.
        var nowT = DateTime.Now.TimeOfDay;
        double nx = line.X + line.Width * nowT.TotalHours / 24;
        dc.DrawLine(new Pen(p.Ink, Math.Max(1.2, h * 0.025)), new Point(nx, line.Y - h * 0.06), new Point(nx, line.Bottom + h * 0.06));

        double size = h * 0.125;
        for (int i = 0; i <= 24; i += 6)
        {
            string t = WidgetEnv.Clock24h ? $"{i % 24:00}" : $"{(i % 12 == 0 ? 12 : i % 12)}{(i % 24 < 12 ? "a" : "p")}";
            var ft = Text(t, size, p.SubInk, p, FontWeights.Normal);
            double x = Math.Clamp(line.X + line.Width * i / 24 - ft.Width / 2, r.X + pad * 0.5, r.Right - pad * 0.5 - ft.Width);
            dc.DrawText(ft, new Point(x, line.Bottom + h * 0.06));
        }
    }

    public override FrameworkElement BuildCard(WidgetPaint p) => ScreenTimeCards.Kinds(p, AppTracker.Today);
}
