using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnowDock.Widgets.Tray;

/// <summary>One notification-area icon, as last reported. Immutable: updates replace the whole record.</summary>
internal sealed record TrayIcon
{
    /// <summary>The window that receives the icon's mouse notifications.</summary>
    public IntPtr HWnd { get; init; }
    public uint UID { get; init; }
    public Guid Guid { get; init; }
    /// <summary>The app's notification message; 0 when only the read-only scan has seen the icon (no clicks then).</summary>
    public uint CallbackMessage { get; init; }
    /// <summary>NOTIFYICON_VERSION the app asked for (0, 3 or 4); decides how notifications are packed.</summary>
    public uint Version { get; init; }
    public string Tip { get; init; } = "";
    /// <summary>Frozen, so the dock can draw it whichever thread made it.</summary>
    public ImageSource? Image { get; init; }
    /// <summary>The app itself hid the icon (NIS_HIDDEN); Windows shows it nowhere, and neither do we.</summary>
    public bool Hidden { get; init; }
    /// <summary>Windows shows it on the taskbar rather than in the hidden-icons flyout.</summary>
    public bool Promoted { get; init; }
    public string? ExePath { get; init; }
    public string Name { get; init; } = "";

    public string Key => Guid != Guid.Empty ? "g" + Guid.ToString("N") : $"{HWnd.ToInt64():X}:{UID}";
    public bool CanClick => CallbackMessage != 0 && HWnd != IntPtr.Zero;

    /// <summary>The tooltip on one line (apps put status on extra lines).</summary>
    public string TipLine => string.Join(" · ", Tip.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>The program's own name: its file description, else the exe name.</summary>
    public string AppName => AppNameFor(ExePath);

    private static readonly ConcurrentDictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase);

    public static string AppNameFor(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return "";
        return Descriptions.GetOrAdd(exe, path =>
        {
            try
            {
                var d = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(d) && !d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return d;
            }
            catch { /* no version resource */ }
            return Path.GetFileNameWithoutExtension(path);
        });
    }

    private static readonly ConditionalWeakTable<ImageSource, StrongBox<int>> Tones = new();

    /// <summary>
    /// +1 for a near-white single-colour glyph, -1 for near-black, 0 otherwise. Apps draw tray icons for the
    /// taskbar's own colour, so a white glyph vanishes on a light tile (and a black one on a dark tile).
    /// </summary>
    public static int ToneOf(ImageSource image)
    {
        if (image is not BitmapSource bmp) return 0;
        if (Tones.TryGetValue(image, out var known)) return known.Value;
        int tone = 0;
        try
        {
            BitmapSource src = bmp.Format == PixelFormats.Pbgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            double lum = 0, sat = 0, weight = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                int a = px[i + 3];
                if (a < 40) continue;
                double b = px[i] * 255.0 / a, g = px[i + 1] * 255.0 / a, r = px[i + 2] * 255.0 / a; // un-premultiply
                lum += (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 * a;
                sat += (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 255 * a;
                weight += a;
            }
            if (weight > 0 && sat / weight < 0.15) tone = lum / weight > 0.8 ? 1 : lum / weight < 0.2 ? -1 : 0;
        }
        catch { /* unreadable: treat as colourful */ }
        Tones.AddOrUpdate(image, new StrongBox<int>(tone));
        return tone;
    }

    /// <summary>What to call the icon: the first tooltip line, else the program's name.</summary>
    public static string Describe(string tip, string? exe)
    {
        var first = tip.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrEmpty(first)) return first;
        var app = AppNameFor(exe);
        return app.Length > 0 ? app : "Tray icon";
    }
}
