using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SnowDock.Widgets;

/// <summary>
/// One shared reading of the machine, refreshed at most once a second however many system widgets are
/// showing. Uses plain Win32 calls and .NET APIs (no performance-counter package).
/// </summary>
internal static class SystemSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    private static DateTime _last = DateTime.MinValue;
    private static ulong _idle, _kernel, _user;
    private static long _rx, _tx;
    private static DateTime _netAt;

    public static double Cpu { get; private set; }          // 0..1
    public static double Ram { get; private set; }          // 0..1
    public static double RamUsedGb { get; private set; }
    public static double RamTotalGb { get; private set; }
    public static double DownBps { get; private set; }
    public static double UpBps { get; private set; }
    public static Queue<double> DownHistory { get; } = new();
    public static Queue<double> UpHistory { get; } = new();
    public static bool HasBattery { get; private set; }
    public static int BatteryPercent { get; private set; }
    public static bool Charging { get; private set; }

    /// <summary>Refresh if the last reading is older than ~1 s.</summary>
    public static void Sample()
    {
        var now = DateTime.UtcNow;
        if ((now - _last).TotalMilliseconds < 900) return;
        _last = now;

        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong di = idle.Value - _idle, dk = kernel.Value - _kernel, du = user.Value - _user; // kernel includes idle
            if (_kernel != 0 && dk + du > 0) Cpu = Math.Clamp(1 - (double)di / (dk + du), 0, 1);
            (_idle, _kernel, _user) = (idle.Value, kernel.Value, user.Value);
        }

        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            RamTotalGb = m.ullTotalPhys / 1073741824.0;
            RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            Ram = m.ullTotalPhys == 0 ? 0 : RamUsedGb / RamTotalGb;
        }

        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var st = nic.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
            double secs = (now - _netAt).TotalSeconds;
            if (_netAt != default && secs > 0)
            {
                DownBps = Math.Max(0, (rx - _rx) / secs);
                UpBps = Math.Max(0, (tx - _tx) / secs);
                Push(DownHistory, DownBps);
                Push(UpHistory, UpBps);
            }
            (_rx, _tx, _netAt) = (rx, tx, now);
        }
        catch { /* adapters can vanish mid-read */ }

        if (GetSystemPowerStatus(out var power))
        {
            HasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255 && power.BatteryLifePercent <= 100;
            BatteryPercent = power.BatteryLifePercent;
            Charging = power.ACLineStatus == 1;
        }
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > 40) q.Dequeue();
    }

    // ---- per-app usage, only computed when a Top CPU / Top RAM widget asks ----
    private static readonly Dictionary<int, TimeSpan> _cpuTimes = new();
    private static DateTime _procAt;
    public static IReadOnlyList<(string Name, double Value)> TopCpu { get; private set; } = Array.Empty<(string, double)>();
    public static IReadOnlyList<(string Name, double Value)> TopRam { get; private set; } = Array.Empty<(string, double)>();

    /// <summary>Per-app CPU share and memory, grouped by app name like Task Manager. At most every 2 s.</summary>
    public static void SampleProcesses()
    {
        var now = DateTime.UtcNow;
        double secs = (now - _procAt).TotalSeconds;
        if (secs < 2) return;
        var cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var ram = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0) continue;
                string name = p.ProcessName;
                ram[name] = ram.GetValueOrDefault(name) + p.WorkingSet64;
                var t = p.TotalProcessorTime; // throws for protected processes
                seen.Add(p.Id);
                if (_cpuTimes.TryGetValue(p.Id, out var before) && _procAt != default)
                    cpu[name] = cpu.GetValueOrDefault(name) + (t - before).TotalSeconds / secs / Environment.ProcessorCount;
                _cpuTimes[p.Id] = t;
            }
            catch { }
            finally { p.Dispose(); }
        }
        foreach (var dead in _cpuTimes.Keys.Where(k => !seen.Contains(k)).ToList()) _cpuTimes.Remove(dead);
        _procAt = now;
        TopCpu = cpu.Where(kv => kv.Key != "Idle").OrderByDescending(kv => kv.Value).Take(3).Select(kv => (kv.Key, kv.Value)).ToList();
        TopRam = ram.OrderByDescending(kv => kv.Value).Take(3).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public static string Speed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s"
    };
}
