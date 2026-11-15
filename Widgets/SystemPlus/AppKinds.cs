using System.Windows.Media;

namespace SnowDock.Widgets.SystemPlus;

internal enum AppKind { Browser, Communication, Development, Media, Games, Other }

/// <summary>Sorts apps into a few kinds by their exe name (and, for games, where they're installed).</summary>
internal static class AppKinds
{
    private static readonly Dictionary<string, AppKind> Known = Build();

    private static Dictionary<string, AppKind> Build()
    {
        var d = new Dictionary<string, AppKind>(StringComparer.OrdinalIgnoreCase);
        void Add(AppKind k, string names) { foreach (var n in names.Split(' ')) d[n] = k; }
        Add(AppKind.Browser, "chrome msedge firefox brave opera vivaldi arc iexplore librewolf waterfox chromium zen floorp thorium palemoon yandex");
        Add(AppKind.Communication, "teams ms-teams slack discord zoom outlook olk thunderbird telegram whatsapp signal skype webex ciscowebex " +
            "mailspring element hxoutlook mattermost rocketchat viber lync");
        Add(AppKind.Development, "devenv code code-insiders cursor windsurf zed rider64 idea64 pycharm64 webstorm64 clion64 goland64 rustrover64 " +
            "datagrip64 phpstorm64 studio64 windowsterminal wt powershell pwsh cmd conhost wsl bash mintty sublime_text notepad++ vim nvim gvim " +
            "unity unityhub unrealeditor godot gitkraken githubdesktop sourcetree fork postman insomnia dbeaver ssms azuredatastudio " +
            "dockerdesktop wireshark fiddler ilspy dnspy x64dbg ghidra");
        d["Docker Desktop"] = d["Code - Insiders"] = AppKind.Development;
        Add(AppKind.Media, "spotify vlc mpv music.ui microsoft.media.player applemusic itunes potplayer potplayermini64 wmplayer foobar2000 " +
            "obs64 netflix plex plexamp kodi mpc-hc64 mpc-be64 audacity tidal deezer aimp musicbee winamp video.ui");
        Add(AppKind.Games, "steam steamwebhelper epicgameslauncher battle.net galaxyclient riotclientservices leagueclient eadesktop " +
            "ubisoftconnect upc minecraft minecraftlauncher robloxplayerbeta xboxpcapp gamingservices playnite heroic");
        return d;
    }

    /// <summary>Folders games install into, for games the list above doesn't name.</summary>
    private static readonly string[] GameFolders =
    {
        @"\steamapps\common\", @"\epic games\", @"\gog galaxy\games\", @"\xboxgames\", @"\riot games\", @"\ubisoft game launcher\games\",
        @"\ea games\", @"\windowsapps\microsoft.minecraft", @"\battle.net\"
    };

    public static AppKind Classify(string exe, string path)
    {
        if (Known.TryGetValue(exe, out var k)) return k;
        var lower = path.ToLowerInvariant();
        return GameFolders.Any(lower.Contains) ? AppKind.Games : AppKind.Other;
    }

    public static string Label(AppKind k) => k switch
    {
        AppKind.Browser => "Browsing", AppKind.Communication => "Communication", AppKind.Development => "Development",
        AppKind.Media => "Media", AppKind.Games => "Games", _ => "Other"
    };

    private static readonly Dictionary<AppKind, Brush> Brushes = new()
    {
        [AppKind.Browser] = Solid(0x3B, 0x8B, 0xF6),
        [AppKind.Communication] = Solid(0xAF, 0x52, 0xDE),
        [AppKind.Development] = Solid(0x34, 0xC7, 0x59),
        [AppKind.Media] = Solid(0xFF, 0x2D, 0x55),
        [AppKind.Games] = Solid(0xFF, 0x9F, 0x0A),
        [AppKind.Other] = Solid(0x8E, 0x8E, 0x93)
    };

    public static Brush Brush(AppKind k) => Brushes[k];

    private static Brush Solid(byte r, byte g, byte b)
    {
        var s = new SolidColorBrush(Color.FromRgb(r, g, b));
        s.Freeze();
        return s;
    }
}
