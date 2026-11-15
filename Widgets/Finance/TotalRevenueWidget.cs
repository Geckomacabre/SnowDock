using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>All connected stores' last 30 days added up, with a monthly goal and whether the month is on pace for it.</summary>
internal sealed class TotalRevenueWidget : OnlineWidget<TotalData>
{
    private TotalData? _live;
    private int _seenVersion = -1;
    private string? _seenSignature;

    private const double SampleGoal = 30_000;
    private double? Goal => TotalGatherer.ParseGoal(Get("goal", ""));

    protected override TimeSpan Interval => FinanceHub.WidgetInterval;
    protected override bool CanFetch => TotalGatherer.AnyReady();
    protected override TotalData Sample => TotalData.Sample;
    protected override Task<TotalData> FetchAsync(CancellationToken ct) => TotalGatherer.FetchAsync(ct);

    private TotalData? Current => IsSample ? Sample : _live ?? Shown;
    private double? ShownGoal => Goal ?? (IsSample ? SampleGoal : null);

    public override string Title => Current is { } t
        ? $"Total revenue: {FinanceMoney.Full(t.Last30, t.Currency)} in the last 30 days" + (t.Mixed ? $" ({t.Currency} stores only)" : "")
        : "Total Revenue";
    public override double WidthUnits => 2.4;
    public override bool HasCard => true;

    /// <summary>Pick up new data a store's own widget fetched, and fetch at once when a store is connected or removed.</summary>
    protected override bool TickLive(DateTime now)
    {
        int version = FinanceHub.Version;
        string signature = FinanceHub.ConnectionSignature();
        if (version == _seenVersion && signature == _seenSignature) return false;
        bool reconnected = _seenSignature != null && signature != _seenSignature;
        _seenVersion = version;
        _seenSignature = signature;
        if (reconnected)
        {
            _live = null;
            RefreshSoon();
        }
        else if (!IsSample) _live = TotalGatherer.FromHub(_live ?? Shown) ?? _live;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (!DrawState(dc, r, p) || Current is not { } t) return;
        double h = r.Height, pad = h * 0.16, inner = r.Width - pad * 2;
        // The label warns when the figure isn't the whole picture; the card says why.
        string? warning = t.Missing > 0 ? $"TOTAL · {t.Missing} MISSING" : t.Mixed ? $"TOTAL · {t.Currency} ONLY"
            : t.Covered < 30 ? "TOTAL · PARTIAL" : null;
        int count = t.Included.Count();
        RevenueWidget.Label(dc, warning ?? $"TOTAL · {count} STORE{(count == 1 ? "" : "S")}", new Point(r.X + pad, r.Y + h * 0.1), h * 0.15,
            warning != null ? p.Warn : p.SubInk, p, inner - (IsSample ? h * 0.62 : 0));
        RevenueWidget.Label(dc, Charts.Money(t.Last30, t.Currency), new Point(r.X + pad, r.Y + h * 0.29), h * 0.33, p.Ink, p, inner);

        var row = new Point(r.X + pad, r.Y + h * 0.69);
        if (ShownGoal is not double goal)
        {
            RevenueWidget.DeltaLine(dc, t.Last30, t.Prev30, t.Covered, 30, "30d", row, h * 0.165, inner, p);
            return;
        }
        // Pace: the bar is the month so far against the goal; the tick and the percentage are the projected month end.
        double pace = t.ProjectedMonth / goal;
        var brush = pace >= 1 ? p.Good : pace >= 0.8 ? p.Warn : p.Bad;
        var pct = Text($"{pace * 100:0}%", h * 0.165, brush, p, FontWeights.Bold);
        double barWidth = inner - pct.Width - h * 0.1, barH = Math.Max(4, h * 0.09);
        var bar = new Rect(row.X, row.Y + h * 0.075 - barH / 2 + h * 0.015, Math.Max(10, barWidth), barH);
        Bar(dc, bar, t.MonthToDate / goal, brush, p);
        double tick = bar.X + bar.Width * Math.Clamp(pace, 0, 1);
        dc.DrawLine(new Pen(p.Ink, 1.2), new Point(tick, bar.Y - 2), new Point(tick, bar.Bottom + 2));
        dc.DrawText(pct, new Point(r.Right - pad - pct.Width, bar.Y + bar.Height / 2 - pct.Height / 2));
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 360 };
        var t = Current;
        string status = IsSample ? "No store connected yet: showing a sample. Connect a store in its own widget (Stripe, Shopify…) and it's added here."
            : Error != null ? $"Couldn't update: {Error}"
            : UpdatedAt is DateTime u ? $"Updated {u:t}" : "Loading…";
        panel.Children.Add(RevenueCard.Note(status, p, Error != null ? p.Bad : null));

