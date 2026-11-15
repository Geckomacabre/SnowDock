using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

[WidgetFamily(60)]
internal static class FinanceWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Finance;
        foreach (var svc in FinanceHub.Services)
        {
            add(svc.Id, svc.Name, c, $"Net revenue for the last 30 days ({svc.NetShort}), with the change from the 30 days before.",
                () => new RevenueWidget(svc, chart: false));
            add(svc.Id + "-chart", svc.Name + " Chart", c, $"A 14-day line of {svc.Name} net revenue, with the last 7 days set against the 7 before.",
                () => new RevenueWidget(svc, chart: true));
        }
        add("total-revenue", "Total Revenue", c, "Every connected store's last 30 days added up, with a monthly goal and whether you're on pace for it.",
            () => new TotalRevenueWidget());
    }
}

/// <summary>A store's net revenue: the 30-day figure, or (as a chart) a 14-day line with this week against last.</summary>
internal sealed class RevenueWidget : AccountWidget<RevenueData>
{
    private readonly FinanceService _svc;
    private readonly bool _chart;
    private string _remembered = "";

    public RevenueWidget(FinanceService svc, bool chart)
    {
        _svc = svc;
        _chart = chart;
    }

    protected override string Service => _svc.Id;
    protected override string ServiceName => _svc.Name;
    protected override string KeyLabel => _svc.KeyLabel;
    protected override string[] Steps => _svc.Steps;
    protected override string? DashboardUrl => _svc.DashboardUrl;
    protected override AccountField[] Fields => _svc.Fields;
    protected override TimeSpan Interval => FinanceHub.WidgetInterval;
    protected override RevenueData Sample => _svc.Sample;
    protected override bool CanFetch => KeyStore.Has(Service) && _svc.HasRequired(Field);

    public override double WidthUnits => _chart ? 2.4 : 2;

    public override string Title
    {
        get
        {
            if (Shown is not { } d) return _svc.Name;
            return _chart
                ? $"{_svc.Name}: {FinanceMoney.Full(d.Week, d.Currency)} in the last 7 days"
                : $"{_svc.Name}: {FinanceMoney.Full(d.Last30, d.Currency)} net in the last 30 days";
        }
    }

    private Dictionary<string, string> CurrentFields() => _svc.Fields.ToDictionary(f => f.Id, f => Field(f.Id));

    public override bool Tick(DateTime now)
    {
        // Share this store's settings (a shop domain) with the total, which has no card field for them.
        if (_svc.Fields.Length > 0)
        {
            var sig = string.Join("\n", _svc.Fields.Select(f => Field(f.Id)));
            if (sig != _remembered && _svc.HasRequired(Field))
            {
                FinanceHub.Remember(_svc.Id, CurrentFields());
                _remembered = sig;
            }
        }
        return base.Tick(now);
    }

    protected override Task<RevenueData> FetchAsync(CancellationToken ct) => FinanceHub.Get(_svc, Key, CurrentFields(), ct);

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (!DrawState(dc, r, p) || Shown is not { } d) return;
        double h = r.Height, pad = h * 0.16;
        double labelWidth = r.Width - pad * 2 - (IsSample ? h * 0.62 : 0); // keep clear of the SAMPLE mark
        Label(dc, _svc.Name.ToUpperInvariant() + (_chart ? " · 7D" : ""), new Point(r.X + pad, r.Y + h * 0.1), h * 0.15, p.SubInk, p, labelWidth);

        var bottom = new Point(r.X + pad, r.Y + h * 0.69);
        if (!_chart)
        {
            Label(dc, Charts.Money(d.Last30, d.Currency), new Point(r.X + pad, r.Y + h * 0.29), h * 0.33, p.Ink, p, r.Width - pad * 2);
            DeltaLine(dc, d.Last30, d.Prev30, d.CoveredDays, 30, "30d", bottom, h * 0.165, r.Width - pad * 2, p);
            return;
        }

        // Money on the left, the line on the right, and the comparison across the bottom.
        Label(dc, Charts.Money(d.Week, d.Currency), new Point(r.X + pad, r.Y + h * 0.29), h * 0.3, p.Ink, p, r.Width * 0.5 - pad);
        DeltaLine(dc, d.Week, d.PrevWeek, d.CoveredDays, 7, "wk", bottom, h * 0.165, r.Width - pad * 2, p);
        var area = new Rect(r.X + r.Width * 0.53, r.Y + h * 0.3, r.Width * 0.47 - pad * 0.8, h * 0.34);
        DrawWeeks(dc, area, d.Recent(14), p);
    }

    /// <summary>The 14-day line, a faint mark where this week starts, and a dot on today.</summary>
    private static void DrawWeeks(DrawingContext dc, Rect area, double[] values, WidgetPaint p)
    {
        double split = area.X + area.Width * 7 / 13.0;
        dc.DrawLine(new Pen(p.Track, 1) { DashStyle = DashStyles.Dot }, new Point(split, area.Y), new Point(split, area.Bottom));
        Charts.Line(dc, area, values, p.Accent, 1.6);
        double min = values.Min(), max = values.Max();
        if (max - min < 1e-9) { max = min + 1; min -= 1; }
        dc.DrawEllipse(p.Accent, null, new Point(area.Right, area.Bottom - area.Height * (values[^1] - min) / (max - min)), 2.2, 2.2);
    }

    /// <summary>Left-aligned text, shrunk to fit.</summary>
    internal static void Label(DrawingContext dc, string s, Point topLeft, double size, Brush ink, WidgetPaint p, double maxWidth, FontWeight? weight = null)
    {
        var t = Text(s, size, ink, p, weight);
        if (t.Width > maxWidth && maxWidth > 0) t = Text(s, size * maxWidth / t.Width, ink, p, weight);
        dc.DrawText(t, new Point(topLeft.X, topLeft.Y + (size - t.Height) / 2 + size * 0.1));
    }

    /// <summary>
    /// "▲ 12% vs prior 30d" in green or red. When paging stopped early (<paramref name="covered"/> days read, fewer
    /// than twice the period), it says so instead, in orange if even the headline figure is short.
    /// </summary>
    internal static void DeltaLine(DrawingContext dc, double now, double before, int covered, int periodDays, string period, Point at,
        double size, double maxWidth, WidgetPaint p)
    {
        if (covered < periodDays * 2)
        {
            Label(dc, $"only {covered} days read", at, size, covered < periodDays ? p.Warn : p.SubInk, p, maxWidth);
            return;
        }
        var change = Text(FinanceMoney.Change(now, before), size, Charts.DeltaBrush(now, before, p), p, FontWeights.Bold);
        var rest = Text(" vs prior " + period, size, p.SubInk, p, FontWeights.Normal);
        double scale = Math.Min(1, maxWidth / Math.Max(1, change.Width + rest.Width));
        if (scale < 1)
        {
            change = Text(change.Text, size * scale, Charts.DeltaBrush(now, before, p), p, FontWeights.Bold);
            rest = Text(rest.Text, size * scale, p.SubInk, p, FontWeights.Normal);
        }
        double y = at.Y + (size - change.Height) / 2 + size * 0.1;
        dc.DrawText(change, new Point(at.X, y));
        dc.DrawText(rest, new Point(at.X + change.Width, y));
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } d ? RevenueCard.Detail(d, _svc, p) : null;

    /// <summary>Before connecting, the card still says what "net" will mean for this service.</summary>
    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var card = base.BuildCard(p);
        if (IsSample && card is ScrollViewer { Content: StackPanel panel })
            panel.Children.Insert(1, RevenueCard.Note("Net here means " + _svc.NetLong, p));
        return card;
    }
}
