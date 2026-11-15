using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>Card pieces shared by the store widgets and the total.</summary>
internal static class RevenueCard
{
    /// <summary>The figures behind a store's tiles, a 30-day line, and what the numbers mean.</summary>
    public static FrameworkElement Detail(RevenueData d, FinanceService svc, WidgetPaint p)
    {
        var panel = new StackPanel();
        panel.Children.Add(Row("Last 30 days", d.Last30, d.HasPrev30 ? d.Prev30 : null, "the 30 days before", d.Currency, p));
        panel.Children.Add(Row("Last 7 days", d.Week, d.HasPrevWeek ? d.PrevWeek : null, "the 7 days before", d.Currency, p));
        panel.Children.Add(Row("This month so far", d.MonthToDate, null, "", d.Currency, p));
        panel.Children.Add(Chart(d.Recent(30), p));
        panel.Children.Add(Note("Net here means " + svc.NetLong, p));
        foreach (var warning in Warnings(d)) panel.Children.Add(Note(warning, p, p.Warn));
        return panel;
    }

    public static IEnumerable<string> Warnings(RevenueData d)
    {
        if (d.CoveredDays < RevenueData.Days)
            yield return $"Only the last {d.CoveredDays} days could be read in full (too many sales to page through), so comparisons that need older days are hidden.";
        if (d.LeftOut.Count > 0)
            yield return $"Shown in {d.Currency}. Sales in {string.Join(", ", d.LeftOut)} are left out, since they can't be added without exchange rates.";
    }

    /// <summary>"Label ........ $12,345  ▲ 12%", with the earlier figure in small print underneath.</summary>
    public static FrameworkElement Row(string label, double value, double? before, string beforeLabel, string currency, WidgetPaint p)
    {
        var row = new DockPanel();
        var right = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
        right.Inlines.Add(new Run(FinanceMoney.Full(value, currency)) { FontWeight = FontWeights.SemiBold });
        if (before is double b)
            right.Inlines.Add(new Run("  " + FinanceMoney.Change(value, b)) { Foreground = Charts.DeltaBrush(value, b, p), FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = p.SubInk });

        var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };
        wrap.Children.Add(row);
        if (before is double b2)
            wrap.Children.Add(new TextBlock
            {
                Text = $"{FinanceMoney.Full(b2, currency)} in {beforeLabel}", FontSize = 11, Foreground = p.SubInk,
                HorizontalAlignment = HorizontalAlignment.Right
            });
        return wrap;
    }

    /// <summary>A day-by-day line with its first and last day marked underneath.</summary>
    public static FrameworkElement Chart(double[] values, WidgetPaint p, double width = 328, double height = 64)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            dc.DrawLine(new Pen(p.Track, 1), new Point(0, height - 0.5), new Point(width, height - 0.5));
            Charts.Line(dc, new Rect(1, 3, width - 2, height - 6), values, p.Accent, 1.8);
        }
        var image = new Image { Source = new DrawingImage(group), Width = width, Height = height, Stretch = Stretch.None };
        var captions = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var today = new TextBlock { Text = "today", FontSize = 10.5, Foreground = p.SubInk };
        DockPanel.SetDock(today, Dock.Right);
        captions.Children.Add(today);
        captions.Children.Add(new TextBlock { Text = $"{values.Length} days ago", FontSize = 10.5, Foreground = p.SubInk });
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 8), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(image);
        panel.Children.Add(captions);
        return panel;
    }

    public static TextBlock Note(string text, WidgetPaint p, Brush? ink = null) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6),
        Foreground = ink ?? p.SubInk
    };
}
