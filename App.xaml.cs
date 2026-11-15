using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SnowDock.Native;

namespace SnowDock;

/// <summary>
/// Command line:
///   SnowDock                    run the dock, hiding the Windows taskbar (see config.json: HideTaskbar)
///   SnowDock --preview          run above the taskbar without touching it (safe for trying things out)
///   SnowDock --restore-taskbar  bring the Windows taskbar back after a crash, then exit
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private DockWindow? _dock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();

        if (args.Contains("--restore-taskbar"))
        {
            TaskbarController.Restore();
            AppPaths.Log("Taskbar restored from command line");
            Shutdown();
            return;
        }

        if (ArgValue(e.Args, "--copy-test") is string copySpec && copySpec.Split('|') is [var copyFrom, var copyTo])
        {
            // Debug: one real shell copy, to check the file-operation plumbing.
            AppPaths.Log($"Copy test: {FileOps.Copy(new[] { copyFrom }, copyTo, IntPtr.Zero)}");
            Shutdown();
            return;
        }

        if (ArgValue(e.Args, "--poof-preview") is string poofPath)
        {
            PoofPreview.Save(Path.GetFullPath(poofPath), 52);
            Shutdown();
            return;
        }

        if (args.Contains("--install"))
        {
            try { Installer.Install(); }
            catch (Exception ex) { MessageBox.Show($"Install failed: {ex.Message}", "SnowDock"); }
            Shutdown();
            return;
        }
        if (args.Contains("--uninstall"))
        {
            try { Installer.Uninstall(quiet: args.Contains("--quiet")); }
            catch (Exception ex) { AppPaths.Log($"Uninstall failed: {ex}"); }
            Shutdown();
            return;
        }

        string? snapshot = ArgValue(e.Args, "--snapshot");
        bool preview = args.Contains("--preview") || snapshot != null;
        if (snapshot != null)
        {
            RunSnapshot(snapshot, e.Args);
            return;
        }

        // Restarting (after install, update, or a display-mode change): let the old copy exit first.
        if (int.TryParse(ArgValue(e.Args, "--after"), out var oldPid))
        {
            try { System.Diagnostics.Process.GetProcessById(oldPid).WaitForExit(15000); } catch { /* already gone */ }
        }
        _singleInstance = new Mutex(true, preview ? @"Local\SnowDock.Preview" : @"Local\SnowDock", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            AppPaths.Log($"FATAL: {ev.ExceptionObject}");
            CleanupAll();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAll();
        SessionEnding += (_, _) => CleanupAll();

        var config = DockConfig.Load();
        _preview = preview;
        // Debug: --all-displays tries one dock per display without saving the setting.
        if (args.Contains("--all-displays")) config.AllDisplays = true;
        // Debug: --update-check REPO logs what the update checker finds for that repository, then exits.
        if (ArgValue(e.Args, "--update-check") is string repo)
        {
            Updater.Start(repo);
            _ = Updater.Check().ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                AppPaths.Log($"Update check result for {repo}: {(Updater.Available is { } u ? $"{u.Version} zip={u.ZipUrl != null}" : "none")}");
                Shutdown();
            }));
            return;
        }
        if (config.AllDisplays)
        {
            // One dock per display; the one on the main display owns the taskbar.
            bool owner = true;
            foreach (var mon in Monitors.All().OrderByDescending(m => m.IsPrimary))
            {
                var dock = new DockWindow(config, preview, mon.DeviceName, ownsTaskbar: owner);
                owner = false;
                _docks.Add(dock);
                dock.Show();
            }
        }
        else
        {
            var dock = new DockWindow(config, preview);
            _docks.Add(dock);
            dock.Show();
        }
        _dock = _docks[0];
        if (!preview) Updater.Start(config.UpdateRepo);
    }

    private readonly List<DockWindow> _docks = new();
    private bool _preview;

    private void CleanupAll()
    {
        foreach (var d in _docks) d.Cleanup();
        _dock?.Cleanup();
    }

    /// <summary>Start a fresh copy (it waits for this one to exit) and quit: used when the set of docks changes.</summary>
    public void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            Arguments = $"--after {Environment.ProcessId}" + (_preview ? " --preview" : "")
        });
        Shutdown();
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Debug: render the dock to a PNG (optionally hovering item N) and exit. Never touches the taskbar.</summary>
    private void RunSnapshot(string path, string[] args)
    {
        int? hover = int.TryParse(ArgValue(args, "--hover"), out var n) ? n : null;
        string? dumpDir = ArgValue(args, "--dump-icons");
        // --drag FROM:TO fakes a drag of item FROM to the spot before item TO (nothing is saved).
        int? dragFrom = null, dragTo = null;
        var drag = ArgValue(args, "--drag")?.Split(':');
        if (drag is { Length: 2 } && int.TryParse(drag[0], out var f) && int.TryParse(drag[1], out var t))
        {
            dragFrom = f;
            dragTo = t;
        }
        var config = DockConfig.Load(persist: false); // snapshots never write the real settings
        // --edge Left|Right: render a side dock (in memory only; the setting on disk is untouched).
        if (Enum.TryParse<DockEdge>(ArgValue(args, "--edge"), true, out var edge)) config.Edge = edge;
        // --style id: render another era's look (also in memory only). --backdrop light: a bright desktop.
        if (ArgValue(args, "--style") is string styleId) config.Style = styleId;
        // --widgets a,b,c: show these widgets (in memory only).
        if (ArgValue(args, "--widgets") is string widgetList)
            config.Widgets = widgetList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => new Widgets.WidgetConfig { Type = t.Trim() }).ToList();
        DockWindow.SnapshotLightBackdrop = string.Equals(ArgValue(args, "--backdrop"), "light", StringComparison.OrdinalIgnoreCase);
        DockWindow.SnapshotEditMode = args.Any(a => a.Equals("--edit", StringComparison.OrdinalIgnoreCase));
        _dock = new DockWindow(config, preview: true);
        _dock.Loaded += async (_, _) =>
        {
            await Task.Delay(1500); // let the window tracker and icons settle
            bool drop = args.Any(a => string.Equals(a, "--drop", StringComparison.OrdinalIgnoreCase));
            try
            {
                // --fan K: the K-th stack fanned out (with --hover as the highlighted row).
                if (args.Any(a => a.Equals("--picker", StringComparison.OrdinalIgnoreCase)))
                    _dock.SavePickerPreview(Path.GetFullPath(path));
                else if (ArgValue(args, "--card") is string cardType)
                    _dock.SaveCardPreview(Path.GetFullPath(path), cardType);
                else if (args.Any(a => a.Equals("--menus", StringComparison.OrdinalIgnoreCase)))
                    _dock.SaveMenuPreview(Path.GetFullPath(path));
                else if (args.Any(a => a.Equals("--switch-test", StringComparison.OrdinalIgnoreCase)))
                    await _dock.RunStyleSwitchTest(Path.GetFullPath(path));
                else if (ArgValue(args, "--drop-plans") is string dropList)
                    _dock.WriteDropPlans(Path.GetFullPath(path), dropList.Split('|'));
                else if (int.TryParse(ArgValue(args, "--grid"), out var gridStack))
                    _dock.SaveStackViewSnapshot(Path.GetFullPath(path), gridStack, list: false);
                else if (int.TryParse(ArgValue(args, "--list"), out var listStack))
                    _dock.SaveStackViewSnapshot(Path.GetFullPath(path), listStack, list: true);
                else if (args.Any(a => a.Equals("--expose", StringComparison.OrdinalIgnoreCase)))
                    _dock.SaveExposePreview(Path.GetFullPath(path));
                else if (args.Any(a => a.Equals("--expose-live", StringComparison.OrdinalIgnoreCase)))
                    await _dock.RunExposeLive(1100, Path.GetFullPath(path));
                else if (int.TryParse(ArgValue(args, "--fan"), out var fanStack))
                    _dock.SaveFanSnapshot(Path.GetFullPath(path), fanStack, hover ?? -1);
                else
                    _dock.SaveSnapshot(Path.GetFullPath(path), hover, dumpDir, dragFrom, dragTo, drop,
                        double.TryParse(ArgValue(args, "--hide"), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var hideAmt) ? hideAmt : 0);
            }
            catch (Exception ex) { AppPaths.Log($"Snapshot failed: {ex}"); }
            Shutdown();
        };
        _dock.Show();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the dock alive on UI glitches; a dead dock with a hidden taskbar is worse.
        AppPaths.Log($"UI exception: {e.Exception}");
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupAll();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
