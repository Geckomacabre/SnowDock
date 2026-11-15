using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnowDock.Widgets.SystemPlus;

/// <summary>The screen-time cards: per-app totals, and time by kind of app.</summary>
internal static class ScreenTimeCards
{
    private const string Footnote = "Counted while a screen-time tile is in the dock. Time with no mouse or keyboard input for two minutes isn't counted.";

    /// <summary>One section per period: its total and its top apps with bars.</summary>
    public static FrameworkElement Build(WidgetPaint p, params (string Name, DayRecord Day)[] sections)
    {
        var panel = new StackPanel { Width = 300 };
        foreach (var (name, day) in sections)
        {
            var apps = day.Apps.Values.Where(a => a.Total >= 60).OrderByDescending(a => a.Total).ToList();
            panel.Children.Add(Header(name, ScreenTimeTile.TotalSeconds(day), panel.Children.Count > 0));
            if (apps.Count == 0) panel.Children.Add(Faint("Nothing yet."));
            int max = apps.Count > 0 ? apps[0].Total : 1;
            foreach (var a in apps.Take(6)) panel.Children.Add(Row(p, a.Label, a.Kind, a.Total, max));
        }
        panel.Children.Add(Faint(Footnote, top: 10));
        return panel;
    }

    /// <summary>Today's time by kind of app, each with its top apps.</summary>
    public static FrameworkElement Kinds(WidgetPaint p, DayRecord day)
    {
        var panel = new StackPanel { Width = 300 };
        panel.Children.Add(Header("Today", ScreenTimeTile.TotalSeconds(day), false));
        var kinds = day.Apps.Values.GroupBy(a => a.Kind)
            .Select(g => (Kind: g.Key, Secs: g.Sum(a => a.Total), Top: g.OrderByDescending(a => a.Total).Take(3).Select(a => a.Label)))
            .Where(k => k.Secs >= 60).OrderByDescending(k => k.Secs).ToList();
        if (kinds.Count == 0) panel.Children.Add(Faint("Nothing yet."));
        int max = kinds.Count > 0 ? kinds[0].Secs : 1;
        foreach (var k in kinds)
        {
            panel.Children.Add(Row(p, AppKinds.Label(k.Kind), k.Kind, k.Secs, max));
            panel.Children.Add(Faint(string.Join(", ", k.Top), left: 16, top: -2, bottom: 4));
        }
        panel.Children.Add(Faint(Footnote, top: 10));
        return panel;
    }

    private static FrameworkElement Header(string name, int seconds, bool spaced)
    {
        var row = new DockPanel { Margin = new Thickness(0, spaced ? 12 : 0, 0, 6) };
        var total = new TextBlock { Text = ScreenTimeTile.Duration(seconds), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(total, Dock.Right);
        row.Children.Add(total);
        row.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
        return row;
    }

    /// <summary>A colour dot, a name, a bar scaled to the longest entry, and the time.</summary>
    private static FrameworkElement Row(WidgetPaint p, string label, AppKind kind, int seconds, int max)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        var dot = new Ellipse { Width = 8, Height = 8, Fill = AppKinds.Brush(kind), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = p.Track, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        track.Child = new Border
        {
            Height = 5, CornerRadius = new CornerRadius(2.5), Background = AppKinds.Brush(kind), HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(5, 90.0 * seconds / Math.Max(1, max))
        };
        var time = new TextBlock { Text = ScreenTimeTile.Duration(seconds), TextAlignment = TextAlignment.Right, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(track, 2);
        Grid.SetColumn(time, 3);
        foreach (var e in new UIElement[] { dot, name, track, time }) grid.Children.Add(e);
        return grid;
    }

    private static TextBlock Faint(string text, double left = 0, double top = 0, double bottom = 0) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(left, top, 0, bottom)
    };
}
