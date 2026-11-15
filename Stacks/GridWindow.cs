using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shape = System.Windows.Shapes;
using SnowDock.Native;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Stacks;

/// <summary>
/// A stack's grid view, after 10.6: a dark rounded panel with a pointer down to the stack, the folder's
/// items as icons with names, scrolling for big folders, and folders you can step into (with a back button).
/// </summary>
internal sealed class GridWindow : Window
{
    private const double TileW = 100, TileH = 112, Pad = 14, PointerH = 14, HeaderH = 40, FooterH = 40, MaxItems = 500;
    private static readonly Brush PanelBrush = Frozen(new SolidColorBrush(Color.FromArgb(235, 34, 34, 37)));
    private static readonly Brush HoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)));

    private readonly Grid _root = new();
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _back;
    private readonly TextBlock _footerText = new() { Foreground = Brushes.White, FontSize = 12.5 };
    private readonly WrapPanel _wrap = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Shape.Path _pointer = new() { Fill = PanelBrush, Data = Geometry.Parse("M0,0 L24,0 L12,14 Z") };
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly Stack<string> _history = new();
    private IntPtr _hwnd;
    private bool _closing, _returnFocus;
    private string _path = "";

    public StackFolder? Stack { get; private set; }
    public bool IsOpen => IsVisible && !_closing;
    public IntPtr Handle => _hwnd;
    public event Action<bool>? GridClosed;

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public GridWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000;
        FontFamily = new FontFamily("Lucida Sans Unicode, Lucida Grande, Segoe UI");
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        _back = Pill("‹  Back", () => GoBack());
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        _back.Margin = new Thickness(12, 0, 0, 0);
        var header = new Grid { Height = HeaderH };
        header.Children.Add(_title);
        header.Children.Add(_back);

        var footer = Pill("", () => { DockWindow.ShellOpen(_path); CloseGrid(); });
        footer.Child = _footerText;
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        footer.VerticalAlignment = VerticalAlignment.Center;
        var footerHost = new Grid { Height = FooterH };
        footerHost.Children.Add(footer);

        _scroll.Content = _wrap;
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footerHost, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(footerHost);
        dock.Children.Add(_scroll);

        var panel = new Border
        {
            CornerRadius = new CornerRadius(10), Background = PanelBrush, Child = dock,
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55 }
        };
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PointerH) });
        _root.Children.Add(panel);
        Grid.SetRow(_pointer, 1);
        _pointer.HorizontalAlignment = HorizontalAlignment.Left;
        _pointer.Margin = new Thickness(0, -1, 0, 0);
        _root.Children.Add(_pointer);
        _root.Margin = new Thickness(Pad);
        _root.RenderTransform = _zoom;
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        };
        Deactivated += (_, _) => CloseGrid();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseGrid(returnFocus: true);
            else if (e.Key == Key.Back) GoBack();
        };
    }

    /// <summary>Open above <paramref name="anchorPx"/> (stack icon center X, top Y; physical pixels).</summary>
    /// <summary>Size the panel for <paramref name="stack"/> and fill it. Returns the panel size (DIPs).</summary>
    private Size Prepare(StackFolder stack, double maxPanelH)
    {
        Stack = stack;
        _history.Clear();
        _path = "";
        int count = Math.Min(stack.TotalCount, (int)MaxItems);
        int cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, count) * 1.4)), 3, 7);
        int rows = Math.Max(1, (int)Math.Ceiling(count / (double)cols));
        double panelW = cols * TileW + 24, panelH = Math.Min(maxPanelH, HeaderH + FooterH + rows * TileH + 8);
        _wrap.Width = cols * TileW;
        _root.Width = panelW;
        _root.Height = panelH + PointerH;
        _root.RowDefinitions[0].Height = new GridLength(panelH);
        Navigate(stack.Config.Path, remember: false);
        return new Size(panelW, panelH);
    }

    /// <summary>Debug: render the grid for <paramref name="stack"/> to a PNG without showing a window.</summary>
    public void SaveSnapshot(StackFolder stack, string path)
    {
        var size = Prepare(stack, 660);
        _pointer.Margin = new Thickness(size.Width / 2 - 12, -1, 0, 0);
        LazyIcon.Flush();
        Content = null; // render the panel on its own
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x6A)), Child = _root, Padding = new Thickness(20) };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public void Open(StackFolder stack, Point anchorPx, double scale, MonitorDesc monitor)
    {
        double maxPanelH = Math.Min(660, (anchorPx.Y - monitor.Bounds.Top) / scale - PointerH - Pad * 2 - 30);
        var panel = Prepare(stack, maxPanelH);
        double panelW = panel.Width, panelH = panel.Height;

        int w = (int)Math.Ceiling((panelW + Pad * 2) * scale), h = (int)Math.Ceiling((panelH + PointerH + Pad * 2) * scale);
        int left = (int)Math.Clamp(anchorPx.X - w / 2.0, monitor.Bounds.Left + 4, monitor.Bounds.Right - w - 4);
        int top = (int)Math.Round(anchorPx.Y - h + Pad * scale);
        double pointerX = (anchorPx.X - left) / scale - Pad;
        _pointer.Margin = new Thickness(Math.Clamp(pointerX - 12, 10, panelW - 34), -1, 0, 0);
        _root.RenderTransformOrigin = new Point(Math.Clamp(pointerX / panelW, 0, 1), 1);

        _closing = false;
        if (!IsVisible) Show();
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, w, h, SWP_SHOWWINDOW);
        DockWindow.ForceForeground(_hwnd);
        Activate();
        Focus();
        Animate(open: true);
    }

    private void Navigate(string path, bool remember = true)
    {
        if (remember && _path.Length > 0) _history.Push(_path);
        _path = path;
        _back.Visibility = _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
        _title.MaxWidth = _wrap.Width - 180;

        var (entries, total) = StackFolder.Enumerate(path, Stack?.Config.Sort ?? StackSort.Name, (int)MaxItems);
        LazyIcon.NewBatch();
        _wrap.Children.Clear();
        foreach (var entry in entries) _wrap.Children.Add(Tile(entry));
        _scroll.ScrollToTop();
        _footerText.Text = total > entries.Count ? $"{total - entries.Count} More in File Explorer  ➜" : "Open in File Explorer  ➜";
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        Navigate(_history.Pop(), remember: false);
    }

    private FrameworkElement Tile(StackEntry entry)
    {
        var image = new Image { Width = 64, Height = 64, Margin = new Thickness(0, 10, 0, 4), Source = IconLoader.Placeholder() };
        LazyIcon.Load(entry, img => image.Source = img);
        var label = new TextBlock
        {
            Text = entry.Name, Foreground = Brushes.White, FontSize = 11.5, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30,
            Margin = new Thickness(6, 0, 6, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(image);
        stack.Children.Add(label);
        var tile = new Border
        {
            Width = TileW - 4, Height = TileH - 4, Margin = new Thickness(2), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Child = stack, ToolTip = entry.Name
        };
        tile.MouseEnter += (_, _) => tile.Background = HoverBrush;
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (entry.IsFolder) Navigate(entry.Path); // step into folders, like 10.6
            else { DockWindow.ShellOpen(entry.Path); CloseGrid(); }
        };
        return tile;
    }

    private static Border Pill(string text, Action onClick)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 3, 12, 4),
            Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5 }, Cursor = Cursors.Hand
        };
        pill.MouseEnter += (_, _) => pill.Background = new SolidColorBrush(Color.FromArgb(95, 255, 255, 255));
        pill.MouseLeave += (_, _) => pill.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return pill;
    }

    public void CloseGrid(bool returnFocus = false)
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        _returnFocus = returnFocus;
        Animate(open: false);
    }

    private void Animate(bool open)
    {
        var dur = TimeSpan.FromSeconds(open ? 0.17 : 0.12);
        var ease = new QuadraticEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        var scale = new DoubleAnimation(open ? 0.86 : 1, open ? 1 : 0.86, dur) { EasingFunction = ease };
        var fade = new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, dur) { EasingFunction = ease };
        if (!open)
            fade.Completed += (_, _) =>
            {
                Hide();
                LazyIcon.NewBatch();
                _closing = false;
                GridClosed?.Invoke(_returnFocus);
            };
        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        _root.BeginAnimation(OpacityProperty, fade);
    }
}
