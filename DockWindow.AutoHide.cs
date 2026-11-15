using System.Windows;
using System.Windows.Threading;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

/// <summary>
/// Auto-hide, as on the Mac: the dock slides off the screen edge, comes back when the mouse touches that
/// edge, and slides away again shortly after the mouse leaves it. While hiding, no screen space is reserved.
/// </summary>
internal sealed partial class DockWindow
{
    private DispatcherTimer? _autoHideTimer;
    private DateTime _mouseLeftDockAt = DateTime.MaxValue;
    private const double HideDelaySeconds = 0.45;

    private void StartAutoHide()
    {
        _autoHideTimer ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(60) };
        _autoHideTimer.Tick -= OnAutoHideTick;
        _autoHideTimer.Tick += OnAutoHideTick;
        if (_config.AutoHide) _autoHideTimer.Start();
        else
        {
            _autoHideTimer.Stop();
            _surface.HideTarget = 0;
            EnsureAnimating();
        }
    }

    private void SetAutoHide(bool on)
    {
        _config.AutoHide = on;
        _config.Save();
        UpdateAppBar(); // auto-hide gives the screen edge back to windows
        PositionWindow();
        _mouseLeftDockAt = DateTime.UtcNow;
        StartAutoHide();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        if (!_config.AutoHide) return;
        GetCursorPos(out var c);
        var mon = TargetMonitor.Bounds;
        bool hidden = _surface.HideTarget >= 1;

        if (hidden)
        {
            // Reveal when the mouse presses against the dock's edge of this monitor.
            if (AtDockEdge(c, mon)) Reveal();
            return;
        }

        // Shown: stay while the mouse is on the dock or anything of ours is open; hide shortly after it leaves.
        var local = _surface.DockFromScreen(new Point(c.X, c.Y));
        bool overDock = _surface.IsOverDock(local) || AtDockEdge(c, mon);
        if (overDock || PopupOpen() || IsDragging || _surface.DropTarget != null || _drag != null)
        {
            _mouseLeftDockAt = DateTime.MaxValue;
            return;
        }
        if (_mouseLeftDockAt == DateTime.MaxValue) _mouseLeftDockAt = DateTime.UtcNow;
        if ((DateTime.UtcNow - _mouseLeftDockAt).TotalSeconds >= HideDelaySeconds)
        {
            _surface.HideTarget = 1;
            _surface.IsHovering = false;
            _surface.Hovered = null;
            EnsureAnimating();
        }
    }

    private bool AtDockEdge(POINT c, RECT mon)
    {
        if (c.X < mon.Left || c.X >= mon.Right || c.Y < mon.Top || c.Y >= mon.Bottom) return false;
        return _config.Edge switch
        {
            DockEdge.Left => c.X <= mon.Left + 1,
            DockEdge.Right => c.X >= mon.Right - 2,
            _ => c.Y >= mon.Bottom - 2
        };
    }

    private void Reveal()
    {
        _surface.HideTarget = 0;
        _mouseLeftDockAt = DateTime.MaxValue;
        EnsureAnimating();
    }
}
