using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using static SnowDock.Native.NativeMethods;
using static SnowDock.Widgets.Tray.TrayNative;

namespace SnowDock.Widgets.Tray;

/// <summary>An icon Windows remembers in HKCU\Control Panel\NotifyIconSettings (one per app icon it has ever shown).</summary>
internal sealed record TrayEntry(string Exe, uint Uid, Guid Guid, string Tip, byte[]? Png, bool Promoted);

/// <summary>
/// The read-only view of the tray: nothing is sent to apps and nothing changes. Windows 11 remembers every
/// notification icon (exe, icon id, last picture, whether it's shown on the taskbar) in the registry; an entry
/// whose program is running is checked with the documented Shell_NotifyIconGetRect, which only succeeds for icons
/// the shell has right now. That gives the list, pictures and taskbar/hidden state, but not the message an app
/// expects for clicks, which only the listener (<see cref="TrayHost"/>) hears.
/// </summary>
internal static class TrayScan
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";
    /// <summary>(exe|uid) → the window that owned the icon last time, so a rescan rarely walks windows.</summary>
    private static readonly Dictionary<string, IntPtr> Owners = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>(exe|uid) → when walking its program's windows last found nothing; retried after a while, not every scan.</summary>
    private static readonly Dictionary<string, long> Misses = new(StringComparer.OrdinalIgnoreCase);
    private const long RetryMissMs = 30_000;
    private static readonly Dictionary<uint, string?> PathByPid = new();

    public static List<TrayEntry> ReadEntries()
    {
        var list = new List<TrayEntry>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (root == null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k?.GetValue("ExecutablePath") is not string exe) continue;
                uint uid = k.GetValue("UID") is int u ? unchecked((uint)u) : 0;
                var guid = Guid.TryParse(k.GetValue("IconGuid") as string, out var g) ? g : Guid.Empty;
                list.Add(new TrayEntry(ExpandKnownFolder(exe), uid, guid, k.GetValue("InitialTooltip") as string ?? "",
                    k.GetValue("IconSnapshot") as byte[], k.GetValue("IsPromoted") is int p && p != 0));
            }
        }
        catch (Exception ex) { AppPaths.Log($"Tray scan: registry: {ex.Message}"); }
        return list;
    }

    /// <summary>Paths are stored as "{known-folder id}\rest" for Program Files, System32 and the like.</summary>
    private static string ExpandKnownFolder(string path)
    {
        if (!path.StartsWith('{') || path.IndexOf('}') is not (> 0 and var close)) return path;
        if (!Guid.TryParse(path[..(close + 1)], out var id)) return path;
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return path;
        try { return Marshal.PtrToStringUni(p) + path[(close + 1)..]; }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>The icons the shell has right now, among the remembered ones whose program is running.</summary>
    public static List<TrayIcon> LiveIcons(List<TrayEntry> entries)
    {
        var icons = new List<TrayIcon>();
        var wanted = new HashSet<string>(entries.Select(e => e.Exe), StringComparer.OrdinalIgnoreCase);
        var pidsByExe = RunningPrograms(wanted);
        Dictionary<uint, List<IntPtr>>? windows = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long now = Environment.TickCount64;
        foreach (var e in entries)
        {
            if (!pidsByExe.TryGetValue(e.Exe, out var pids)) continue;
            string key = e.Guid != Guid.Empty ? e.Guid.ToString() : e.Exe + "|" + e.Uid;
            if (!seen.Add(key)) continue; // Windows keeps the odd duplicate
            IntPtr owner = IntPtr.Zero;
            if (e.Guid != Guid.Empty)
            {
                if (!Exists(IntPtr.Zero, 0, e.Guid)) continue;
            }
            else if (Owners.TryGetValue(key, out var known) && IsWindow(known) && Exists(known, e.Uid, Guid.Empty)) owner = known;
            else
            {
                Owners.Remove(key);
                if (Misses.TryGetValue(key, out var missed) && now - missed < RetryMissMs) continue;
                windows ??= WindowsByProcess();
                owner = pids.SelectMany(pid => windows.GetValueOrDefault(pid) ?? new List<IntPtr>())
                            .FirstOrDefault(h => Exists(h, e.Uid, Guid.Empty));
                if (owner == IntPtr.Zero) { Misses[key] = now; continue; }
                Misses.Remove(key);
                Owners[key] = owner;
            }
            icons.Add(new TrayIcon
            {
                HWnd = owner, UID = e.Uid, Guid = e.Guid, Tip = e.Tip, Image = Decode(e.Png), Promoted = e.Promoted,
                ExePath = e.Exe, Name = TrayIcon.Describe(e.Tip, e.Exe)
            });
        }
        return icons;
    }

    private static bool Exists(IntPtr hWnd, uint uid, Guid guid)
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = hWnd, uID = uid, guidItem = guid };
        return Shell_NotifyIconGetRect(ref id, out _) == 0;
    }

    /// <summary>Running processes whose program is in <paramref name="wanted"/>, by full path.</summary>
    private static Dictionary<string, List<uint>> RunningPrograms(HashSet<string> wanted)
    {
        var result = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        var alive = new HashSet<uint>();
        foreach (var proc in Process.GetProcesses())
        {
            uint pid = (uint)proc.Id;
            proc.Dispose();
            alive.Add(pid);
            if (!PathByPid.TryGetValue(pid, out var path)) PathByPid[pid] = path = ExePath(pid);
            if (path == null || !wanted.Contains(path)) continue;
            if (!result.TryGetValue(path, out var list)) result[path] = list = new List<uint>();
            list.Add(pid);
        }
        foreach (var gone in PathByPid.Keys.Where(pid => !alive.Contains(pid)).ToList()) PathByPid.Remove(gone);
        return result;
    }

    public static string? ExePath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Top-level and message-only windows (tray icons often belong to the latter), by process.</summary>
    private static Dictionary<uint, List<IntPtr>> WindowsByProcess()
    {
        var map = new Dictionary<uint, List<IntPtr>>();
        void Add(IntPtr h)
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (!map.TryGetValue(pid, out var list)) map[pid] = list = new List<IntPtr>();
            list.Add(h);
        }
        EnumWindows((h, _) => { Add(h); return true; }, IntPtr.Zero);
        for (IntPtr h = IntPtr.Zero; (h = FindWindowEx(HWND_MESSAGE, h, null, null)) != IntPtr.Zero;) Add(h);
        return map;
    }

    private static readonly Dictionary<string, ImageSource?> Pictures = new();

    /// <summary>The remembered picture (a small PNG), decoded once per distinct image.</summary>
    public static ImageSource? Decode(byte[]? png)
    {
        if (png == null || png.Length == 0) return null;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(png));
        lock (Pictures)
        {
            if (Pictures.TryGetValue(key, out var known)) return known;
            ImageSource? img = null;
            try
            {
                var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                img = frame;
            }
            catch { /* not a picture */ }
            if (Pictures.Count > 300) Pictures.Clear();
            return Pictures[key] = img;
        }
    }
}
