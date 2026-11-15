using System.IO;
using System.Runtime.InteropServices;

namespace SnowDock.Native;

/// <summary>
/// Copy, move and delete through the shell itself, so Windows shows its own progress, "replace or skip"
/// and Recycle Bin confirmation dialogs.
/// </summary>
internal static class FileOps
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    private const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40, FOF_WANTNUKEWARNING = 0x4000;

    public static bool Copy(IEnumerable<string> paths, string folder, IntPtr owner) => Run(FO_COPY, paths, folder, owner);
    public static bool Move(IEnumerable<string> paths, string folder, IntPtr owner) => Run(FO_MOVE, paths, folder, owner);

    /// <summary>
    /// To the Recycle Bin. Some files can't go there (network shares, some removable drives, files too big for
    /// the bin) and would be deleted outright; for those Windows is told to warn first.
    /// </summary>
    public static bool Recycle(IEnumerable<string> paths, IntPtr owner) =>
        Run(FO_DELETE, paths, null, owner, FOF_ALLOWUNDO | FOF_WANTNUKEWARNING);

    /// <summary>Explorer's rule: dragging within one drive moves, dragging to another drive copies.</summary>
    public static bool SameDrive(IEnumerable<string> paths, string folder)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? "";
        return paths.All(p => string.Equals(Path.GetPathRoot(Path.GetFullPath(p)), root, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Run(uint func, IEnumerable<string> paths, string? to, IntPtr owner, ushort flags = FOF_ALLOWUNDO)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (list.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = func,
            pFrom = string.Join("\0", list) + "\0\0", // double-null-terminated list
            pTo = to == null ? null : to + "\0\0",
            fFlags = flags
        };
        int hr = SHFileOperation(ref op);
        AppPaths.Log($"File operation {func} on {list.Count} item(s) -> {to ?? "Recycle Bin"}: result={hr}, aborted={op.fAnyOperationsAborted}");
        return hr == 0 && !op.fAnyOperationsAborted;
    }
}
