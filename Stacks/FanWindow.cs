using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Stacks;

/// <summary>
/// The window a fan is drawn in. Unlike the dock it does take focus, so a click anywhere else
/// (or Esc) closes it, the way a stack closes on the Mac.
/// </summary>
internal sealed class FanWindow : Window
{
    private readonly FanSurface _surface = new();
    private IntPtr _hwnd;
    private bool _closing;
    private bool _returnFocus;
    private bool _animating;
    private DateTime _lastFrame;

    public StackFolder? Stack { get; private set; }
    public bool IsOpen => IsVisible && !_closing;
    public IntPtr Handle => _hwnd;
    /// <summary>
    /// Raised once the closing animation has finished. True when focus should go back to the app that had it
    /// (closed with Esc or by clicking the stack again, rather than by clicking something else).
    /// </summary>
    public event Action<bool>? FanClosed;

    public FanWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; Width = 10; Height = 10;
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.HighQuality);
        Content = _surface;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        };

        Deactivated += (_, _) => CloseFan();
        KeyDown += OnKey;
        _surface.MouseMove += (_, e) => SetHover(_surface.RowAt(e.GetPosition(_surface)));
        _surface.MouseLeave += (_, _) => SetHover(-1);
        _surface.MouseLeftButtonUp += (_, e) => OpenRow(_surface.RowAt(e.GetPosition(_surface)));
    }

    /// <summary>
    /// Fan out <paramref name="stack"/> from <paramref name="basePx"/>: the stack icon's center X and top Y,
    /// in physical screen pixels.
    /// </summary>
    public void Open(StackFolder stack, int maxItems, double tileSize, Point basePx, double dpiScale, double screenTopPx)
    {
        Stack = stack;
        // Fit under the top of the screen: rows are TileSize + 12 tall, plus the top button and label headroom.
        double available = (basePx.Y - screenTopPx) / dpiScale - FanSurface.LabelMaxWidth * 0.3 - tileSize;
        int fit = Math.Max(1, (int)(available / (tileSize + 12)) - 1);
        int count = Math.Min(Math.Min(maxItems, fit), stack.Entries.Count);

        _surface.Entries = stack.Entries.Take(count).ToList();
        _surface.MoreCount = Math.Max(0, stack.TotalCount - count);
        _surface.TileSize = tileSize;
        _surface.HoverIndex = -1;
        _surface.AnimT = 0;
        // Extract thumbnails now (videos can be slow) so the animation doesn't stall halfway.
        foreach (var entry in _surface.Entries) _ = entry.Image;

        var size = FanSurface.RequiredSize(_surface.Rows, tileSize);
        int w = (int)Math.Ceiling(size.Width * dpiScale), h = (int)Math.Ceiling(size.Height * dpiScale);
        int left = (int)Math.Round(basePx.X - _surface.OriginX * dpiScale);
        int top = (int)Math.Round(basePx.Y - h);

        _closing = false;
        if (!IsVisible) Show();
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, w, h, SWP_SHOWWINDOW);
        // We just received the click on the dock, so Windows lets us take the foreground. If activation
        // failed, Deactivated would never fire and clicking elsewhere couldn't close the fan.
        DockWindow.ForceForeground(_hwnd);
        Activate();
        Focus();
        StartAnimating();
    }

    public void CloseFan(bool returnFocus = false)
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        _returnFocus = returnFocus;
        StartAnimating();
    }

    /// <summary>Snapshot support: jump straight to the fully open state and draw.</summary>
    public FanSurface Surface => _surface;

    private void SetHover(int row)
    {
        if (row == _surface.HoverIndex) return;
        _surface.HoverIndex = row;
        _surface.InvalidateVisual();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: CloseFan(returnFocus: true); break;
            case Key.Up: SetHover(Math.Min(_surface.Rows - 1, _surface.HoverIndex + 1)); break;
            case Key.Down: SetHover(Math.Max(0, _surface.HoverIndex - 1)); break;
            case Key.Enter: OpenRow(_surface.HoverIndex); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Open the item in <paramref name="row"/> (the top row opens the folder itself).</summary>
    private void OpenRow(int row)
    {
        if (row < 0 || Stack == null) return;
        string path = row < _surface.Entries.Count ? _surface.Entries[row].Path : Stack.Config.Path;
        DockWindow.ShellOpen(path);
        CloseFan();
    }

    private void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        bool done;
        if (_closing)
        {
            _surface.AnimT = Math.Max(0, Math.Min(_surface.AnimT, _surface.OpenDuration) - dt * 1.7);
            done = _surface.AnimT <= 0;
        }
        else
        {
            _surface.AnimT += dt;
            done = _surface.AnimT >= _surface.OpenDuration;
        }
        _surface.InvalidateVisual();
        if (!done) return;

        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
        if (_closing)
        {
            Hide();
            _closing = false;
            FanClosed?.Invoke(_returnFocus);
        }
    }
}
