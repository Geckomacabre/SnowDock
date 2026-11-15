using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnowDock.Widgets;

internal static class LauncherWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Apps;
        add("search", "Search", c, "Search the web, or type a folder or file path to open it.", () => new SearchWidget());
        add("commands", "Commands", c, "Type a few letters to launch an app from your Start menu, or a system action.", () => new CommandWidget());
        add("recent", "Recently Opened", c, "The files and folders you opened last.", () => new RecentWidget());
    }

    public static void DrawGlyphTile(DrawingContext dc, Rect r, WidgetPaint p, Action<DrawingContext, Point, double, Pen> glyph)
    {
        dc.DrawRoundedRectangle(p.Tile, p.TileRim, r, Math.Min(14, r.Height * 0.24), Math.Min(14, r.Height * 0.24));
        double u = r.Height * 0.09;
        var pen = new Pen(p.Ink, Math.Max(1.6, u * 0.4)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        glyph(dc, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), u, pen);
    }

    public static TextBox Input(WidgetPaint p) => new()
    {
        FontSize = 15, Padding = new Thickness(6, 4, 6, 4),
        Background = p.Light ? Brushes.White : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
        Foreground = p.Ink, CaretBrush = p.Ink, BorderBrush = p.TileRim.Brush
    };
}

internal sealed class SearchWidget : DockWidget
{
    public override string Title => "Search";
    public override bool HasCard => true;

    private string Engine => Get("engine", "Bing");
    private static readonly Dictionary<string, string> Engines = new()
    {
        ["Bing"] = "https://www.bing.com/search?q=", ["Google"] = "https://www.google.com/search?q=",
        ["DuckDuckGo"] = "https://duckduckgo.com/?q="
    };

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p) =>
        LauncherWidgets.DrawGlyphTile(dc, r, p, (g, c, u, pen) =>
        {
            g.DrawEllipse(null, pen, new Point(c.X - u * 0.6, c.Y - u * 0.6), u * 1.9, u * 1.9);
            g.DrawLine(pen, new Point(c.X + u * 0.75, c.Y + u * 0.75), new Point(c.X + u * 2.4, c.Y + u * 2.4));
        });

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 380 };
        var input = LauncherWidgets.Input(p);
        var engines = new ComboBox { ItemsSource = Engines.Keys, SelectedItem = Engine, Margin = new Thickness(0, 8, 0, 0), Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        engines.SelectionChanged += (_, _) => { if (engines.SelectedItem is string e) Set("engine", e); };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var q = input.Text.Trim();
            if (q.Length == 0) return;
            var path = Environment.ExpandEnvironmentVariables(q.Trim('"'));
            if (Directory.Exists(path) || File.Exists(path)) DockWindow.ShellOpen(path);
            else if (Uri.TryCreate(q, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")) DockWindow.ShellOpen(q);
            else if (q.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) DockWindow.ShellOpen("https://" + q);
            else DockWindow.ShellOpen(Engines.GetValueOrDefault(Engine, Engines["Bing"]) + Uri.EscapeDataString(q));
            Host?.CloseCard();
        };
        panel.Children.Add(input);
        panel.Children.Add(engines);
        panel.Loaded += (_, _) => input.Focus();
        return panel;
    }
}

internal sealed class CommandWidget : DockWidget
{
    public override string Title => "Commands";
    public override bool HasCard => true;

    private static readonly (string Name, string Target)[] Builtins =
    {
        ("Settings", "ms-settings:"), ("Task Manager", "taskmgr.exe"), ("File Explorer", "explorer.exe"),
        ("Control Panel", "control.exe"), ("Calculator", "calc.exe"), ("Notepad", "notepad.exe"),
        ("Lock PC", "rundll32.exe|user32.dll,LockWorkStation"), ("Sound Settings", "ms-settings:sound"),
        ("Display Settings", "ms-settings:display"), ("Bluetooth Settings", "ms-settings:bluetooth"),
        ("Recycle Bin", "shell:RecycleBinFolder"), ("Downloads", "shell:Downloads")
    };

