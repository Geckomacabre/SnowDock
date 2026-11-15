using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using Microsoft.Win32;

namespace SnowDock.Native;

/// <summary>
/// A per-user install with no admin rights: copies SnowDock to %LocalAppData%\Programs\SnowDock, adds a Start
/// menu shortcut, and registers it in Windows Settings → Apps → Installed apps so it can be uninstalled from
/// there. Uninstalling brings the taskbar back and removes everything except your settings folder.
/// </summary>
internal static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SnowDock";
    public const string RepoUrl = "https://github.com/Geckomacabre/SnowDock";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SnowDock");
    public static string InstalledExe => Path.Combine(InstallDir, "SnowDock.exe");
    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SnowDock.lnk");

    public static bool IsInstalled => File.Exists(InstalledExe) && Registry.CurrentUser.OpenSubKey(UninstallKey) != null;

    /// <summary>True when this process is the installed copy.</summary>
    public static bool RunningInstalled =>
        string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), InstallDir, StringComparison.OrdinalIgnoreCase);

    public static string Version =>
        typeof(Installer).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Copy this build into place, register it, and start the installed copy.</summary>
    public static void Install(bool startAfter = true)
    {
        var source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if (!RunningInstalled)
        {
            Directory.CreateDirectory(InstallDir);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(InstallDir, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        CreateShortcut(ShortcutPath, InstalledExe, "A Mac-style dock that replaces the taskbar");
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "SnowDock");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "SnowDock");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("URLInfoAbout", RepoUrl);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirectorySize(InstallDir) / 1024), RegistryValueKind.DWord);
        }
        // If "start at sign-in" was on for another copy, point it at the installed one.
        if (StartupEntry.IsEnabled()) StartupEntry.Set(true, InstalledExe);
        AppPaths.Log($"Installed {Version} to {InstallDir}");
        if (startAfter) Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, Arguments = $"--after {Environment.ProcessId}" });
    }

    /// <summary>Remove SnowDock (not your settings): stop other copies, restore the taskbar, delete files.</summary>
    public static void Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show("Uninstall SnowDock? Your settings in %AppData%\\SnowDock are kept.", "SnowDock",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var p in Process.GetProcessesByName("SnowDock").Where(p => p.Id != Environment.ProcessId))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        TaskbarController.Restore(); // a killed copy couldn't restore it itself
        StartupEntry.Set(false);
        try { File.Delete(ShortcutPath); } catch { }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        // The running exe can't delete its own folder; a short-lived cmd does it once this process exits.
        if (Directory.Exists(InstallDir))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
            });
        AppPaths.Log("Uninstalled");
        if (!quiet) MessageBox.Show("SnowDock has been removed. The Windows taskbar is back.", "SnowDock");
    }

    private static long DirectorySize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr a, int b, IntPtr c, uint d); void GetIDList(out IntPtr p); void SetIDList(IntPtr p);
        void GetDescription(IntPtr a, int b); void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetWorkingDirectory(IntPtr a, int b); void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetArguments(IntPtr a, int b); void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetHotkey(out short h); void SetHotkey(short h); void GetShowCmd(out int c); void SetShowCmd(int c);
        void GetIconLocation(IntPtr a, int b, out int c); void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, uint r); void Resolve(IntPtr h, uint f);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
    }

    private static void CreateShortcut(string lnk, string target, string description)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(lnk, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }
}
