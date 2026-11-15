using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

/// <summary>
/// Hides and restores the Windows taskbar (primary and secondary monitors).
///
/// Hiding a taskbar window does not release the screen strip it reserves, so the taskbar is also
/// switched to auto-hide while the dock runs. The user's original auto-hide setting is written to
/// disk first, so <c>SnowDock --restore-taskbar</c> can put things back even after a crash.
/// </summary>
internal sealed class TaskbarController
{
    private static readonly string StateFile =
        Path.Combine(AppPaths.DataDir, "taskbar-state.txt");

    private DispatcherTimer? _guard;
    public bool IsHidden { get; private set; }

    public void Hide()
    {
        SaveOriginalStateOnce();
        SetAutoHide(true);
        SetTaskbarsVisible(false);
        IsHidden = true;

        // Explorer re-shows the taskbar in some cases (Start menu, Win+T, explorer restart).
        // Keep it hidden with a light watchdog.
        _guard ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _guard.Tick -= OnGuardTick;
        _guard.Tick += OnGuardTick;
        _guard.Start();
    }

    public void Show()
    {
        _guard?.Stop();
        IsHidden = false;
        Restore();
    }

    /// <summary>Called after Explorer restarts (TaskbarCreated): the new taskbar is visible again.</summary>
    public void Reapply()
    {
        if (IsHidden) { SetAutoHide(true); SetTaskbarsVisible(false); }
    }

    private void OnGuardTick(object? sender, EventArgs e)
    {
        if (!IsHidden) return;
        foreach (var h in TaskbarWindows())
            if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }

    /// <summary>Static so it can run from the recovery command line without a dock instance.</summary>
    public static void Restore()
    {
        SetTaskbarsVisible(true);
        bool originalAutoHide = false;
        try
        {
            if (File.Exists(StateFile))
            {
                originalAutoHide = File.ReadAllText(StateFile).Trim() == "autohide";
                File.Delete(StateFile);
            }
        }
        catch { /* best effort */ }
        SetAutoHide(originalAutoHide);
    }

    private static void SaveOriginalStateOnce()
    {
        try
        {
            // If a previous run crashed, the file still holds the *real* original state. Don't overwrite it
            // with the auto-hide state we set ourselves.
            if (File.Exists(StateFile)) return;
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(StateFile, IsAutoHide() ? "autohide" : "normal");
        }
        catch { /* best effort */ }
    }

    private static bool IsAutoHide()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        uint state = SHAppBarMessage(ABM_GETSTATE, ref abd);
        return (state & ABS_AUTOHIDE) != 0;
    }

    private static void SetAutoHide(bool autoHide)
    {
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = (IntPtr)(autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP)
        };
        SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    private static void SetTaskbarsVisible(bool visible)
    {
        foreach (var h in TaskbarWindows())
            ShowWindow(h, visible ? SW_SHOWNA : SW_HIDE);
    }

    private static IEnumerable<IntPtr> TaskbarWindows()
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;

        IntPtr after = IntPtr.Zero;
        while (true)
        {
            after = FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
            if (after == IntPtr.Zero) yield break;
            yield return after;
        }
    }
}
