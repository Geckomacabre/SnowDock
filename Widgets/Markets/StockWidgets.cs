using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Markets;

[WidgetFamily(45)]
internal static class MarketWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Stocks;
        add("stock-chart", "Stock", c, "One stock's price and change today, with a line of the prices seen since SnowDock started (needs a free Finnhub key).", () => new StockChart());
        add("stock-watchlist", "Watchlist", c, "Three stocks side by side, each with its price and today's change (needs a free Finnhub key).", () => new StockWatchlist());
        add("crypto", "Crypto", c, "A coin's price, its change over 24 hours and a line of the last day, from Binance's public prices.", () => new CryptoWidget());
    }
}

/// <summary>Shared by the Finnhub tiles: the key, how to get one, and a SAMPLE badge that stays readable.</summary>
internal abstract class FinnhubWidget<T> : AccountWidget<T> where T : class
{
    protected override string Service => Finnhub.Service;
    protected override string ServiceName => "Finnhub";
    protected override string KeyLabel => "Finnhub API key";
    protected override string? DashboardUrl => "https://finnhub.io/dashboard";
    protected override string[] Steps => new[]
    {
        "Sign up for a free account at finnhub.io.",
        "Copy the API key shown on your dashboard.",
        "Paste it below; every stock tile shares it."
    };
    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);
    /// <summary>A key is enough: an empty symbol field means the default symbols.</summary>
    protected override bool CanFetch => KeyStore.Has(Service);

    /// <summary>Draws the tile, the content (or loading/error), and the badge last.</summary>
    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (IsSample ? Shown != null : DrawState(dc, r, p)) DrawData(dc, r, p, Shown!);
        if (IsSample) MarketDraw.SampleBadge(dc, r, p);
    }

    protected abstract void DrawData(DrawingContext dc, Rect r, WidgetPaint p, T data);

    protected static FrameworkElement Note(string text, WidgetPaint p) =>
        new TextBlock { Text = text, FontSize = 12, Foreground = p.SubInk, TextWrapping = TextWrapping.Wrap };
}

internal sealed class StockChart : FinnhubWidget<StockQuote>
{
    private string Symbol => Finnhub.Clean(Field("symbol")) is { Length: > 0 } s ? s : "AAPL";
    public override string Title => !IsSample && Data is { } q ? $"{q.Symbol} {MarketDraw.Price(q.Price)} ({MarketDraw.Pct(q.ChangePct)})" : "Stock";
    public override double WidthUnits => 2.6;
    protected override AccountField[] Fields => new[] { new AccountField("symbol", "Symbol", "e.g. MSFT; default AAPL") };
    private static readonly StockQuote SampleQuote = Finnhub.Sample("AAPL", 226.1, 225.4, 7);
    protected override StockQuote? Sample => SampleQuote;

    protected override Task<StockQuote> FetchAsync(CancellationToken ct) => Finnhub.Get(Symbol, Key, ct);

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, StockQuote q) => MarketDraw.QuoteTile(dc, r, p, q.Point);

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { } q
        ? Note($"{q.Symbol} {MarketDraw.Price(q.Price)}, {MarketDraw.Pct(q.ChangePct)} today. Open {MarketDraw.Price(q.Open)} · high {MarketDraw.Price(q.High)} · " +
            $"low {MarketDraw.Price(q.Low)} · previous close {MarketDraw.Price(q.PrevClose)}.\nThe line shows prices sampled every couple of minutes " +
            "while SnowDock runs (Finnhub's free plan has no intraday history), starting from today's open.", p)
        : null;
}

internal sealed class StockWatchlist : FinnhubWidget<IReadOnlyList<StockQuote>>
{
    private static readonly string[] Defaults = { "AAPL", "MSFT", "NVDA" };
    private string[] Symbols
    {
        get
        {
            var typed = Field("symbols").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Finnhub.Clean).Where(s => s.Length > 0).Distinct().Take(3).ToArray();
            return typed.Length > 0 ? typed : Defaults;
        }
    }

    public override string Title => !IsSample && Data is { Count: > 0 } l ? string.Join(" · ", l.Select(q => $"{q.Symbol} {MarketDraw.Pct(q.ChangePct)}")) : "Watchlist";
    public override double WidthUnits => 3.2;
    protected override AccountField[] Fields => new[] { new AccountField("symbols", "Symbols", "up to 3; default AAPL, MSFT, NVDA") };
    private static readonly StockQuote[] SampleList =
        { Finnhub.Sample("AAPL", 226.1, 225.4, 7), Finnhub.Sample("MSFT", 418.2, 421.0, 3), Finnhub.Sample("NVDA", 137.5, 135.9, 11) };
    protected override IReadOnlyList<StockQuote>? Sample => SampleList;

    protected override async Task<IReadOnlyList<StockQuote>> FetchAsync(CancellationToken ct) =>
        await Task.WhenAll(Symbols.Select(s => Finnhub.Get(s, Key, ct))).ConfigureAwait(false);

    protected override void DrawData(DrawingContext dc, Rect r, WidgetPaint p, IReadOnlyList<StockQuote> list)
    {
        double h = r.Height, col = (r.Width - h * 0.2) / Math.Max(1, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var q = list[i];
            double cx = r.X + h * 0.1 + col * (i + 0.5);
            if (i > 0) dc.DrawLine(new Pen(p.Track, 1), new Point(cx - col / 2, r.Y + h * 0.2), new Point(cx - col / 2, r.Bottom - h * 0.2));
            Centered(dc, q.Symbol, new Point(cx, r.Y + h * 0.3), h * 0.17, p.SubInk, p, col - 6, FontWeights.Bold);
            Centered(dc, MarketDraw.Price(q.Price), new Point(cx, r.Y + h * 0.55), h * 0.21, p.Ink, p, col - 6);
            Centered(dc, MarketDraw.Pct(q.ChangePct), new Point(cx, r.Y + h * 0.8), h * 0.17, MarketDraw.Trend(q.ChangePct, p), p, col - 6);
        }
    }

    protected override FrameworkElement? BuildDetail(WidgetPaint p) => Shown is { Count: > 0 } l
        ? Note(string.Join("\n", l.Select(q => $"{q.Symbol}: {MarketDraw.Price(q.Price)} ({MarketDraw.Pct(q.ChangePct)}), " +
            $"range {MarketDraw.Price(q.Low)}–{MarketDraw.Price(q.High)}")), p)
        : null;
}
