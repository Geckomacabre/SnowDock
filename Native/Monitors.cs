using System.Runtime.InteropServices;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

internal sealed record MonitorDesc(IntPtr Handle, string DeviceName, RECT Bounds, RECT Work, bool IsPrimary);

/// <summary>
/// Display enumeration. The dock remembers its monitor by device name (e.g. \\.\DISPLAY2), because
/// monitor indices change when displays are plugged in or rearranged.
/// </summary>
internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoEx(IntPtr hMon, ref MONITORINFOEX info);

    private const uint MONITORINFOF_PRIMARY = 1;

    public static List<MonitorDesc> All()
    {
        var list = new List<MonitorDesc>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoEx(h, ref info))
                list.Add(new MonitorDesc(h, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        // Left to right, then top to bottom: the order people expect in a menu.
        return list.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>The monitor with this device name, or the primary one if it's gone (or none was chosen).</summary>
    public static MonitorDesc Find(string? deviceName)
    {
        var all = All();
        return all.FirstOrDefault(m => deviceName != null && string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(m => m.IsPrimary)
               ?? all.First();
    }
}
