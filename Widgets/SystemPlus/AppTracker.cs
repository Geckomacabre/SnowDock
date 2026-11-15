using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Widgets.SystemPlus;

/// <summary>One app's foreground seconds for one day, hour by hour.</summary>
internal sealed class AppDay
{
    public string Label { get; set; } = "";
    public AppKind Kind { get; set; }
    public int[] Hours { get; set; } = new int[24];
    [System.Text.Json.Serialization.JsonIgnore] public int Total => Hours.Sum();
}

/// <summary>A day's record, saved as screentime\yyyy-MM-dd.json.</summary>
internal sealed class DayRecord
{
    public string Date { get; set; } = "";
    public Dictionary<string, AppDay> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A light foreground-app tracker. A screen-time tile in the dock samples the foreground window once a second
/// (from its tick); idle time (no input for 2 minutes) isn't counted. It only runs, and only writes, while one
/// of those tiles is in the dock; a debug snapshot never writes.
/// </summary>
internal static class AppTracker
{
    private const int IdleLimitMs = 120_000;
    private static readonly HashSet<DockWidget> Users = new();
    private static readonly Dictionary<uint, (string Exe, string Path, DateTime At)> Pids = new();
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase);
    private static readonly bool ReadOnly = Environment.GetCommandLineArgs().Any(a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
    private static DateTime _lastSample = DateTime.MinValue, _lastSave = DateTime.Now;
    private static DayRecord? _today;
    private static bool _dirty, _hooked;
    private static readonly object FileGate = new();

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string Folder => WidgetEnv.DataFile("screentime");

    /// <summary>Today's record (loaded from disk the first time).</summary>
    public static DayRecord Today => _today ??= Load(DateTime.Today);

    /// <summary>Record one second for the foreground app. Only tiles that live in the dock (a Host) drive it.</summary>
    public static void Sample(DockWidget tile, DateTime now)
    {
        if (tile.Host == null) return;
        Users.Add(tile);
        if (!_hooked) { _hooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => Save(sync: true); }
        double gap = (now - _lastSample).TotalSeconds;
        if (gap < 0.9) return; // two tiles mustn't count the same second twice
        _lastSample = now;
        if (Today.Date != Key(now))
        {
            Save(sync: false);
            _today = Load(now.Date);
        }
        if (IdleMs() < IdleLimitMs && Foreground() is (string exe, string path))
        {
            if (!Today.Apps.TryGetValue(exe, out var app))
                Today.Apps[exe] = app = new AppDay { Label = Label(exe, path), Kind = AppKinds.Classify(exe, path) };
            app.Hours[now.Hour] += gap is > 0 and < 3 ? (int)Math.Round(gap) : 1;
            _dirty = true;
        }
        if ((now - _lastSave).TotalSeconds >= 60) Save(sync: false);
    }

    /// <summary>A tile left the dock: stop when none are left, saving first.</summary>
    public static void Release(DockWidget tile)
    {
        if (Users.Remove(tile) && Users.Count == 0) Save(sync: false);
    }

    private static void Save(bool sync)
    {
        _lastSave = DateTime.Now;
        if (ReadOnly || !_dirty || _today == null) return;
        _dirty = false;
        string json = JsonSerializer.Serialize(_today), file = Path.Combine(Folder, _today.Date + ".json");
        void Write()
        {
            try
            {
                lock (FileGate)
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(file + ".tmp", json);
                    File.Move(file + ".tmp", file, overwrite: true);
                }
            }
            catch (Exception ex) { AppPaths.Log($"Saving screen time failed: {ex.Message}"); }
        }
        if (sync) Write(); else Task.Run(Write);
    }

    public static DayRecord Load(DateTime day)
    {
        if (_today != null && _today.Date == Key(day)) return _today;
        try
        {
            var file = Path.Combine(Folder, Key(day) + ".json");
            if (File.Exists(file) && JsonSerializer.Deserialize<DayRecord>(File.ReadAllText(file)) is { } rec)
            {
                rec.Apps = new Dictionary<string, AppDay>(rec.Apps, StringComparer.OrdinalIgnoreCase);
                foreach (var a in rec.Apps.Values) if (a.Hours.Length != 24) a.Hours = a.Hours.Concat(new int[24]).Take(24).ToArray();
                return rec;
            }
        }
        catch (Exception ex) { AppPaths.Log($"Reading screen time for {day:yyyy-MM-dd} failed: {ex.Message}"); }
        return new DayRecord { Date = Key(day) };
    }

    /// <summary>The days of the current week up to today (the week starts on this PC's first day of the week).</summary>
    public static IEnumerable<DayRecord> ThisWeek()
    {
        var first = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var day = DateTime.Today;
        int back = ((int)day.DayOfWeek - (int)first + 7) % 7;
        for (int i = back; i >= 0; i--) yield return Load(day.AddDays(-i));
    }

    // ---- the foreground app ----

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint Size, Time; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);

    private static uint IdleMs()
    {
        var info = new LASTINPUTINFO { Size = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : 0;
    }

    /// <summary>The foreground window's exe name and path (Store apps are found inside their frame host), or null.</summary>
    private static (string Exe, string Path)? Foreground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        var app = ProcessOf(pid);
        if (app?.Exe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) == true)
        {
            uint inner = 0;
            EnumChildWindows(hwnd, (child, _) =>
            {
                GetWindowThreadProcessId(child, out uint p);
                if (p != pid) { inner = p; return false; }
                return true;
            }, IntPtr.Zero);
            if (inner != 0) app = ProcessOf(inner);
        }
        if (app == null || app.Value.Exe.Equals("LockApp", StringComparison.OrdinalIgnoreCase)) return null;
        return app;
    }

    private static (string Exe, string Path)? ProcessOf(uint pid)
    {
        if (pid == 0) return null;
        if (Pids.TryGetValue(pid, out var hit) && (DateTime.UtcNow - hit.At).TotalMinutes < 5) return (hit.Exe, hit.Path);
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref size)) return null;
            string path = sb.ToString(), exe = Path.GetFileNameWithoutExtension(path);
            if (Pids.Count > 500) Pids.Clear();
            Pids[pid] = (exe, path, DateTime.UtcNow);
            return (exe, path);
        }
        finally { CloseHandle(h); }
    }

    /// <summary>A friendly app name from the exe's description ("Google Chrome"), else the exe name.</summary>
    private static string Label(string exe, string path)
    {
        if (Labels.TryGetValue(exe, out var l)) return l;
        string label = exe;
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(d) && d.Length <= 40) label = d;
        }
        catch { }
        return Labels[exe] = label;
    }
}
