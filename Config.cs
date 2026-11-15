using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnowDock.Native;

namespace SnowDock;

internal sealed class PinnedItem
{
    public string Name { get; set; } = "";
    /// <summary>What gets launched: a .lnk, an .exe, or shell:AppsFolder\{AUMID}.</summary>
    public string LaunchPath { get; set; } = "";
    public string? Arguments { get; set; }
    /// <summary>Resolved executable, used to match running windows to this pin.</summary>
    public string? TargetExe { get; set; }
    /// <summary>The shortcut's own arguments, kept only for matching (e.g. Update.exe --processStart Discord.exe).</summary>
    public string? ShortcutArguments { get; set; }
    public string? Aumid { get; set; }
    /// <summary>File Explorer plays the Finder role: always first, can't be removed.</summary>
    public bool Permanent { get; set; }
}

internal enum DockEdge { Bottom, Left, Right, Top }
internal enum StackSort { DateAdded, DateModified, Name, Kind }
internal enum StackDisplay { Stack, Folder }
/// <summary>How a stack opens. Automatic: a fan when everything fits in one, otherwise a grid.</summary>
internal enum StackView { Fan, Grid, List, Automatic }

/// <summary>A folder shown on the right of the separator; clicking it fans out its newest items.</summary>
internal sealed class StackConfig
{
    public string Path { get; set; } = "";
    public string? Name { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StackSort Sort { get; set; } = StackSort.DateAdded;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StackDisplay Display { get; set; } = StackDisplay.Stack;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StackView View { get; set; } = StackView.Fan;

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! :
        System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path;
}

internal sealed class DockConfig
{
    /// <summary>Which era's look to use (see Styles/DockStyles.cs); unknown ids fall back to Snow Leopard.</summary>
    public string Style { get; set; } = "snow-leopard";

    /// <summary>Icon size at rest, in DIPs.</summary>
    public double IconSize { get; set; } = 52;
    public bool Magnification { get; set; } = true;
    public double MagnifiedSize { get; set; } = 96;
    /// <summary>Hide the Windows taskbar while the dock runs. Note this hides the system tray too.</summary>
    public bool HideTaskbar { get; set; } = true;
    /// <summary>Stop maximized windows above the shelf.</summary>
    public bool ReserveScreenSpace { get; set; } = true;
    /// <summary>Slide the dock off-screen until the mouse touches the screen edge.</summary>
    public bool AutoHide { get; set; }
    public bool ShowRecycleBin { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    /// <summary>Device name of the monitor to use (e.g. \\.\DISPLAY2); empty means the primary monitor.</summary>
    public string? Monitor { get; set; }
    public bool ImportedTaskbarPins { get; set; }
    public List<PinnedItem> Pinned { get; set; } = new();
    /// <summary>Most items a fan shows before offering "N more in File Explorer".</summary>
    public int FanMaxItems { get; set; } = 14;
    public bool AddedDefaultStacks { get; set; }
    public List<StackConfig> Stacks { get; set; } = new();
    /// <summary>Widgets in the dock, in order (between the apps and the stacks).</summary>
    public List<Widgets.WidgetConfig> Widgets { get; set; } = new();
    /// <summary>24-hour clocks; empty means follow this PC's time format.</summary>
    public bool? Clock24h { get; set; }
    /// <summary>A dock on every display (each display's taskbar hidden), instead of one on <see cref="Monitor"/>.</summary>
    public bool AllDisplays { get; set; }
    /// <summary>GitHub repository whose Releases are checked for updates.</summary>
    public string UpdateRepo { get; set; } = "Geckomacabre/SnowDock";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="persist">Write back the result (imports, migrations). Snapshot/debug runs pass false so they
    /// never touch the real settings file.</param>
    public static DockConfig Load(bool persist = true)
    {
        DockConfig cfg;
        try
        {
            cfg = File.Exists(AppPaths.ConfigFile)
                ? JsonSerializer.Deserialize<DockConfig>(File.ReadAllText(AppPaths.ConfigFile), Json) ?? new DockConfig()
                : new DockConfig();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Config unreadable, using defaults: {ex.Message}");
            cfg = new DockConfig();
        }

        if (!cfg.ImportedTaskbarPins)
        {
            cfg.Pinned.AddRange(ImportTaskbarPins());
            cfg.ImportedTaskbarPins = true;
        }

        if (!cfg.AddedDefaultStacks)
        {
            // Like the Mac's default dock: Documents and Downloads beside the Recycle Bin.
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var downloads = KnownFolders.Downloads();
            if (Directory.Exists(documents)) cfg.Stacks.Add(new StackConfig { Path = documents, Sort = StackSort.DateModified });
            if (downloads != null && Directory.Exists(downloads)) cfg.Stacks.Add(new StackConfig { Path = downloads });
            cfg.AddedDefaultStacks = true;
        }

        // Shortcuts can change (app updates); re-read them so window matching stays right.
        foreach (var pin in cfg.Pinned.Where(p => p.LaunchPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                                                   && File.Exists(p.LaunchPath)))
        {
            var info = ShortcutResolver.Resolve(pin.LaunchPath);
            pin.TargetExe = info.TargetPath ?? pin.TargetExe;
            pin.ShortcutArguments = info.Arguments;
            pin.Aumid = info.Aumid ?? pin.Aumid;
        }
        cfg.EnsureExplorerFirst();
        if (persist) cfg.Save();
        return cfg;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) { AppPaths.Log($"Config save failed: {ex.Message}"); }
    }

    private static string ExplorerExe =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static bool IsExplorerPin(PinnedItem p) =>
        p.Permanent
        || string.Equals(ShortcutResolver.NormalizePath(p.TargetExe), ExplorerExe, StringComparison.OrdinalIgnoreCase)
        || p.Aumid == "Microsoft.Windows.Explorer"
        // The taskbar's own "File Explorer.lnk" points at a shell location, not explorer.exe.
        || string.Equals(Path.GetFileName(p.LaunchPath), "File Explorer.lnk", StringComparison.OrdinalIgnoreCase);

    /// <summary>File Explorer takes the Finder's place: exactly one, always first.</summary>
    private void EnsureExplorerFirst()
    {
        Pinned.RemoveAll(IsExplorerPin);
        Pinned.Insert(0, new PinnedItem
        {
            Name = "File Explorer",
            LaunchPath = ExplorerExe,
            TargetExe = ExplorerExe,
            Permanent = true
        });
    }

    /// <summary>Copies the user's existing taskbar pins, so the dock starts with familiar apps.</summary>
    private static IEnumerable<PinnedItem> ImportTaskbarPins()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        if (!Directory.Exists(dir)) yield break;

        foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk").OrderBy(File.GetCreationTimeUtc))
        {
            var info = ShortcutResolver.Resolve(lnk);
            yield return new PinnedItem
            {
                Name = FriendlyName(lnk),
                LaunchPath = lnk,
                TargetExe = info.TargetPath,
                ShortcutArguments = info.Arguments,
                Aumid = info.Aumid
            };
        }
    }

    public static string FriendlyName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        name = name.Replace(" - Shortcut", "", StringComparison.OrdinalIgnoreCase);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length > 0 && char.IsLower(name[0]))
            name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
        return name;
    }
}