        if (t != null)
        {
            foreach (var s in t.Stores) panel.Children.Add(StoreRow(s, t, p));
            panel.Children.Add(new Border { Height = 1, Background = p.Track, Margin = new Thickness(0, 4, 0, 6) });
            panel.Children.Add(RevenueCard.Row($"Total, last 30 days ({t.Currency})", t.Last30, t.HasPrev30 ? t.Prev30 : null,
                "the 30 days before", t.Currency, p));
            if (t.Mixed)
                panel.Children.Add(RevenueCard.Note($"Mixed currencies: the total is in {t.Currency}, the currency of the first connected store. " +
                    "Stores in other currencies are listed but not counted, since there are no exchange rates here: " +
                    string.Join(", ", t.OtherCurrency.Select(s => $"{s.Service.Name} ({s.Data!.Currency})")) + ".", p, p.Warn));
            if (t.Covered < RevenueData.Days)
                panel.Children.Add(RevenueCard.Note($"Some stores have too many sales to page through, so only their last {t.Covered} days were read" +
                    (t.Covered < 30 ? "; the total is short." : "; the comparison is hidden."), p, p.Warn));
            panel.Children.Add(RevenueCard.Note(PaceText(t), p));
        }

        panel.Children.Add(new TextBlock { Text = $"Monthly goal{(t != null ? $" ({t.Currency})" : "")}", FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
        var goalBox = new TextBox
        {
            Text = Goal is double g ? g.ToString("0.##", CultureInfo.InvariantCulture) : "", Margin = new Thickness(0, 3, 0, 8),
            Padding = new Thickness(4, 3, 4, 3)
        };
        panel.Children.Add(goalBox);
        var save = new Button { Content = "Save", Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            var parsed = TotalGatherer.ParseGoal(goalBox.Text);
            Set("goal", parsed is double v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "");
            Host?.Redraw();
            Host?.CloseCard();
        };
        panel.Children.Add(save);
        panel.Children.Add(RevenueCard.Note("Each store's net means what its own card says (some are after fees, some before). " +
            "A store counts here once its key is saved; Shopify also needs its own widget for the store domain.", p));
        return new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private string PaceText(TotalData t)
    {
        string so = $"This month so far: {FinanceMoney.Full(t.MonthToDate, t.Currency)}. At the last 30 days' daily rate the month ends near " +
            FinanceMoney.Full(t.ProjectedMonth, t.Currency);
        return ShownGoal is double goal
            ? $"{so}, which is {t.ProjectedMonth / goal * 100:0}% of the {FinanceMoney.Full(goal, t.Currency)} goal" + (IsSample && Goal == null ? " (a sample goal)." : ".")
            : so + ". Set a goal below to see your pace.";
    }

    private static FrameworkElement StoreRow(StoreLine s, TotalData t, WidgetPaint p)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        var right = new TextBlock { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap, MaxWidth = 230, TextAlignment = TextAlignment.Right };
        if (s.Data is { } d)
        {
            right.Inlines.Add(new Run(FinanceMoney.Full(d.Last30, d.Currency)) { FontWeight = FontWeights.SemiBold });
            if (d.Currency != t.Currency) right.Inlines.Add(new Run("  not added") { Foreground = p.Warn });
            if (d.CoveredDays < 30) right.Inlines.Add(new Run($"  only {d.CoveredDays} days read") { Foreground = p.Warn });
            if (s.Status != null) right.Inlines.Add(new Run($"\nlast good figure ({s.Status})") { Foreground = p.Warn, FontSize = 11 });
        }
        else right.Inlines.Add(new Run(s.Status ?? "no data") { Foreground = p.SubInk });
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(new TextBlock { Text = s.Service.Name, FontSize = 12.5, Foreground = p.SubInk });
        return row;
    }
}
