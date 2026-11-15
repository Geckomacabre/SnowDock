using System.Globalization;
using System.IO;

namespace SnowDock.Widgets;

/// <summary>Settings and storage every widget shares.</summary>
internal static class WidgetEnv
{
    /// <summary>24-hour clocks (set from config; defaults to this PC's own time format).</summary>
    public static bool Clock24h { get; set; } = !CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');

    public static string Time(DateTime t) => Clock24h ? t.ToString("HH:mm") : t.ToString("h:mm");
    public static string AmPm(DateTime t) => Clock24h ? "" : t.ToString("tt", CultureInfo.CurrentCulture);

    /// <summary>A widget's own data file (notes, todos) next to config.json.</summary>
    public static string DataFile(string name)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        return Path.Combine(AppPaths.DataDir, name);
    }
}
