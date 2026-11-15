// The click sequence sent to an icon's app (and how it's packed per NOTIFYICON_VERSION) is adapted from
// ManagedShell's NotifyIcon (https://github.com/cairoshell/ManagedShell), Apache License 2.0.
// See THIRD-PARTY-NOTICES.md.

using static SnowDock.Native.NativeMethods;
using static SnowDock.Widgets.Tray.TrayNative;

namespace SnowDock.Widgets.Tray;

/// <summary>
/// The tray icons every tray widget shows, from two sources: the listener (live data, clicks work), and a
/// read-only scan every few seconds (fills in anything the listener hasn't heard, and which icons Windows shows
/// on the taskbar). Snapshots and --preview use the scan only, so they never change anything for other apps.
/// </summary>
internal static class TrayService
{
    private static readonly object Gate = new(), PublishGate = new();
    private static readonly Dictionary<string, TrayIcon> Heard = new();
    private static readonly Dictionary<uint, string?> ExeByPid = new();
    private static List<TrayIcon> _scanned = new();
    private static List<TrayEntry> _entries = new();
    private static IReadOnlyList<TrayIcon> _icons = Array.Empty<TrayIcon>();
    private static int _version, _users, _scanning;
    private static string _signature = "";
    private static TrayHost? _host;
    private static Timer? _rescan;
    private static readonly Timer PublishSoon = new(_ => Publish(), null, Timeout.Infinite, Timeout.Infinite);
    private static bool _loggedScan;
    private static string? _lastClickKey;
    private static long _lastClickTick;

