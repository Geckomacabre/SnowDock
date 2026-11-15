using System.Runtime.InteropServices;
using SnowDock.Native;

namespace SnowDock.Widgets.SystemPlus;

/// <summary>A screen whose brightness can be read (and set): an external monitor over DDC/CI, or a built-in panel via WMI.</summary>
internal sealed record Screen(string Key, string Name, bool BuiltIn, int Percent);

/// <summary>
/// Screen brightness. External monitors speak DDC/CI (dxva2), which is slow — tens of milliseconds a call — so
/// everything runs on the thread pool, one operation at a time; reads happen at most every 10 s.
/// </summary>
internal static class BrightnessService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] list);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] list);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Pending = new();
    private static readonly Dictionary<string, (uint Min, uint Max)> Ranges = new();
    private static DateTime _last = DateTime.MinValue;
    private static int _busy;

    public static IReadOnlyList<Screen> Screens { get; private set; } = Array.Empty<Screen>();
    public static bool Ready { get; private set; }
    public static int Version { get; private set; }
    public static event Action? Changed;

    public static void Request(bool now = false)
    {
        if (!now && (DateTime.UtcNow - _last).TotalSeconds < 10) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _last = DateTime.UtcNow;
        Run(() => { }, read: true);
    }

    /// <summary>Set one screen's brightness (0..100). A slider drag collapses to the latest value.</summary>
    public static void Set(string key, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        Screens = Screens.Select(s => s.Key == key ? s with { Percent = percent } : s).ToList();
        lock (Pending) Pending[key] = percent;
        Run(() =>
        {
            int value;
            lock (Pending)
            {
                if (!Pending.Remove(key, out value)) return;
            }
            if (key.StartsWith("wmi:")) SetBuiltIn(key[4..], value);
            else WithMonitors((k, handle, _) => { if (k == key && Ranges.TryGetValue(k, out var r)) SetMonitorBrightness(handle, r.Min + (uint)Math.Round((r.Max - r.Min) * value / 100.0)); });
        }, read: false);
    }

    private static void Run(Action work, bool read)
    {
        var ui = System.Windows.Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            List<Screen>? screens = null;
            lock (Gate)
            {
                try { work(); }
                catch (Exception ex) { AppPaths.Log($"Brightness change failed: {ex.Message}"); }
                if (read) screens = ReadAll();
            }
            if (!read) return;
            ui?.BeginInvoke(() =>
            {
                bool changed = !Ready || !screens!.SequenceEqual(Screens);
                (Screens, Ready) = (screens!, true);
                Interlocked.Exchange(ref _busy, 0);
                if (!changed) return;
                Version++;
                Changed?.Invoke();
            });
        });
    }

    private static List<Screen> ReadAll()
    {
        var list = new List<Screen>();
        int n = 0;
        try
        {
            WithMonitors((key, handle, primary) =>
            {
                n++;
                if (!GetMonitorBrightness(handle, out uint min, out uint cur, out uint max) || max <= min) return;
                Ranges[key] = (min, max);
                var screen = new Screen(key, $"Display {n}{(primary ? " (main)" : "")}", false, (int)Math.Round((cur - min) * 100.0 / (max - min)));
                if (primary) list.Insert(0, screen); else list.Add(screen); // the tile shows the main screen
            });
        }
        catch (Exception ex) { AppPaths.Log($"DDC/CI read failed: {ex.Message}"); }
        try
        {
            // Laptop panels (WMI says "not supported" on desktops).
            foreach (var (name, level) in Wmi.Query(@"root\wmi", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness",
                         o => ((string)o.InstanceName, (int)Convert.ToInt32(o.CurrentBrightness))))
                list.Add(new Screen("wmi:" + name, "Built-in display", true, Math.Clamp(level, 0, 100)));
        }
        catch { }
        return list;
    }

    /// <summary>Call <paramref name="each"/> for every physical monitor (key, handle, on the main display), then release the handles.</summary>
    private static void WithMonitors(Action<string, IntPtr, bool> each)
    {
        foreach (var mon in Monitors.All())
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(mon.Handle, out uint count) || count == 0) continue;
            var list = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(mon.Handle, count, list)) continue;
            try
            {
                for (int i = 0; i < count; i++) each($"ddc:{mon.DeviceName}:{i}", list[i].Handle, mon.IsPrimary);
            }
            finally { DestroyPhysicalMonitors(count, list); }
        }
    }

    private static void SetBuiltIn(string instance, int percent)
    {
        string escaped = instance.Replace(@"\", @"\\").Replace("'", @"\'");
        foreach (var o in Wmi.Objects(@"root\wmi", $"SELECT * FROM WmiMonitorBrightnessMethods WHERE InstanceName='{escaped}'"))
            o.WmiSetBrightness(1, (byte)percent);
    }
}
