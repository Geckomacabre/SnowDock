using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SnowDock.Native;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

/// <summary>
/// The dock: a transparent, topmost, never-activating window across the bottom of the primary monitor.
/// </summary>
internal sealed partial class DockWindow : Window
{
    private readonly DockConfig _config;
    private readonly bool _preview;
    private readonly DockSurface _surface = new();
    private readonly WindowTracker _tracker = new();
    private readonly TaskbarController _taskbar = new();
    private readonly DispatcherTimer _housekeeping;
    private AppBar? _appBar;
    private IntPtr _hwnd;
    private uint _taskbarCreatedMsg;
    private bool _animating;
    private DateTime _lastFrame;
    private bool _hiddenForFullScreen;
    private bool _cleanedUp;
    private int _tick;

    public DockWindow(DockConfig config, bool preview)
    {
        _config = config;
        _preview = preview;

        Title = "SnowDock";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0; Width = 200; Height = 100; // real bounds are set in physical pixels once the HWND exists

        _surface.IconSize = config.IconSize;
        _surface.MagnifiedSize = Math.Max(config.IconSize, config.MagnifiedSize);
        _surface.MagnificationEnabled = config.Magnification;
        _surface.Edge = config.Edge;
        _surface.Look = Styles.DockStyles.Find(config.Style);
        if (config.Clock24h is bool h24) Widgets.WidgetEnv.Clock24h = h24;
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.HighQuality);
        Content = _surface;

        _surface.MouseMove += OnMouseMoveSurface;
        _surface.MouseLeave += OnMouseLeaveSurface;
        _surface.MouseLeftButtonDown += OnLeftDown;
        _surface.MouseLeftButtonUp += OnLeftUp;
        _surface.MouseRightButtonUp += OnRightUp;
        SetupDrop();

        _tracker.Changed += RebuildItems;
        _tracker.AttentionRequested += OnAttentionRequested;

