using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Widgets;

/// <summary>
/// The widget picker: categories down the side, a search box, and a live preview of every widget.
/// Click a tile to add it to the dock; click it again to take it off. While the picker is open the dock
/// shows a remove badge on each widget.
/// </summary>
internal sealed class PickerWindow : Window
{
    private readonly Func<string, bool> _isAdded;
    private readonly Action<string> _toggle;
    private readonly StackPanel _categories = new() { Width = 150, Margin = new Thickness(0, 0, 12, 0) };
    private readonly WrapPanel _tiles = new();
    private readonly TextBox _search = new()
    {
        FontSize = 14, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 12),
        Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), Foreground = Brushes.White, CaretBrush = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))
    };
    private string _category = "All";
    private IntPtr _hwnd;

    public bool IsOpen => IsVisible;
    public event Action? PickerClosed;

    public PickerWindow(Func<string, bool> isAdded, Action<string> toggle)
    {
        _isAdded = isAdded;
        _toggle = toggle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 820; Height = 560;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = WidgetPaint.Font;
        Foreground = Brushes.White;

        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = new Button
        {
            Content = "Done", Padding = new Thickness(14, 3, 14, 3), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x8B, 0xF6)), Foreground = Brushes.White, BorderThickness = new Thickness(0)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        title.Children.Add(new TextBlock { Text = "Widgets", FontSize = 20, FontWeight = FontWeights.SemiBold });

        // Search box with a hint that disappears once you type.
        var hint = new TextBlock { Text = "Search widgets", Opacity = 0.5, Margin = new Thickness(11, 6, 0, 0), IsHitTestVisible = false };
        _search.TextChanged += (_, _) => hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchHost = new Grid();
        searchHost.Children.Add(_search);
        searchHost.Children.Add(hint);
        var right = new DockPanel();
        DockPanel.SetDock(searchHost, Dock.Top);
        right.Children.Add(searchHost);
        right.Children.Add(new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var body = new DockPanel();
        DockPanel.SetDock(_categories, Dock.Left);
        body.Children.Add(_categories);
        body.Children.Add(right);

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        root.Children.Add(body);

        Content = new Border
        {
            Margin = new Thickness(14), CornerRadius = new CornerRadius(14), Child = root,
            Background = new SolidColorBrush(Color.FromArgb(245, 30, 30, 34)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.5 }
        };

        _search.TextChanged += (_, _) => Fill();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        };
        Closed += (_, _) => PickerClosed?.Invoke();
        BuildCategories();
        Fill();
    }

    /// <summary>Show centred on a monitor, given in physical pixels.</summary>
    public void ShowOn(Native.MonitorDesc monitor, double scale)
    {
        var b = monitor.Work;
        Left = (b.Left + b.Width / 2.0) / scale - Width / 2;
        Top = (b.Top + b.Height / 2.0) / scale - Height / 2 - 40;
        Show();
        DockWindow.ForceForeground(_hwnd);
        Activate();
        _search.Focus();
    }

    private void BuildCategories()
    {
        _categories.Children.Clear();
        foreach (var cat in new[] { "All" }.Concat(WidgetCatalog.Categories))
        {
            int count = cat == "All" ? WidgetCatalog.All.Count : WidgetCatalog.All.Count(w => w.Category == cat);
            var b = new Button
            {
                Content = $"{cat}  ({count})", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, BorderThickness = new Thickness(0), Foreground = Brushes.White,
                Background = cat == _category ? new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) : Brushes.Transparent
            };
            b.Click += (_, _) => { _category = cat; BuildCategories(); Fill(); };
            _categories.Children.Add(b);
        }
    }

    private void Fill()
    {
        _tiles.Children.Clear();
        var q = _search.Text.Trim();
        foreach (var info in WidgetCatalog.All.Where(w =>
                     (_category == "All" || w.Category == _category) &&
                     (q.Length == 0 || w.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || w.Description.Contains(q, StringComparison.OrdinalIgnoreCase))))
            _tiles.Children.Add(Tile(info));
    }

    private FrameworkElement Tile(WidgetInfo info)
    {
        bool added = _isAdded(info.Type);
        var stack = new StackPanel();
        stack.Children.Add(new Image { Source = Preview(info), Height = 64, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TextBlock { Text = info.Name, FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
        stack.Children.Add(new TextBlock { Text = info.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 6) });
        stack.Children.Add(new TextBlock { Text = added ? "✓ Added · click to remove" : "+ Add to dock", FontSize = 11.5, Foreground = added ? Brushes.LightGreen : new SolidColorBrush(Color.FromRgb(0x6E, 0xB0, 0xFF)) });
        var tile = new Border
        {
            Width = 178, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Child = stack,
            Background = new SolidColorBrush(Color.FromArgb(added ? (byte)55 : (byte)28, 255, 255, 255)),
            BorderBrush = added ? new SolidColorBrush(Color.FromRgb(0x3B, 0x8B, 0xF6)) : Brushes.Transparent, BorderThickness = new Thickness(1.5)
        };
        tile.MouseLeftButtonUp += (_, _) => { _toggle(info.Type); Fill(); };
        return tile;
    }

    /// <summary>The widget drawn for real, in a dark tile, at the dock's medium size.</summary>
    private BitmapSource Preview(WidgetInfo info)
    {
        var widget = info.Create();
        double h = 64, w = Math.Round(h * widget.WidthUnits);
        widget.Tick(DateTime.Now);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            try { widget.Draw(dc, new Rect(0, 0, w, h), WidgetPaint.For(false, VisualTreeHelper.GetDpi(this).PixelsPerDip)); }
            catch (Exception ex) { AppPaths.Log($"Preview of {info.Type} failed: {ex.Message}"); }
        }
        var rtb = new RenderTargetBitmap((int)w * 2, (int)h * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
