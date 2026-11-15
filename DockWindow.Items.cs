using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SnowDock.Native;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

internal sealed partial class DockWindow
{
    private readonly Dictionary<PinnedItem, DockItem> _pinItems = new();
    private readonly Dictionary<string, DockItem> _runningItems = new(StringComparer.OrdinalIgnoreCase);
    private DockItem? _recycleBin;
    private bool _recycleFull;

    private void RebuildItems()
    {
        // Mid-drag the dragged icon is out of the row; rebuilding would put it back. EndDrag rebuilds.
        if (IsDragging) return;

        var apps = _tracker.Apps;
        var claimed = new HashSet<RunningApp>();
        var items = new List<DockItem>();

        foreach (var pin in _config.Pinned)
        {
            if (!_pinItems.TryGetValue(pin, out var item))
            {
                item = new DockItem { Pin = pin, Name = pin.Name };
                item.SetIcon(IconLoader.ForPath(pin.LaunchPath) ??
                             (pin.TargetExe != null ? IconLoader.ForPath(pin.TargetExe) : null));
                _pinItems[pin] = item;
            }
            item.Running = apps.FirstOrDefault(a => !claimed.Contains(a) && item.Matches(a));
            if (item.Running != null) claimed.Add(item.Running);
            items.Add(item);
        }

        // Running apps that aren't pinned go after the pinned ones, in launch order.
        foreach (var app in apps.Where(a => !claimed.Contains(a)))
        {
            if (!_runningItems.TryGetValue(app.Key, out var item))
            {
                item = new DockItem { Name = AppName(app) };
                item.SetIcon((app.Aumid != null ? IconLoader.ForAumid(app.Aumid) : null) ??
                             (app.ExePath != null ? IconLoader.ForPath(app.ExePath) : null));
                _runningItems[app.Key] = item;
            }
            item.Running = app;
            items.Add(item);
        }

        foreach (var stale in _pinItems.Keys.Where(p => !_config.Pinned.Contains(p)).ToList()) _pinItems.Remove(stale);
        foreach (var gone in _runningItems.Keys.Where(k => apps.All(a => a.Key != k)).ToList()) _runningItems.Remove(gone);

        // Right of the separator: folder stacks, then the Recycle Bin.
        var right = StackItems().ToList();
        if (_config.ShowRecycleBin)
        {
            if (_recycleBin == null)
            {
                _recycleBin = new DockItem { Kind = DockItemKind.RecycleBin, Name = "Recycle Bin" };
                _recycleFull = RecycleBinHasItems();
                _recycleBin.SetIcon(IconLoader.ForPath(IconLoader.RecycleBinPath, bypassCache: true));
            }
            right.Add(_recycleBin);
        }
        int separator = right.Count > 0 ? items.Count : -1;
        items.AddRange(right);

        foreach (var item in items.Where(i => i.Bounce == BounceKind.Launch && i.IsRunning))
            item.LaunchCompleted();

        _surface.Items.Clear();
        _surface.Items.AddRange(items);
        _surface.SeparatorIndex = separator;
        if (_surface.Hovered != null && !items.Contains(_surface.Hovered)) _surface.Hovered = null;
        EnsureAnimating();
    }

    private static string AppName(RunningApp app)
    {
        if (app.ExePath == null || app.ExePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return app.Windows.Count > 0 ? WindowTracker.GetTitle(app.Windows[0]) : "App";
        try
        {
            var desc = FileVersionInfo.GetVersionInfo(app.ExePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
        }
        catch { }
        return DockConfig.FriendlyName(app.ExePath);
    }

    private void OnAttentionRequested(IntPtr hwnd)
    {
        var item = _surface.Items.FirstOrDefault(i => i.Running?.Windows.Contains(hwnd) == true);
        if (item == null || item.Bounce != BounceKind.None || GetForegroundWindow() == hwnd) return;
        item.StartAttentionBounce();
        EnsureAnimating();
    }

    private void OnItemClicked(DockItem item)
    {
        if (item.Kind == DockItemKind.RecycleBin)
        {
            ShellOpen("shell:RecycleBinFolder");
            return;
        }
        if (item.Kind == DockItemKind.Stack)
        {
            OpenStack(item);
            return;
        }
        if (item.IsRunning) ActivateApp(item.Running!);
        else Launch(item);
    }

    private void Launch(DockItem item)
    {
        if (item.Pin == null) return;
        if (!ShellOpen(item.Pin.LaunchPath, item.Pin.Arguments)) return;
        item.StartLaunchBounce();
        EnsureAnimating();
        _tracker.RequestRefresh();
    }

    internal static bool ShellOpen(string path, string? args = null)
    {
        try
        {
            var psi = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", path)
                : new ProcessStartInfo(path) { UseShellExecute = true, Arguments = args ?? "" };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Launch failed for {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Mac behavior: bring the app's windows forward; if they're all minimized, restore the latest one.</summary>
    private static void ActivateApp(RunningApp app)
    {
        if (app.Windows.Count == 0) return;
        if (app.AllMinimized)
        {
            ForceForeground(app.Windows[0]);
            return;
        }
        var visible = app.Windows.Where(w => !IsIconic(w)).ToList();
        for (int i = visible.Count - 1; i >= 1; i--)
            SetWindowPos(visible[i], IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        ForceForeground(visible[0]);
    }

    public static void ForceForeground(IntPtr h)
    {
        if (IsIconic(h)) ShowWindowAsync(h, SW_RESTORE);
        if (SetForegroundWindow(h)) return;

        // The foreground lock can refuse us; a synthetic Alt tap is the standard way past it.
        const byte VK_MENU = 0x12;
        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(h);
    }

    private static bool RecycleBinHasItems()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0;
    }

    private void RefreshRecycleBin()
    {
        if (_recycleBin == null) return;
        bool full = RecycleBinHasItems();
        if (full == _recycleFull) return;
        _recycleFull = full;
        _recycleBin.SetIcon(IconLoader.ForPath(IconLoader.RecycleBinPath, bypassCache: true));
        _surface.InvalidateVisual();
    }

    private void SetKeepInDock(DockItem item, bool keep)
    {
        if (!keep && item.Pin != null && !item.Pin.Permanent)
        {
            _config.Pinned.Remove(item.Pin);
        }
        else if (keep && item.Pin == null)
        {
            var pin = CreatePinFor(item);
            if (pin == null) return;
            _config.Pinned.Add(pin);
        }
        _config.Save();
        RebuildItems();
    }

    /// <summary>A pin for a running, unpinned app (Store apps launch through shell:AppsFolder).</summary>
    private static PinnedItem? CreatePinFor(DockItem item)
    {
        var app = item.Running;
        if (app == null) return null;
        bool isFrameHost = app.ExePath?.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) == true;
        string launch = isFrameHost && app.Aumid != null ? @"shell:AppsFolder\" + app.Aumid : app.ExePath ?? "";
        if (launch.Length == 0) return null;
        return new PinnedItem
        {
            Name = item.Name,
            LaunchPath = launch,
            TargetExe = isFrameHost ? null : app.ExePath,
            Aumid = app.Aumid
        };
    }

    private static void ShowInExplorer(DockItem item)
    {
        string? path = item.Pin?.TargetExe ?? item.Running?.ExePath ?? item.Pin?.LaunchPath;
        if (path == null || !File.Exists(path)) return;
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { AppPaths.Log($"Show in Explorer failed: {ex.Message}"); }
    }
}
