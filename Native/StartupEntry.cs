using Microsoft.Win32;

namespace SnowDock.Native;

/// <summary>
/// "Start SnowDock at sign-in": a value under the current user's Run key, pointing at this exe.
/// Off unless the user turns it on from the dock menu.
/// </summary>
internal static class StartupEntry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnowDock";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        AppPaths.Log($"Start at sign-in: {enabled} ({Environment.ProcessPath})");
    }
}
