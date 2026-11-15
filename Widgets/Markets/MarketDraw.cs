using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Markets;

/// <summary>A price with its change and the day's line, as the quote tiles draw it.</summary>
internal sealed record PricePoint(string Symbol, double Price, double ChangePct, double Baseline, IReadOnlyList<double> Line, string Currency = "USD");

/// <summary>Formatting and drawing shared by the stock, crypto and currency tiles.</summary>
internal static class MarketDraw
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A price short enough for a tile: 65,472 · 227.48 · 0.4821.</summary>
    public static string Price(double v, string currency = "USD")
    {
        string num = Math.Abs(v) >= 10_000 ? v.ToString("#,0", Inv) : Math.Abs(v) >= 1 ? v.ToString("#,0.00", Inv) : v.ToString("0.0000", Inv);
        return Symbol(currency) is { } s ? s + num : num + " " + currency;
    }

    public static string? Symbol(string currency) => currency.ToUpperInvariant() switch
    {
        "USD" or "USDT" or "USDC" or "FDUSD" => "$",
        "EUR" => "€", "GBP" => "£", "JPY" => "¥", _ => null
    };

    public static string Pct(double pct) => (pct >= 0 ? "+" : "−") + Math.Abs(pct).ToString("0.00", Inv) + "%";

    public static Brush Trend(double pct, WidgetPaint p) => pct >= 0 ? p.Good : p.Bad;

    public static FormattedText Text(string s, double size, Brush ink, WidgetPaint p, FontWeight? weight = null) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(WidgetPaint.Font, FontStyles.Normal, weight ?? FontWeights.SemiBold, FontStretches.Normal), size, ink, p.PixelsPerDip);

    /// <summary>The "SAMPLE" mark on a small backing, drawn last so it stays readable over a busy tile.</summary>
    public static void SampleBadge(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var t = Text("SAMPLE", Math.Max(7, r.Height * 0.1), p.Warn, p, FontWeights.Bold);
        var at = new Point(r.Right - t.Width - r.Height * 0.12, r.Y + r.Height * 0.07);
        dc.DrawRoundedRectangle(p.Light ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B)), null,
            new Rect(at.X - 3, at.Y - 1, t.Width + 6, t.Height + 2), 3, 3);
        dc.DrawText(t, at);
    }

    /// <summary>
    /// One instrument: name, price and change on the left, the day's line on the right with a dashed line at the
    /// reference price (previous close or the 24-hour open).
    /// </summary>
    public static void QuoteTile(DrawingContext dc, Rect r, WidgetPaint p, PricePoint q)
    {
        FormattedText text(string s, double size, Brush ink, FontWeight? weight) => Text(s, size, ink, p, weight);
        double h = r.Height, x = r.X + h * 0.16, leftW = h * 1.12;
        var brush = Trend(q.ChangePct, p);
        Left(dc, text(q.Symbol, h * 0.19, p.SubInk, FontWeights.Bold), x, r.Y + h * 0.24, leftW);
        Left(dc, text(Price(q.Price, q.Currency), h * 0.23, p.Ink, FontWeights.SemiBold), x, r.Y + h * 0.5, leftW);
        Left(dc, text(Pct(q.ChangePct), h * 0.18, brush, FontWeights.SemiBold), x, r.Y + h * 0.77, leftW);

        var area = new Rect(x + leftW + h * 0.06, r.Y + h * 0.3, Math.Max(4, r.Right - (x + leftW + h * 0.06) - h * 0.16), h * 0.52);
        if (q.Line.Count >= 2)
        {
            double min = q.Line.Min(), max = q.Line.Max();
            if (q.Baseline > 0) { min = Math.Min(min, q.Baseline); max = Math.Max(max, q.Baseline); }
            if (q.Baseline > 0 && max > min)
            {
                double y = area.Bottom - area.Height * (q.Baseline - min) / (max - min);
                dc.DrawLine(new Pen(p.Track, 1) { DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0) }, new Point(area.X, y), new Point(area.Right, y));
            }
            Line(dc, area, q.Line, min, max, brush, Math.Max(1.4, h * 0.03));
        }
        else
            Centered(dc, text("waiting for prices…", h * 0.14, p.SubInk, FontWeights.Normal), new Point(area.X + area.Width / 2, area.Y + area.Height / 2));
    }

    /// <summary>Like <see cref="Charts.Line"/>, but against a given range so the baseline and the line share one scale.</summary>
    private static void Line(DrawingContext dc, Rect area, IReadOnlyList<double> v, double min, double max, Brush stroke, double thickness)
    {
        if (max - min < 1e-12) { max = min + 1; min -= 1; }
        Point At(int i) => new(area.X + area.Width * i / (v.Count - 1), area.Bottom - area.Height * (v[i] - min) / (max - min));
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(At(0), false, false);
            for (int i = 1; i < v.Count; i++) c.LineTo(At(i), true, true);
        }
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(area.X, area.Bottom), true, true);
            for (int i = 0; i < v.Count; i++) c.LineTo(At(i), false, false);
            c.LineTo(new Point(area.Right, area.Bottom), false, false);
        }
        if (stroke is SolidColorBrush sb)
        {
            var col = sb.Color;
            dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(80, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B), 90), null, fill);
        }
        dc.DrawGeometry(null, new Pen(stroke, thickness) { LineJoin = PenLineJoin.Round }, line);
    }

    public static void Left(DrawingContext dc, FormattedText t, double x, double cy, double maxWidth)
    {
        t.MaxTextWidth = Math.Max(1, maxWidth);
        t.MaxLineCount = 1;
        t.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(t, new Point(x, cy - t.Height / 2));
    }

    public static void Centered(DrawingContext dc, FormattedText t, Point c) => dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
}
