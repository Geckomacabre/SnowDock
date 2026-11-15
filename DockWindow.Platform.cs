using System.Diagnostics;
using System.Windows.Threading;
using SnowDock.Native;
using SnowDock.Widgets;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

/// <summary>Moving the dock by dragging it, size presets, the clock format, and Start-menu awareness.</summary>
internal sealed partial class DockWindow
{
    // ---- drag an empty spot of the dock to another edge (or monitor) ----

    private DispatcherTimer? _moveTimer;
    private POINT _moveStart;

    private void BeginDockMove()
    {
        GetCursorPos(out _moveStart);
        _moveTimer ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _moveTimer.Tick -= OnMoveTick;
        _moveTimer.Tick += OnMoveTick;
        _moveTimer.Start();
    }

    private void OnMoveTick(object? sender, EventArgs e)
    {
        if (PrimaryButtonDown()) return; // still dragging
        _moveTimer!.Stop();
        GetCursorPos(out var c);
        double moved = Math.Sqrt(Math.Pow(c.X - _moveStart.X, 2) + Math.Pow(c.Y - _moveStart.Y, 2));
        if (moved < 40 * DpiScale) return; // a click, not a drag

        // Released on another monitor? Move there first.
        var mon = Monitors.All().FirstOrDefault(m => c.X >= m.Bounds.Left && c.X < m.Bounds.Right && c.Y >= m.Bounds.Top && c.Y < m.Bounds.Bottom);
        if (mon != null && !string.Equals(mon.DeviceName, TargetMonitor.DeviceName, StringComparison.OrdinalIgnoreCase))
            MoveToMonitor(mon.DeviceName);
        var b = (mon ?? TargetMonitor).Bounds;
        double fx = (c.X - b.Left) / (double)b.Width, fy = (c.Y - b.Top) / (double)b.Height;
        // Near the left or right edge: dock there. Otherwise the top or bottom half decides.
        var edge = fx < 0.12 ? DockEdge.Left : fx > 0.88 ? DockEdge.Right : fy < 0.5 ? DockEdge.Top : DockEdge.Bottom;
        AppPaths.Log($"Dock dragged to {edge}");
        SetEdge(edge);
    }

    // ---- size and clock ----

    /// <summary>Small, medium or large: icon size, with magnification scaled to match.</summary>
    private void SetIconSize(double icon)
    {
        _config.IconSize = icon;
        _config.MagnifiedSize = Math.Round(icon * 1.85);
        _config.Save();
        _surface.IconSize = icon;
        _surface.MagnifiedSize = _config.MagnifiedSize;
        UpdateAppBar();
        PositionWindow();
        EnsureAnimating();
    }

    private void SetClock24h(bool on)
    {
        _config.Clock24h = on;
        _config.Save();
        WidgetEnv.Clock24h = on;
        OnWidgetTick(null, EventArgs.Empty);
        _surface.InvalidateVisual();
    }

    // ---- Start menu ----

    private readonly Dictionary<uint, string> _processNames = new();

    /// <summary>True while Start or Windows Search is open (so an auto-hidden dock comes up with it).</summary>
    private bool StartIsOpen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out uint pid);
        if (!_processNames.TryGetValue(pid, out var name))
        {
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
            catch { name = ""; }
            if (_processNames.Count > 64) _processNames.Clear();
            _processNames[pid] = name;
        }
        return name is "StartMenuExperienceHost" or "SearchHost" or "SearchApp";
    }
}
