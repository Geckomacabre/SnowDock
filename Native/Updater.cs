using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Threading;
using SnowDock.Widgets.Online;

namespace SnowDock.Native;

/// <summary>A newer release found on GitHub.</summary>
internal sealed record UpdateInfo(string Version, string PageUrl, string? ZipUrl);

/// <summary>
/// Checks the project's GitHub Releases a minute after start and every six hours. When a newer version is out
/// the dock offers an Update button: for an installed copy one click downloads the release zip, swaps the
/// files once SnowDock has exited, and starts the new version; otherwise it opens the release page.
/// Only GitHub's public releases API is contacted; nothing is sent about you.
/// </summary>
internal static class Updater
{
    private static DispatcherTimer? _timer;
    private static string _repo = "Geckomacabre/SnowDock";
    public static UpdateInfo? Available { get; private set; }
    public static event Action? AvailableChanged;

    public static void Start(string repo)
    {
        _repo = repo;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(6);
            await Check();
        };
        _timer.Start();
    }

    /// <summary>Ask GitHub for the latest release and compare it with this build's version.</summary>
    public static async Task Check()
    {
        try
        {
            // https://docs.github.com/en/rest/releases/releases#get-the-latest-release
            using var doc = await Web.GetJson($"https://api.github.com/repos/{_repo}/releases/latest",
                r => r.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28"));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!System.Version.TryParse(tag.TrimStart('v', 'V'), out var latest) ||
                !System.Version.TryParse(Installer.Version, out var current) || latest <= current) return;
            string? zip = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    if (a.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        zip = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
            Available = new UpdateInfo(latest.ToString(3), root.GetProperty("html_url").GetString() ?? Installer.RepoUrl, zip);
            AppPaths.Log($"Update available: {Available.Version}");
            AvailableChanged?.Invoke();
        }
        catch (WebError ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            AppPaths.Log($"Update check: {_repo} has no published releases yet");
        }
        catch (Exception ex) { AppPaths.Log($"Update check failed: {ex.Message}"); }
    }

    /// <summary>Install the available update (installed copies), or open its release page.</summary>
    public static async Task Apply()
    {
        var u = Available;
        if (u == null) return;
        if (!Installer.RunningInstalled || u.ZipUrl == null)
        {
            DockWindow.ShellOpen(u.PageUrl);
            return;
        }
        try
        {
            var work = Path.Combine(Path.GetTempPath(), $"SnowDock-update-{u.Version}");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var zipPath = Path.Combine(work, "update.zip");
            await File.WriteAllBytesAsync(zipPath, await Web.GetBytes(u.ZipUrl));
            var files = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zipPath, files);
            // If the zip holds one top-level folder, use what's inside it.
            var dirs = Directory.GetDirectories(files);
            if (Directory.GetFiles(files).Length == 0 && dirs.Length == 1) files = dirs[0];
            if (!File.Exists(Path.Combine(files, "SnowDock.exe"))) throw new InvalidOperationException("The release zip has no SnowDock.exe");

            // Swap files after we exit, then start the new version.
            var script = Path.Combine(work, "apply.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                $":wait\r\ntasklist /fi \"PID eq {Environment.ProcessId}\" | find \"{Environment.ProcessId}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"robocopy \"{files}\" \"{Installer.InstallDir}\" /E /IS /IT /NFL /NDL /NJH /NJS /NP >nul\r\n" +
                $"start \"\" \"{Installer.InstalledExe}\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
            AppPaths.Log($"Updating to {u.Version}");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Update failed: {ex.Message}");
            DockWindow.ShellOpen(u.PageUrl);
        }
    }
}