    /// <summary>Snapshots and the side-by-side preview must not touch the real tray traffic.</summary>
    public static bool ReadOnly { get; } = Environment.GetCommandLineArgs()
        .Any(a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase) || a.Equals("--preview", StringComparison.OrdinalIgnoreCase));

    /// <summary>True while the listener runs, i.e. clicks reach the apps.</summary>
    public static bool Listening => _host != null;
    public static int Version => _version;

    /// <summary>Visible icons: Windows' taskbar ones first, then the hidden ones, each by name.</summary>
    public static IReadOnlyList<TrayIcon> Icons => _icons;

    /// <summary>The icons now; with nobody using the service yet (a card preview), one read-only scan first.</summary>
    public static IReadOnlyList<TrayIcon> Current()
    {
        if (_users == 0 && _icons.Count == 0) Rescan();
        return _icons;
    }

    public static void Acquire()
    {
        if (_users++ > 0) return;
        if (!ReadOnly)
        {
            var host = new TrayHost();
            host.Message += OnMessage;
            host.ExplorerRestarted += () => { lock (Gate) Heard.Clear(); SchedulePublish(); };
            if (host.Start()) _host = host;
        }
        if (ReadOnly) Rescan(); // a snapshot draws right away, so fill the list before the first frame
        _rescan = new Timer(_ => Rescan(), null, ReadOnly ? 3000 : 0, 3000);
    }

    public static void Release()
    {
        if (_users == 0 || --_users > 0) return;
        _rescan?.Dispose();
        _rescan = null;
        _host?.Stop();
        _host = null;
        lock (Gate) Heard.Clear();
    }

    /// <summary>Listener thread: record one Shell_NotifyIcon call (Explorer already accepted it).</summary>
    private static void OnMessage(TrayMessage m)
    {
        if (m.HWnd == IntPtr.Zero && m.Guid == Guid.Empty) return;
        lock (Gate)
        {
            var key = m.Guid != Guid.Empty ? "g" + m.Guid.ToString("N") : $"{m.HWnd.ToInt64():X}:{m.UID}";
            Heard.TryGetValue(key, out var icon);
            switch (m.Message)
            {
                case NIM_DELETE:
                    Heard.Remove(key);
                    break;
                case NIM_SETVERSION:
                    if (icon != null) Heard[key] = icon with { Version = m.Version };
                    break;
                case NIM_ADD or NIM_MODIFY:
                    icon ??= new TrayIcon { HWnd = m.HWnd, UID = m.UID, Guid = m.Guid };
                    if (m.HWnd != IntPtr.Zero) icon = icon with { HWnd = m.HWnd, UID = m.UID };
                    if ((m.Flags & NIF_MESSAGE) != 0) icon = icon with { CallbackMessage = m.CallbackMessage };
                    if ((m.Flags & NIF_TIP) != 0) icon = icon with { Tip = m.Tip };
                    if ((m.Flags & NIF_ICON) != 0) icon = icon with { Image = m.Icon };
                    if (m.Hidden is bool hidden) icon = icon with { Hidden = hidden };
                    Heard[key] = icon;
                    break;
            }
        }
        SchedulePublish();
    }

    /// <summary>Apps often send several calls in a row; merge once they settle (off the listener thread).</summary>
    private static void SchedulePublish() => PublishSoon.Change(120, Timeout.Infinite);

    private static void Rescan()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            long start = Environment.TickCount64;
            var entries = TrayScan.ReadEntries();
            var live = TrayScan.LiveIcons(entries);
            lock (Gate) (_entries, _scanned) = (entries, live);
            Publish();
            if (!_loggedScan)
            {
                _loggedScan = true;
                AppPaths.Log($"Tray scan: {live.Count} live of {entries.Count} remembered icons in {Environment.TickCount64 - start} ms (listener {(Listening ? "on" : "off")})");
            }
        }
        catch (Exception ex) { AppPaths.Log($"Tray scan failed: {ex.Message}"); }
        finally { Volatile.Write(ref _scanning, 0); }
    }

    private static void Publish()
    {
        lock (PublishGate) Merge();
    }

    private static void Merge()
    {
        List<TrayIcon> heard, scanned;
        List<TrayEntry> entries;
        lock (Gate)
        {
            // Windows drops icons whose window is gone; so do we.
            foreach (var dead in Heard.Where(kv => !IsWindow(kv.Value.HWnd)).Select(kv => kv.Key).ToList()) Heard.Remove(dead);
            (heard, scanned, entries) = (Heard.Values.ToList(), _scanned, _entries);
        }

        var all = new Dictionary<string, TrayIcon>();
        foreach (var h in heard)
        {
            if (h.Hidden) continue;
            var exe = ExeFor(h.HWnd);
            var entry = entries.FirstOrDefault(e => h.Guid != Guid.Empty ? e.Guid == h.Guid
                : e.Guid == Guid.Empty && e.Uid == h.UID && string.Equals(e.Exe, exe, StringComparison.OrdinalIgnoreCase));
            all[h.Key] = h with
            {
                ExePath = exe ?? entry?.Exe, Promoted = entry?.Promoted ?? false,
                Image = h.Image ?? TrayScan.Decode(entry?.Png), Name = TrayIcon.Describe(h.Tip, exe ?? entry?.Exe)
            };
        }
        // What the listener hasn't heard yet (apps that ignore TaskbarCreated, or no listener at all). Icons the
        // listener knows stay its way, hidden ones included.
        var heardKeys = heard.Select(h => h.Key).ToHashSet();
        foreach (var s in scanned)
            if (!heardKeys.Contains(s.Key)) all.TryAdd(s.Key, s);

        var list = all.Values.OrderByDescending(i => i.Promoted).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var signature = string.Join("\n", list.Select(i => $"{i.Key}|{i.Tip}|{i.Promoted}|{i.CanClick}|{i.Image?.GetHashCode()}"));
        lock (Gate)
        {
            if (signature == _signature) return;
            _signature = signature;
            _icons = list;
            _version++;
        }
    }

    private static string? ExeFor(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return null;
        lock (ExeByPid)
        {
            if (ExeByPid.Count > 256) ExeByPid.Clear();
            if (!ExeByPid.TryGetValue(pid, out var exe)) ExeByPid[pid] = exe = TrayScan.ExePath(pid);
            return exe;
        }
    }

    /// <summary>
    /// Do what clicking the icon in the taskbar does: send the app its own notification message, packed the way
    /// its NOTIFYICON_VERSION expects, with the cursor as the anchor for any menu. False if the icon can't be clicked.
    /// </summary>
    public static bool Click(TrayIcon icon, bool right)
    {
        if (!icon.CanClick || !IsWindow(icon.HWnd)) return false;
        GetCursorPos(out var at);
        GetWindowThreadProcessId(icon.HWnd, out uint pid);
        AllowSetForegroundWindow((int)pid); // so its window or menu can come to the front
        if (right)
        {
            Notify(icon, WM_RBUTTONDOWN, at);
            Notify(icon, WM_RBUTTONUP, at);
            if (icon.Version >= 3) Notify(icon, WM_CONTEXTMENU, at);
            return true;
        }
        long now = Environment.TickCount64;
        bool second = _lastClickKey == icon.Key && now - _lastClickTick <= GetDoubleClickTime();
        Notify(icon, second ? WM_LBUTTONDBLCLK : WM_LBUTTONDOWN, at);
        Notify(icon, WM_LBUTTONUP, at);
        if (icon.Version >= 3) Notify(icon, NIN_SELECT, at);
        (_lastClickKey, _lastClickTick) = (second ? null : icon.Key, now);
        return true;
    }

    private static void Notify(TrayIcon icon, int message, POINT at)
    {
        // Version 4: wParam = anchor x/y, lParam = event | icon id << 16. Older: wParam = icon id, lParam = event.
        var (w, l) = icon.Version >= 4
            ? (MakeLong(at.X, at.Y), MakeLong(message, (int)icon.UID))
            : ((IntPtr)(int)icon.UID, (IntPtr)message);
        SendNotifyMessage(icon.HWnd, icon.CallbackMessage, w, l);
    }
}
