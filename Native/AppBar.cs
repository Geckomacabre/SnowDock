using System.Runtime.InteropServices;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

/// <summary>
/// Registers the dock as an appbar so maximized windows stop short of the shelf, the way zoomed windows
/// stop short of the Mac dock. Only the shelf strip is reserved; the transparent area beyond it
/// (magnification, labels) overlaps windows. All values here are physical pixels.
/// </summary>
internal sealed class AppBar : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private bool _registered;
    private int _thicknessPx;

    public event Action<bool>? FullScreenChanged;
    public event Action? PositionChanged;

    public uint CallbackMessage => _callbackMessage;
    public bool IsRegistered => _registered;
    /// <summary>The strip Windows actually gave us (it sits inside the taskbar if the taskbar is visible).</summary>
    public RECT ReservedRect { get; private set; }

    /// <summary>The monitor to sit on; re-read every time the position is set.</summary>
    public required Func<MonitorDesc> Monitor { get; init; }
    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    public AppBar(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _callbackMessage = RegisterWindowMessage("SnowDock.AppBarMessage");
    }

    public void Register(int thicknessPx)
    {
        _thicknessPx = thicknessPx;
        if (!_registered)
        {
            var abd = NewData();
            abd.uCallbackMessage = _callbackMessage;
            _registered = SHAppBarMessage(ABM_NEW, ref abd) != 0;
            AppPaths.Log($"AppBar ABM_NEW registered={_registered}");
        }
        SetPosition();
    }

    public void Unregister()
    {
        if (!_registered) return;
        var abd = NewData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }

    public void SetPosition()
    {
        if (!_registered) return;
        var mon = Monitor().Bounds;
        var abd = NewData();
        abd.uEdge = Edge switch { DockEdge.Left => ABE_LEFT, DockEdge.Right => ABE_RIGHT, _ => ABE_BOTTOM };
        abd.rc = mon;
        switch (Edge)
        {
            case DockEdge.Left: abd.rc.Right = mon.Left + _thicknessPx; break;
            case DockEdge.Right: abd.rc.Left = mon.Right - _thicknessPx; break;
            default: abd.rc.Top = mon.Bottom - _thicknessPx; break;
        }
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        // QUERYPOS may move our edge inward if something else (a visible taskbar) holds it; keep our thickness.
        switch (Edge)
        {
            case DockEdge.Left: abd.rc.Right = abd.rc.Left + _thicknessPx; break;
            case DockEdge.Right: abd.rc.Left = abd.rc.Right - _thicknessPx; break;
            default: abd.rc.Top = abd.rc.Bottom - _thicknessPx; break;
        }
        SHAppBarMessage(ABM_SETPOS, ref abd);
        ReservedRect = abd.rc;
    }

    /// <summary>Handle the appbar callback message. Returns true if handled.</summary>
    public bool HandleCallback(IntPtr wParam, IntPtr lParam)
    {
        switch ((int)wParam)
        {
            case ABN_POSCHANGED:
                SetPosition();
                PositionChanged?.Invoke();
                return true;
            case ABN_FULLSCREENAPP:
                FullScreenChanged?.Invoke(lParam != IntPtr.Zero);
                return true;
        }
        return false;
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    public void Dispose() => Unregister();
}
