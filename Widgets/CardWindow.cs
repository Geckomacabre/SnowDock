using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SnowDock.Native;
using static SnowDock.Native.NativeMethods;
using Shape = System.Windows.Shapes;

namespace SnowDock.Widgets;

/// <summary>
/// A widget's card: a rounded panel with a title and the widget's own controls, pointing at its tile.
/// Like a stack's grid it takes focus, and closes on Esc or a click elsewhere.
/// </summary>
internal sealed class CardWindow : Window
{
    private const double Pad = 14, PointerH = 12;
    private readonly Grid _root = new();
    private readonly Border _panel = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 14) };
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
    private readonly ContentControl _content = new();
    private readonly Shape.Path _pointer = new() { Data = Geometry.Parse("M0,0 L24,0 L12,12 Z") };
    private readonly ScaleTransform _zoom = new(1, 1);
    private IntPtr _hwnd;
    private bool _closing;

    public DockWidget? Widget { get; private set; }
    public bool IsOpen => IsVisible && !_closing;
    public IntPtr Handle => _hwnd;
    public event Action? CardClosed;

    public CardWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000;
        FontFamily = WidgetPaint.Font;

        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(_content);
        _panel.Child = stack;
        _panel.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45 };
        _root.Margin = new Thickness(Pad);
        _root.RenderTransform = _zoom;
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        };
        Deactivated += (_, _) => CloseCard();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseCard(); e.Handled = true; } };
    }

    /// <summary>
    /// Show <paramref name="widget"/>'s card next to <paramref name="anchorPx"/> (the tile's centre and near
    /// edge, physical pixels). <paramref name="above"/>: open upward (bottom dock) or downward (top dock).
    /// </summary>
    public void Open(DockWidget widget, WidgetPaint paint, Point anchorPx, bool above, double scale, MonitorDesc monitor)
    {
        Widget = widget;
        _title.Text = widget.Title;
        _content.Content = widget.BuildCard(paint);
        _panel.Background = paint.Light ? new SolidColorBrush(Color.FromArgb(245, 248, 248, 250)) : new SolidColorBrush(Color.FromArgb(240, 34, 34, 38));
        _panel.BorderBrush = paint.TileRim.Brush;
        _panel.BorderThickness = new Thickness(1);
        _pointer.Fill = _panel.Background;
        TextElement.SetForeground(_panel, paint.Ink);
        _title.Foreground = paint.Ink;

        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = above ? GridLength.Auto : new GridLength(PointerH) });
        _root.RowDefinitions.Add(new RowDefinition { Height = above ? new GridLength(PointerH) : GridLength.Auto });
        Grid.SetRow(_panel, above ? 0 : 1);
        Grid.SetRow(_pointer, above ? 1 : 0);
        _pointer.RenderTransform = above ? Transform.Identity : new ScaleTransform(1, -1, 12, 6);
        _pointer.HorizontalAlignment = HorizontalAlignment.Left;
        _root.Children.Add(_panel);
        _root.Children.Add(_pointer);

        _closing = false;
        if (!IsVisible) Show();
        // Measure, then place so the pointer tip touches the tile.
        _root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _root.DesiredSize;
        int w = (int)Math.Ceiling((size.Width + Pad * 2) * scale), h = (int)Math.Ceiling((size.Height + Pad * 2) * scale);
        int left = (int)Math.Clamp(anchorPx.X - w / 2.0, monitor.Bounds.Left + 4, monitor.Bounds.Right - w - 4);
        int top = above ? (int)Math.Round(anchorPx.Y - h + Pad * scale) : (int)Math.Round(anchorPx.Y - Pad * scale);
        double pointerX = (anchorPx.X - left) / scale - Pad;
        _pointer.Margin = new Thickness(Math.Clamp(pointerX - 12, 12, Math.Max(12, size.Width - 36)), above ? -1 : 0, 0, above ? 0 : -1);
        _root.RenderTransformOrigin = new Point(Math.Clamp(pointerX / Math.Max(1, size.Width), 0, 1), above ? 1 : 0);
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, w, h, SWP_SHOWWINDOW);
        DockWindow.ForceForeground(_hwnd);
        Activate();
        Animate(open: true);
    }

    public void CloseCard()
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        Animate(open: false);
    }

    private void Animate(bool open)
    {
        var dur = TimeSpan.FromSeconds(open ? 0.15 : 0.1);
        var scale = new DoubleAnimation(open ? 0.9 : 1, open ? 1 : 0.9, dur);
        var fade = new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, dur);
        if (!open)
            fade.Completed += (_, _) =>
            {
                Hide();
                _closing = false;
                _content.Content = null;
                CardClosed?.Invoke();
            };
        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        _root.BeginAnimation(OpacityProperty, fade);
    }
}
