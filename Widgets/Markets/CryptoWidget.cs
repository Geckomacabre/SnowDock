using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Markets;

internal sealed record CryptoQuote(string Coin, string Quote, double Price, double ChangePct, double Open, double High, double Low, IReadOnlyList<double> Line)
{
    public PricePoint Point => new(Coin, Price, ChangePct, Open, Line, Quote);
}

/// <summary>
/// A coin's price from Binance's public market data (no account or key). It uses data-api.binance.vision, the
/// host Binance's docs give for public market data only.
/// </summary>
internal sealed class CryptoWidget : OnlineWidget<CryptoQuote>
{
    private string Coin => Clean(Get("coin", "BTC"), "BTC");
    private string Quote => Clean(Get("quote", "USDT"), "USDT");
    private static string Clean(string s, string fallback) =>
        new string(s.Trim().ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray()) is { Length: > 0 } c ? c : fallback;

    public override string Title => Data is { } q ? $"{q.Coin} {MarketDraw.Price(q.Price, q.Quote)} ({MarketDraw.Pct(q.ChangePct)} in 24h)" : $"{Coin} price";
    public override double WidthUnits => 2.6;
    public override bool HasCard => true;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);

    protected override async Task<CryptoQuote> FetchAsync(CancellationToken ct)
    {
        string coin = Coin, quote = Quote, symbol = coin + quote;
        const string api = "https://data-api.binance.vision/api/v3";
        try
        {
            // 24hr ticker: https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints#24hr-ticker-price-change-statistics
            using var ticker = await Web.GetJson($"{api}/ticker/24hr?symbol={symbol}", ct: ct).ConfigureAwait(false);
            // Klines (15-minute candles, 96 = one day): same page, "Kline/Candlestick data".
            using var klines = await Web.GetJson($"{api}/klines?symbol={symbol}&interval=15m&limit=96", ct: ct).ConfigureAwait(false);
            var t = ticker.RootElement;
            var line = klines.RootElement.EnumerateArray().Select(k => Num(k[4])).Where(v => v > 0).ToList();
            return new CryptoQuote(coin, quote, Num(t.GetProperty("lastPrice")), Num(t.GetProperty("priceChangePercent")),
                Num(t.GetProperty("openPrice")), Num(t.GetProperty("highPrice")), Num(t.GetProperty("lowPrice")), line);
        }
        catch (WebError e) when (e.Status == HttpStatusCode.BadRequest)
        {
            throw new WebError($"Binance has no {coin}/{quote} pair");
        }
    }

    /// <summary>Binance sends prices as strings.</summary>
    private static double Num(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.Parse(e.GetString() ?? "0", CultureInfo.InvariantCulture);

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        if (DrawState(dc, r, p)) MarketDraw.QuoteTile(dc, r, p, Shown!.Point);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 320 };
        if (Data is { } q)
            panel.Children.Add(new TextBlock
            {
                Text = $"{q.Coin} {MarketDraw.Price(q.Price, q.Quote)}, {MarketDraw.Pct(q.ChangePct)} over 24 hours.\n" +
                    $"24h high {MarketDraw.Price(q.High, q.Quote)} · low {MarketDraw.Price(q.Low, q.Quote)}",
                TextWrapping = TextWrapping.Wrap, Foreground = p.Ink, Margin = new Thickness(0, 0, 0, 10)
            });
        else if (Error != null)
            panel.Children.Add(new TextBlock { Text = Error, Foreground = p.Bad, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });

        TextBox Field(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
            var box = LauncherWidgets.Input(p);
            box.FontSize = 13;
            box.Text = value;
            box.Margin = new Thickness(0, 3, 0, 8);
            panel.Children.Add(box);
            return box;
        }
        var coin = Field("Coin  (e.g. BTC, ETH, SOL)", Coin);
        var quote = Field("Priced in  (e.g. USDT, USDC, EUR)", Quote);
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 3, 14, 3) };
        save.Click += (_, _) =>
        {
            Set("coin", Clean(coin.Text, "BTC"));
            Set("quote", Clean(quote.Text, "USDT"));
            RefreshSoon();
            Host?.Redraw();
            Host?.CloseCard();
        };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock
        {
            Text = "Prices are Binance's public market data; no account is needed. The line shows the last 24 hours in 15-minute steps.",
            FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
        return panel;
    }
}
