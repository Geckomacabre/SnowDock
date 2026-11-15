using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

/// <summary>A running application: all of its Alt-Tab-eligible top-level windows.</summary>
internal sealed class RunningApp
{
    public required string Key { get; init; }
    public string? ExePath { get; init; }
    public string? Aumid { get; init; }
    public long FirstSeen { get; init; }
    /// <summary>Most recently activated first.</summary>
    public List<IntPtr> Windows { get; } = new();
    public bool AllMinimized => Windows.Count > 0 && Windows.All(IsIconic);
}

/// <summary>
/// Tracks the windows the taskbar would show, grouped by app (AppUserModelID when the window has one,
/// otherwise the process image path). Updated from shell hook messages, with a slow poll as a fallback.
/// </summary>
internal sealed class WindowTracker
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow"
    };

    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private readonly HashSet<IntPtr> _ownWindows = new();
    private readonly Dictionary<IntPtr, long> _lastActive = new();
    private readonly Dictionary<string, long> _firstSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string?> _exeCache = new();
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _debounce;
    private long _clock;
    private string _signature = "";

    public IReadOnlyList<RunningApp> Apps { get; private set; } = Array.Empty<RunningApp>();
    public event Action? Changed;
    /// <summary>A window asked for attention (taskbar flash). Raised with the window handle.</summary>
    public event Action<IntPtr>? AttentionRequested;

    public uint ShellHookMessage { get; } = RegisterWindowMessage("SHELLHOOK");

    public WindowTracker()
    {
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => Refresh();
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
    }

    public void AddOwnWindow(IntPtr hwnd) => _ownWindows.Add(hwnd);

    public void Start() { Refresh(); _poll.Start(); }
    public void Stop() => _poll.Stop();

    public void RequestRefresh() { _debounce.Stop(); _debounce.Start(); }

    public void HandleShellMessage(IntPtr wParam, IntPtr lParam)
    {
        int code = (int)wParam.ToInt64();
        switch (code)
        {
            case HSHELL_WINDOWACTIVATED:
            case HSHELL_RUDEAPPACTIVATED:
                if (lParam != IntPtr.Zero) _lastActive[lParam] = ++_clock;
                RequestRefresh();
                break;
            case HSHELL_FLASH:
                AttentionRequested?.Invoke(lParam);
                break;
            case HSHELL_WINDOWCREATED:
            case HSHELL_WINDOWDESTROYED:
            case HSHELL_REDRAW:
                RequestRefresh();
                break;
        }
    }

    public void Refresh()
    {
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && (!_lastActive.TryGetValue(fg, out var t) || t != _clock))
            _lastActive[fg] = ++_clock;

        var groups = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        var alive = new HashSet<IntPtr>();

        EnumWindows((h, _) =>
        {
            if (!IsTaskbarWindow(h)) return true;
            alive.Add(h);
            string? exe = GetExePath(h);
            string? aumid = GetAumid(h);
            string? key = aumid ?? exe;
            if (key == null) return true;

            if (!groups.TryGetValue(key, out var app))
            {
                if (!_firstSeen.ContainsKey(key)) _firstSeen[key] = ++_clock;
                app = new RunningApp { Key = key, ExePath = exe, Aumid = aumid, FirstSeen = _firstSeen[key] };
                groups[key] = app;
            }
            app.Windows.Add(h);
            return true;
        }, IntPtr.Zero);

        foreach (var app in groups.Values)
            app.Windows.Sort((a, b) => LastActive(b).CompareTo(LastActive(a)));

        // Forget closed windows and apps.
        foreach (var dead in _lastActive.Keys.Where(k => !alive.Contains(k)).ToList()) _lastActive.Remove(dead);
        foreach (var gone in _firstSeen.Keys.Where(k => !groups.ContainsKey(k)).ToList()) _firstSeen.Remove(gone);

        Apps = groups.Values.OrderBy(a => a.FirstSeen).ToList();

        // Include the ordered handles so focus changes and window swaps rebind the dock's items
        // (otherwise a click could activate a stale or closed window).
        var sig = string.Join("|", Apps.Select(a => $"{a.Key}:{a.AllMinimized}:{string.Join(",", a.Windows)}"));
        if (sig != _signature)
        {
            _signature = sig;
            Changed?.Invoke();
        }
    }

    private long LastActive(IntPtr h) => _lastActive.TryGetValue(h, out var t) ? t : 0;

    private bool IsTaskbarWindow(IntPtr h)
    {
        if (_ownWindows.Contains(h) || !IsWindowVisible(h)) return false;
        // Nothing SnowDock opens (folder picker, Exposé, fans) is a "running app".
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == OwnPid) return false;

        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if ((ex & WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
        if (GetWindow(h, GW_OWNER) != IntPtr.Zero && !appWindow) return false;
        if ((ex & WS_EX_NOACTIVATE) != 0 && !appWindow) return false;

        // Cloaked: windows on other virtual desktops, suspended UWP frames, etc.
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        if (GetWindowTextLength(h) == 0) return false;

        var cls = new StringBuilder(256);
        GetClassName(h, cls, cls.Capacity);
        return !IgnoredClasses.Contains(cls.ToString());
    }

    public static string GetTitle(IntPtr h)
    {
        int len = GetWindowTextLength(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private string? GetExePath(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (_exeCache.TryGetValue(pid, out var cached)) return cached;
        string? path = null;
        var hp = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hp != IntPtr.Zero)
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(hp, 0, sb, ref size)) path = sb.ToString();
            CloseHandle(hp);
        }
        if (_exeCache.Count > 512) _exeCache.Clear();
        _exeCache[pid] = path;
        return path;
    }

    public static string? GetAumid(IntPtr h)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(h, ref iid, out var store) != 0 || store == null) return null;
        try
        {
            var key = PKEY_AppUserModel_ID;
            store.GetValue(ref key, out var pv);
            try
            {
                const ushort VT_LPWSTR = 31;
                return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.pointerValue) : null;
            }
            finally { PropVariantClear(ref pv); }
        }
        catch { return null; }
        finally { Marshal.ReleaseComObject(store); }
    }
}