        _housekeeping = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _housekeeping.Tick += OnHousekeeping;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            _tracker.Start();
            RebuildItems();
            _housekeeping.Start();
            StartAutoHide();
            StartWidgetTimer();
            // Create the drag ghost's window now (off-screen), not in the middle of the first drag.
            _ghost = new DragGhost(Math.Max(_surface.MagnifiedSize, _surface.IconSize * 3.4) + 24); // room for wide widget tiles
            _ghost.Show();
            _ghost.Hide();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Never take focus (so clicking the dock doesn't steal it from the app being activated)
        // and stay out of Alt-Tab.
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));

        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        _tracker.AddOwnWindow(_hwnd);
        RegisterShellHookWindow(_hwnd);
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

        PositionWindow();

        if (!_preview && _config.HideTaskbar) _taskbar.Hide();
        UpdateAppBar();
        PositionWindow();
        AppPaths.Log($"Dock started (preview={_preview}, hideTaskbar={_config.HideTaskbar})");
    }

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleY;
    private int ToPx(double dip) => (int)Math.Ceiling(dip * DpiScale);

    /// <summary>The monitor the dock lives on (chosen by device name; falls back to the primary).</summary>
    private MonitorDesc TargetMonitor => Monitors.Find(_config.Monitor);

    /// <summary>Reserve screen space only in real mode, when asked to, and not while auto-hiding.</summary>
    private void UpdateAppBar()
    {
        bool want = !_preview && _config.ReserveScreenSpace && !_config.AutoHide;
        if (!want)
        {
            _appBar?.Unregister();
            return;
        }
        if (_appBar == null)
        {
            _appBar = new AppBar(_hwnd) { Monitor = () => TargetMonitor };
            _appBar.FullScreenChanged += SetFullScreenMode;
            _appBar.PositionChanged += PositionWindow;
        }
        _appBar.Edge = _config.Edge;
        _appBar.Register(ToPx(_surface.ReservedHeight));
    }

    private void PositionWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        var mon = TargetMonitor;
        int depthPx = ToPx(_surface.RequiredHeight);
        // Sit on whatever Windows gave the appbar (inside the taskbar if it's still visible). Without an appbar:
        // the screen edge if the taskbar is hidden, otherwise the work area's edge.
        bool appBar = !_preview && _appBar?.IsRegistered == true;
        var area = _preview || (!appBar && !_config.HideTaskbar) ? mon.Work : mon.Bounds;
        switch (_config.Edge)
        {
            case DockEdge.Left:
            {
                int left = appBar ? _appBar!.ReservedRect.Left : area.Left;
                SetWindowPos(_hwnd, HWND_TOPMOST, left, area.Top, depthPx, area.Height, SWP_NOACTIVATE);
                break;
            }
            case DockEdge.Right:
            {
                int right = appBar ? _appBar!.ReservedRect.Right : area.Right;
                SetWindowPos(_hwnd, HWND_TOPMOST, right - depthPx, area.Top, depthPx, area.Height, SWP_NOACTIVATE);
                break;
            }
            case DockEdge.Top:
            {
                int top = appBar ? _appBar!.ReservedRect.Top : area.Top;
                SetWindowPos(_hwnd, HWND_TOPMOST, mon.Bounds.Left, top, mon.Bounds.Width, depthPx, SWP_NOACTIVATE);
                break;
            }
            default:
            {
                int bottom = appBar ? _appBar!.ReservedRect.Bottom : area.Bottom;
                SetWindowPos(_hwnd, HWND_TOPMOST, mon.Bounds.Left, bottom - depthPx, mon.Bounds.Width, depthPx, SWP_NOACTIVATE);
                break;
            }
        }
    }

    /// <summary>Move the dock to another screen edge.</summary>
    private void SetEdge(DockEdge edge)
    {
        if (_config.Edge == edge) return;
        _fan?.CloseFan();
        _grid?.CloseGrid();
        _config.Edge = edge;
        _config.Save();
        _surface.Edge = edge;
        _appBar?.Unregister(); // release the old edge before claiming the new one
        UpdateAppBar();
        PositionWindow();
        EnsureAnimating();
    }

    private Stacks.GridWindow? _grid;
    private Expose.ExposeWindow? _expose;

    /// <summary>True while any SnowDock popup (menu, fan, grid, Exposé) is up: no auto-hide, no topmost re-assert.</summary>
    private bool PopupOpen() =>
        _menuOpen || _fan?.IsVisible == true || _grid?.IsVisible == true || _expose?.IsVisible == true;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        if (msg == (int)_tracker.ShellHookMessage)
        {
            _tracker.HandleShellMessage(wParam, lParam);
        }
        else if (_appBar != null && msg == (int)_appBar.CallbackMessage)
        {
            handled = _appBar.HandleCallback(wParam, lParam);
        }
        else if (msg == (int)_taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            // Explorer restarted: it brings a fresh, visible taskbar and forgets our appbar.
            AppPaths.Log("TaskbarCreated: re-applying");
            Dispatcher.BeginInvoke(() =>
            {
                _taskbar.Reapply();
                _appBar?.Unregister();
                UpdateAppBar();
                PositionWindow();
            });
        }
        else if (msg == WM_DISPLAYCHANGE || msg == 0x02E0 /* WM_DPICHANGED */)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateAppBar();
                PositionWindow();
            });
        }
        return IntPtr.Zero;
    }

    // ---- animation loop: runs only while something moves ----

    private void EnsureAnimating()
    {
        _surface.InvalidateVisual();
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

        DragTick();
        bool more = _surface.Tick(dt);
        _surface.InvalidateVisual();
        if (!more)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    // ---- mouse ----

    private void OnMouseMoveSurface(object sender, MouseEventArgs e)
    {
        if (IsDragging) return;
        var p = _surface.ToDock(e.GetPosition(_surface));
        if (!_surface.IsHovering && !_surface.IsOverDock(p)) return;
        _surface.IsHovering = true;
        _surface.LastMouseX = p.X;
        _surface.Hovered = _surface.HitItem(p);
        EnsureAnimating();
    }

    private void OnMouseLeaveSurface(object sender, MouseEventArgs e)
    {
        if (_menuOpen || IsDragging) return;
        _surface.IsHovering = false;
        _surface.Hovered = null;
        _surface.Pressed = null;
        EnsureAnimating();
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        var p = _surface.ToDock(e.GetPosition(_surface));
        _surface.Pressed = _surface.HitItem(p);
        if (_surface.Pressed != null) BeginPress(_surface.Pressed);
        else if (_surface.IsOverDock(p)) BeginDockMove(); // an empty spot: drag the whole dock to another edge
        _surface.InvalidateVisual();
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (IsDragging)
        {
            EndDrag(cancel: false);
            return;
        }
        ClearPress();
        var p = _surface.ToDock(e.GetPosition(_surface));
        var item = _surface.HitItem(p);
        var pressed = _surface.Pressed;
        _surface.Pressed = null;
        _surface.InvalidateVisual();
        if (item == null || !ReferenceEquals(item, pressed)) return;
        if (item.Kind == DockItemKind.Widget) OnWidgetClicked(item, p);
        else OnItemClicked(item);
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        if (IsDragging) return;
        var p = _surface.ToDock(e.GetPosition(_surface));
        var item = _surface.HitItem(p);
        if (item != null) ShowItemMenu(item);
        else if (_surface.IsOverDock(p)) ShowDockMenu(p);
        e.Handled = true;
    }

    // ---- full-screen apps, topmost, recycle bin ----

    private void OnHousekeeping(object? sender, EventArgs e)
    {
        _tick++;
        // ABN_FULLSCREENAPP only reaches registered appbars; poll as well so preview mode and games work.
        SetFullScreenMode(IsForegroundFullScreen());
        // Stay on top of other windows, but never climb over an open fan: its bottom rows overlap the dock's
        // hover area, and clicks there must reach the fan.
        if (!_hiddenForFullScreen && !PopupOpen())
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        if (_tick % 3 == 0) RefreshRecycleBin();
    }

    private bool IsForegroundFullScreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return false;
        // Our own windows (the Exposé overlay is full-screen) never count.
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;
        // Exactly the monitor: maximized windows overhang it by their invisible borders, so they don't count.
        var mon = TargetMonitor.Bounds;
        return r.Left == mon.Left && r.Top == mon.Top && r.Right == mon.Right && r.Bottom == mon.Bottom;
    }

    private void SetFullScreenMode(bool fullScreen)
    {
        if (fullScreen == _hiddenForFullScreen) return;
        _hiddenForFullScreen = fullScreen;
        ShowWindow(_hwnd, fullScreen ? SW_HIDE : SW_SHOWNA);
    }

    /// <summary>Restore the taskbar and release the screen edge. Safe to call more than once.</summary>
    public void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        try { _housekeeping.Stop(); _tracker.Stop(); } catch { }
        try { if (_hwnd != IntPtr.Zero) DeregisterShellHookWindow(_hwnd); } catch { }
        try { _appBar?.Dispose(); } catch { }
        try { DisposeStacks(); } catch { }
        try { if (_taskbar.IsHidden) _taskbar.Show(); } catch { }
        AppPaths.Log("Dock cleaned up");
    }
}
