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

        string? snapshot = ArgValue(e.Args, "--snapshot");
        bool preview = args.Contains("--preview") || snapshot != null;
        if (snapshot != null)
        {
            RunSnapshot(snapshot, e.Args);
            return;
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
            _dock?.Cleanup();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _dock?.Cleanup();
        SessionEnding += (_, _) => _dock?.Cleanup();

        var config = DockConfig.Load();
        _dock = new DockWindow(config, preview);
        _dock.Show();
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
        var config = DockConfig.Load();
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
        _dock?.Cleanup();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
