using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.Online;

/// <summary>Small drawing and formatting helpers shared by the online widgets.</summary>
internal static class Charts
{
    /// <summary>A line through <paramref name="values"/> across <paramref name="area"/>, optionally filled underneath.</summary>
    public static void Line(DrawingContext dc, Rect area, IReadOnlyList<double> values, Brush stroke, double thickness = 1.6, bool fill = true)
    {
        if (values.Count < 2) return;
        double min = values.Min(), max = values.Max();
        if (max - min < 1e-9) { max = min + 1; min -= 1; }
        Point At(int i) => new(area.X + area.Width * i / (values.Count - 1), area.Bottom - area.Height * (values[i] - min) / (max - min));

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(At(0), false, false);
            for (int i = 1; i < values.Count; i++) c.LineTo(At(i), true, true);
        }
        if (fill && stroke is SolidColorBrush sb)
        {
            var area2 = new StreamGeometry();
            using (var c = area2.Open())
            {
                c.BeginFigure(new Point(area.X, area.Bottom), true, true);
                for (int i = 0; i < values.Count; i++) c.LineTo(At(i), false, false);
                c.LineTo(new Point(area.Right, area.Bottom), false, false);
            }
            var col = sb.Color;
            var grad = new LinearGradientBrush(Color.FromArgb(90, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B), 90);
            dc.DrawGeometry(grad, null, area2);
        }
        dc.DrawGeometry(null, new Pen(stroke, thickness) { LineJoin = PenLineJoin.Round }, line);
    }

    /// <summary>Simple bars (e.g. hours of a day).</summary>
    public static void Bars(DrawingContext dc, Rect area, IReadOnlyList<double> values, Brush fill)
    {
        if (values.Count == 0) return;
        double max = Math.Max(1e-9, values.Max()), w = area.Width / values.Count;
        for (int i = 0; i < values.Count; i++)
        {
            double h = Math.Max(1, area.Height * values[i] / max);
            dc.DrawRoundedRectangle(fill, null, new Rect(area.X + i * w + w * 0.15, area.Bottom - h, w * 0.7, h), 1.5, 1.5);
        }
    }

    /// <summary>Money in short form: $1,234 · $12.3k · $1.2M (currency is an ISO code like "USD").</summary>
    public static string Money(double amount, string currency = "USD")
    {
        string sym = currency.ToUpperInvariant() switch
        {
            "USD" => "$", "EUR" => "€", "GBP" => "£", "JPY" => "¥", "INR" => "₹", "CAD" => "CA$", "AUD" => "A$",
            _ => currency.ToUpperInvariant() + " "
        };
        double a = Math.Abs(amount);
        string num = a >= 1_000_000 ? (a / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M"
            : a >= 10_000 ? (a / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : a.ToString(a >= 100 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);
        return (amount < 0 ? "-" : "") + sym + num;
    }

    /// <summary>Large counts in short form: 950 · 12.3k · 1.2M.</summary>
    public static string Count(double n) =>
        n >= 1_000_000 ? (n / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : n >= 10_000 ? (n / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : n.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>Percent change, e.g. "+12%" / "-3%" / "new".</summary>
    public static string Delta(double now, double before) =>
        before == 0 ? (now == 0 ? "0%" : "new") : $"{(now - before) / Math.Abs(before) * 100:+0;-0;0}%";

    public static Brush DeltaBrush(double now, double before, WidgetPaint p) => now >= before ? p.Good : p.Bad;
}
