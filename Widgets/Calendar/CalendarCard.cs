using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SnowDock.Widgets.CalendarWidgets;

/// <summary>The calendar tile's card: the coming events, and the calendar links (added masked, shown masked).</summary>
internal static class CalendarCard
{
    public static FrameworkElement Build(WidgetPaint p, CalendarWidget w)
    {
        var panel = new StackPanel { Width = 360 };
        if (w.Sampled)
            panel.Children.Add(Note("No calendar yet, so the tile shows a sample. Add a calendar link below.", p));
        else if (w.LastError is { } err && w.Upcoming == null)
            panel.Children.Add(new TextBlock { Text = err, Foreground = p.Bad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        else if (w.Upcoming is { } events)
        {
            if (events.Count == 0) panel.Children.Add(Note("Nothing in the next 60 days.", p));
            var list = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = p.Ink, LineHeight = 20, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var e in events)
            {
                if (list.Inlines.Count > 0) list.Inlines.Add(new LineBreak());
                list.Inlines.Add(new Run(e.Title) { FontWeight = FontWeights.SemiBold });
                list.Inlines.Add(new Run("  " + w.WhenText(e)) { Foreground = p.SubInk, FontSize = 12 });
            }
            panel.Children.Add(list);
            if (w.LastError is { } partial) panel.Children.Add(Note("Last refresh: " + partial, p));
        }
        panel.Children.Add(new Border { Height = 1, Background = p.Track, Margin = new Thickness(0, 8, 0, 8) });

        var saved = new StackPanel();
        void Refresh()
        {
            saved.Children.Clear();
            var links = CalendarWidget.SavedLinks;
            if (links.Count == 0) saved.Children.Add(Note("No calendars added.", p));
            foreach (var link in links)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = new Button { Content = "Remove", Padding = new Thickness(8, 1, 8, 1) };
                DockPanel.SetDock(remove, Dock.Right);
                remove.Click += (_, _) =>
                {
                    CalendarWidget.StoreLinks(CalendarWidget.SavedLinks.Where(l => l != link).ToList());
                    w.Changed();
                    Refresh();
                };
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = Mask(link), Foreground = p.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                saved.Children.Add(row);
            }
        }
        Refresh();
        panel.Children.Add(new TextBlock { Text = "Calendars", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(saved);

        panel.Children.Add(new TextBlock
        {
            Text = "Add a calendar's private iCal (.ics) link:\n" +
                "• Google Calendar: Settings › your calendar › Integrate calendar › Secret address in iCal format\n" +
                "• Outlook.com: Settings › Calendar › Shared calendars › Publish a calendar › ICS link\n" +
                "• iCloud: share the calendar as a Public Calendar and copy its link",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4)
        });
        var row2 = new DockPanel();
        var add = new Button { Content = "Add", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(add, Dock.Right);
        var box = new PasswordBox { Padding = new Thickness(4, 3, 4, 3) };
        row2.Children.Add(add);
        row2.Children.Add(box);
        panel.Children.Add(row2);
        var status = new TextBlock { FontSize = 12, Foreground = p.Bad, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);
        void Add()
        {
            string link = box.Password.Trim();
            if (link.Length == 0) return;
            bool web = link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
                || link.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            if (!web || !Uri.TryCreate(link.Replace("webcal://", "https://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out _))
            {
                status.Text = "That doesn't look like a calendar link (it should start with https:// or webcal://).";
                return;
            }
            var links = CalendarWidget.SavedLinks;
            if (!links.Contains(link)) links.Add(link);
            CalendarWidget.StoreLinks(links);
            box.Clear();
            status.Text = "";
            w.Changed();
            Refresh();
        }
        add.Click += (_, _) => Add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };

        panel.Children.Add(new TextBlock
        {
            Text = "Anyone with one of these links can read that calendar, so they're encrypted for your Windows account and only used to download the calendar, every 15 minutes.",
            FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
        return new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Enough of a link to tell calendars apart without showing the secret part.</summary>
    private static string Mask(string link)
    {
        var uri = Uri.TryCreate(link.Replace("webcal://", "https://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var u) ? u : null;
        string host = uri?.Host ?? "calendar";
        string file = uri?.Segments.LastOrDefault()?.Trim('/') ?? "";
        return file.Length is > 0 and <= 16 ? $"{host} · …/{file}" : $"{host} · …";
    }

    private static TextBlock Note(string text, WidgetPaint p) =>
        new() { Text = text, Foreground = p.SubInk, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
}
