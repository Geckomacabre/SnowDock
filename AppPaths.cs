using System.IO;

namespace SnowDock;

internal static class AppPaths
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnowDock");

    public static readonly string ConfigFile = Path.Combine(DataDir, "config.json");
    public static readonly string LogFile = Path.Combine(DataDir, "snowdock.log");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