    /// <summary>Start-menu shortcuts (yours and everyone's), read fresh each time the card opens.</summary>
    private static List<(string Name, string Target)> Index()
    {
        var list = new List<(string, string)>(Builtins);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                 })
        {
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(Path.Combine(root, "Programs"), "*.lnk", SearchOption.AllDirectories))
                    list.Add((Path.GetFileNameWithoutExtension(lnk), lnk));
            }
            catch { }
        }
        return list.DistinctBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Launch(string target)
    {
        var parts = target.Split('|', 2);
        DockWindow.ShellOpen(parts[0], parts.Length > 1 ? parts[1] : null);
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p) =>
        LauncherWidgets.DrawGlyphTile(dc, r, p, (g, c, u, pen) =>
        {
            g.DrawLine(pen, new Point(c.X - u * 2.4, c.Y - u * 1.6), new Point(c.X - u * 0.8, c.Y));
            g.DrawLine(pen, new Point(c.X - u * 0.8, c.Y), new Point(c.X - u * 2.4, c.Y + u * 1.6));
            g.DrawLine(pen, new Point(c.X + u * 0.2, c.Y + u * 1.8), new Point(c.X + u * 2.6, c.Y + u * 1.8));
        });

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var all = Index();
        var panel = new StackPanel { Width = 380 };
        var input = LauncherWidgets.Input(p);
        var results = new ListBox { MaxHeight = 260, Margin = new Thickness(0, 8, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = p.Ink };
        void Filter()
        {
            var q = input.Text.Trim();
            var hits = all.Where(x => q.Length == 0 || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(x => q.Length > 0 && x.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                          .ThenBy(x => x.Name).Take(9).ToList();
            results.ItemsSource = hits.Select(h => h.Name).ToList();
            results.Tag = hits;
            if (hits.Count > 0) results.SelectedIndex = 0;
        }
        void Go()
        {
            if (results.Tag is List<(string Name, string Target)> hits && results.SelectedIndex >= 0 && results.SelectedIndex < hits.Count)
            {
                Launch(hits[results.SelectedIndex].Target);
                Host?.CloseCard();
            }
        }
        input.TextChanged += (_, _) => Filter();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { results.SelectedIndex = Math.Min(results.Items.Count - 1, results.SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == Key.Up) { results.SelectedIndex = Math.Max(0, results.SelectedIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Enter) { Go(); e.Handled = true; }
        };
        results.MouseDoubleClick += (_, _) => Go();
        Filter();
        panel.Children.Add(input);
        panel.Children.Add(results);
        panel.Loaded += (_, _) => input.Focus();
        return panel;
    }
}

internal sealed class RecentWidget : DockWidget
{
    public override string Title => "Recently Opened";
    public override bool HasCard => true;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p) =>
        LauncherWidgets.DrawGlyphTile(dc, r, p, (g, c, u, pen) =>
        {
            g.DrawEllipse(null, pen, c, u * 2.4, u * 2.4);
            g.DrawLine(pen, c, new Point(c.X, c.Y - u * 1.5));
            g.DrawLine(pen, c, new Point(c.X + u * 1.1, c.Y + u * 0.6));
        });

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 340 };
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        List<FileInfo> items;
        try
        {
            items = new DirectoryInfo(folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(15).ToList();
        }
        catch { items = new(); }
        foreach (var f in items)
        {
            var name = Path.GetFileNameWithoutExtension(f.Name);
            var button = new Button
            {
                Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = p.Ink, Padding = new Thickness(4, 3, 4, 3), Cursor = Cursors.Hand
            };
            button.Click += (_, _) => { DockWindow.ShellOpen(f.FullName); Host?.CloseCard(); };
            panel.Children.Add(button);
        }
        if (items.Count == 0) panel.Children.Add(new TextBlock { Text = "Nothing opened recently.", Opacity = 0.7 });
        return new ScrollViewer { Content = panel, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
