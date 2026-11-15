using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnowDock.Native;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Expose;

/// <summary>
/// Dock Exposé: one app's windows spread out over a dimmed desktop as live thumbnails. Click one to bring it
/// forward; click elsewhere or press Esc to go back.
///
/// The window is deliberately not layered (DWM thumbnails don't draw into layered windows), so the
/// "transparent" dimmed desktop is a screenshot taken just before it opens.
/// </summary>
internal sealed class ExposeWindow : Window
{
    private const double AnimSeconds = 0.28;
    private readonly ExposeSurface _surface = new();
    private readonly IReadOnlyList<IntPtr> _windows;
    private readonly MonitorDesc _monitor;
    private readonly double _reserveBottomPx;
    private IntPtr _hwnd;
    private double _t;
    private bool _closing, _closed, _sawDown, _animating;
    private IntPtr _choice;
    private DateTime _lastFrame;
    /// <summary>Who had focus before Exposé; gets it back if nothing is picked.</summary>
    private readonly IntPtr _previousForeground = GetForegroundWindow();

    /// <summary>Log of thumbnail registration results, for diagnostics.</summary>
    public List<string> Diagnostics { get; } = new();

    public ExposeWindow(IReadOnlyList<IntPtr> windows, BitmapSource? appIcon, MonitorDesc monitor, double dpiScale, double reserveBottomPx)
    {
        _windows = windows;
        _monitor = monitor;
        _reserveBottomPx = reserveBottomPx;
        _surface.AppIcon = appIcon;
        _surface.Scale = dpiScale;
        _surface.Backdrop = Dwm.CaptureScreen(monitor.Bounds); // before we cover the screen

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Bounds.Left / dpiScale; Top = monitor.Bounds.Top / dpiScale;
        Width = monitor.Bounds.Width / dpiScale; Height = monitor.Bounds.Height / dpiScale;
        Content = _surface;

        SourceInitialized += OnSourceInitialized;
        ContentRendered += (_, _) => { DockWindow.ForceForeground(_hwnd); Focus(); };
        Deactivated += (_, _) => Dismiss(IntPtr.Zero);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Dismiss(IntPtr.Zero); };
        _surface.MouseLeftButtonDown += (_, _) => _sawDown = true;
        _surface.MouseLeftButtonUp += OnLeftUp;
        _surface.MouseRightButtonUp += (_, _) => Dismiss(IntPtr.Zero);
        _surface.MouseMove += (_, e) =>
        {
            int hover = HitTile(e.GetPosition(_surface));
            if (hover == _surface.Hover) return;
            _surface.Hover = hover;
            _surface.InvalidateVisual();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        var b = _monitor.Bounds;
        SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, SWP_SHOWWINDOW);

        var sizes = new List<Size>();
        foreach (var w in _windows)
        {
            int hr = Dwm.DwmRegisterThumbnail(_hwnd, w, out var thumb);
            var source = Dwm.VisibleSourceRect(w);
            Size size;
            if (source is RECT s) size = new Size(s.Width, s.Height);
            else if (hr == 0 && Dwm.DwmQueryThumbnailSourceSize(thumb, out var qs) == 0 && qs.cx > 0) size = new Size(qs.cx, qs.cy);
            else size = new Size(1280, 800);
            Diagnostics.Add($"hwnd=0x{w.ToInt64():X} register=0x{hr:X8} size={size.Width}x{size.Height} minimized={IsIconic(w)}");

            var vis = Dwm.VisibleBounds(w);
            _surface.Tiles.Add(new ExposeTile
            {
                Hwnd = w,
                Thumb = hr == 0 ? thumb : IntPtr.Zero,
                Source = source,
                Title = WindowTracker.GetTitle(w),
                Start = IsIconic(w) ? Rect.Empty
                    : new Rect(vis.Left - b.Left, vis.Top - b.Top, vis.Width, vis.Height)
            });
            sizes.Add(size);
        }

        // Leave the top and the dock's strip clear, like the Mac.
        var area = new Rect(60, 60, b.Width - 120, b.Height - 60 - _reserveBottomPx - 40);
        var targets = ExposeLayout.Arrange(sizes, area);
        for (int i = 0; i < _surface.Tiles.Count; i++)
        {
            var tile = _surface.Tiles[i];
            tile.Target = targets[i];
            // Minimized windows grow out of their own spot instead of flying in from nowhere.
            if (tile.Start.IsEmpty)
                tile.Start = new Rect(targets[i].X + targets[i].Width * 0.4, targets[i].Y + targets[i].Height * 0.4,
                    targets[i].Width * 0.2, targets[i].Height * 0.2);
        }
        AppPaths.Log("Exposé: " + string.Join("; ", Diagnostics));
        Animate();
    }

    private int HitTile(Point dip)
    {
        var px = new Point(dip.X * _surface.Scale, dip.Y * _surface.Scale);
        for (int i = 0; i < _surface.Tiles.Count; i++)
            if (_surface.Tiles[i].Target.Contains(px)) return i;
        return -1;
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        // The release of the click-and-hold that opened us lands here with no press: ignore it.
        if (!_sawDown) return;
        int i = HitTile(e.GetPosition(_surface));
        Dismiss(i >= 0 ? _surface.Tiles[i].Hwnd : IntPtr.Zero);
    }

    /// <summary>Animate back and close; then bring <paramref name="choice"/> forward if one was picked.</summary>
    public void Dismiss(IntPtr choice)
    {
        if (_closing) return;
        _closing = true;
        _choice = choice;
        Animate();
    }

    private void Animate()
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
        _t = Math.Clamp(_t + (_closing ? -dt * 1.4 : dt) / AnimSeconds, 0, 1);
        double ease = 1 - Math.Pow(1 - _t, 3);

        foreach (var tile in _surface.Tiles)
        {
            tile.Current = Lerp(tile.Start, tile.Target, ease);
            if (tile.Thumb != IntPtr.Zero)
                Dwm.Show(tile.Thumb, ToRect(tile.Current), tile.Source);
        }
        _surface.Progress = ease;
        _surface.InvalidateVisual();

        if (_closing && _t <= 0) Finish();
        else if (!_closing && _t >= 1) { _animating = false; CompositionTarget.Rendering -= OnFrame; }
    }

    private void Finish()
    {
        if (_closed) return;
        _closed = true;
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
        foreach (var tile in _surface.Tiles)
            if (tile.Thumb != IntPtr.Zero) Dwm.DwmUnregisterThumbnail(tile.Thumb);
        Close();
        var target = _choice != IntPtr.Zero ? _choice : _previousForeground;
        if (target != IntPtr.Zero && IsWindow(target)) DockWindow.ForceForeground(target);
    }

    private static Rect Lerp(Rect a, Rect b, double t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
        Math.Max(1, a.Width + (b.Width - a.Width) * t), Math.Max(1, a.Height + (b.Height - a.Height) * t));

    private static RECT ToRect(Rect r) => new()
    {
        Left = (int)Math.Round(r.Left), Top = (int)Math.Round(r.Top),
        Right = (int)Math.Round(r.Right), Bottom = (int)Math.Round(r.Bottom)
    };
}
