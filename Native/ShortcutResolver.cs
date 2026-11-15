using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

internal sealed record ShortcutInfo(string? TargetPath, string? Arguments, string? Aumid);

/// <summary>Reads a .lnk file's target, arguments and AppUserModelID.</summary>
internal static class ShortcutResolver
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr pfd, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    public static ShortcutInfo Resolve(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new CShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sl = (IShellLinkW)link;

            var sb = new StringBuilder(1024);
            sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string? target = sb.Length > 0 ? Environment.ExpandEnvironmentVariables(sb.ToString()) : null;

            sb.Clear();
            sl.GetArguments(sb, sb.Capacity);
            string? args = sb.Length > 0 ? sb.ToString() : null;

            string? aumid = null;
            try
            {
                var store = (IPropertyStore)link;
                var key = PKEY_AppUserModel_ID;
                store.GetValue(ref key, out var pv);
                if (pv.vt == 31) aumid = Marshal.PtrToStringUni(pv.pointerValue);
                PropVariantClear(ref pv);
            }
            catch { /* no property store on this link */ }

            return new ShortcutInfo(target, args, string.IsNullOrWhiteSpace(aumid) ? null : aumid);
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Could not resolve {lnkPath}: {ex.Message}");
            return new ShortcutInfo(null, null, null);
        }
        finally
        {
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim('"'))); }
        catch { return path; }
    }
}
