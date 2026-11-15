using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Markets;

[WidgetFamily(50)]
internal static class CurrencyFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add) =>
        add("currency", "Currency", WidgetCatalog.Productivity,
            "An amount converted from one currency to another at the European Central Bank's latest rate.", () => new CurrencyWidget());
}

internal sealed record FxRate(string From, string To, double Rate, DateTime Date);

/// <summary>Converts an amount at the ECB's daily reference rate, fetched from the Frankfurter API (no key).</summary>
internal sealed class CurrencyWidget : OnlineWidget<FxRate>
{
    /// <summary>The currencies the ECB publishes reference rates for.</summary>
    private static readonly string[] Currencies =
    {
        "AUD", "BGN", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR", "ISK", "JPY",
        "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "USD", "ZAR"
    };

    private string From => Code(Get("from", "USD"), "USD");
    private string To => Code(Get("to", "EUR"), "EUR");
    private double Amount => double.TryParse(Get("amount", "100"), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && a > 0 ? a : 100;
    private static string Code(string s, string fallback) =>
        new string(s.Trim().ToUpperInvariant().Where(char.IsAsciiLetter).ToArray()) is { Length: 3 } c ? c : fallback;

    public override string Title => Data is { } r ? $"{AmountText} {r.From} = {Num(Amount * r.Rate)} {r.To}" : "Currency";
    private string AmountText => Amount.ToString("#,0.##", CultureInfo.CurrentCulture);
    public override double WidthUnits => 2.5;
    public override bool HasCard => true;
    /// <summary>The ECB publishes once each working day, so hourly is plenty.</summary>
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    private static string Num(double v) => v.ToString(Math.Abs(v) >= 1000 ? "#,0" : Math.Abs(v) >= 1 ? "#,0.00" : "0.0000", CultureInfo.CurrentCulture);

    protected override async Task<FxRate> FetchAsync(CancellationToken ct)
    {
        string from = From, to = To;
        if (from == to) return new FxRate(from, to, 1, DateTime.Today);
        try
        {
            // ECB rates for one pair: https://frankfurter.dev/ ("For European Central Bank data, use /v2/providers/ecb/…").
            using var doc = await Web.GetJson($"https://api.frankfurter.dev/v2/providers/ecb/rate/{from.ToLowerInvariant()}/{to.ToLowerInvariant()}", ct: ct)
                .ConfigureAwait(false);
            var r = doc.RootElement;
            return new FxRate(from, to, r.GetProperty("rate").GetDouble(),
                DateTime.ParseExact(r.GetProperty("date").GetString() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (WebError e) when (e.Status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
        {
            throw new WebError($"No ECB rate for {from} to {to}");
        }
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        // Before the first reply (and in the picker's preview) the default pair shows a marked sample.
        bool placeholder = Data == null && Error == null && From == "USD" && To == "EUR";
        if (!placeholder && !DrawState(dc, r, p)) return;
        var fx = placeholder ? new FxRate("USD", "EUR", 0.9048, DateTime.Today) : Shown!;
        double h = r.Height, cx = r.X + r.Width / 2, w = r.Width - h * 0.3;
        Centered(dc, $"{AmountText} {fx.From} =", new Point(cx, r.Y + h * 0.24), h * 0.17, p.SubInk, p, w, FontWeights.Normal);
        Centered(dc, $"{Num(Amount * fx.Rate)} {fx.To}", new Point(cx, r.Y + h * 0.52), h * 0.28, p.Ink, p, w);
        Centered(dc, $"ECB · {fx.Date:MMM d}", new Point(cx, r.Y + h * 0.81), h * 0.14, p.SubInk, p, w, FontWeights.Normal);
        if (placeholder) MarketDraw.SampleBadge(dc, r, p);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 300 };
        if (Data is { } fx)
            panel.Children.Add(new TextBlock
            {
                Text = $"1 {fx.From} = {fx.Rate.ToString("0.#####", CultureInfo.CurrentCulture)} {fx.To}  ·  1 {fx.To} = {(1 / fx.Rate).ToString("0.#####", CultureInfo.CurrentCulture)} {fx.From}\nECB reference rate of {fx.Date:D}",
                Foreground = p.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
        else if (Error != null)
            panel.Children.Add(new TextBlock { Text = Error, Foreground = p.Bad, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(new TextBlock { Text = "Amount", FontSize = 12 });
        var amount = LauncherWidgets.Input(p);
        amount.FontSize = 13;
        amount.Text = Amount.ToString(CultureInfo.CurrentCulture);
        amount.Margin = new Thickness(0, 3, 0, 8);
        panel.Children.Add(amount);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        ComboBox Pick(string value, int column)
        {
            var box = new ComboBox { ItemsSource = Currencies, IsEditable = true, Text = value };
            Grid.SetColumn(box, column);
            row.Children.Add(box);
            return box;
        }
        var from = Pick(From, 0);
        var swap = new Button { Content = "⇄", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 6, 0), ToolTip = "Swap" };
        Grid.SetColumn(swap, 1);
        row.Children.Add(swap);
        var to = Pick(To, 2);
        swap.Click += (_, _) => (from.Text, to.Text) = (to.Text, from.Text);
        panel.Children.Add(new TextBlock { Text = "From and to", FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(row);

        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 10, 0, 0) };
        save.Click += (_, _) =>
        {
            if (double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var a) && a > 0)
                Set("amount", a.ToString(CultureInfo.InvariantCulture));
            Set("from", Code(from.Text, From));
            Set("to", Code(to.Text, To));
            RefreshSoon();
            Host?.Redraw();
            Host?.CloseCard();
        };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock
        {
            Text = "Rates are the European Central Bank's daily reference rates, via the free Frankfurter API. Good for a rough idea, not for trading.",
            FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
        return panel;
    }
}
