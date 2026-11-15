using System.Runtime.InteropServices;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

internal static class KnownFolders
{
    private static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>The user's Downloads folder, honouring a relocated folder (it's not in Environment.SpecialFolder).</summary>
    public static string? Downloads()
    {
        if (SHGetKnownFolderPath(FOLDERID_Downloads, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }
}
