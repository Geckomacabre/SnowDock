using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using SnowDock.Native;

namespace SnowDock;

/// <summary>
/// Dropping things from File Explorer onto the dock, Mac style:
///   apps (.exe/.lnk) between icons     → pinned at that spot (the icons part to make room)
///   files onto an app                  → opened with that app
///   files onto a stack                 → moved there (same drive) or copied (another drive); Ctrl copies, Shift moves
///   files onto the Recycle Bin         → recycled (Windows asks, if you've told it to)
///   folders anywhere else              → added as stacks
///   anything onto File Explorer        → shown in File Explorer
/// </summary>
internal sealed partial class DockWindow
{
    internal enum DropKind { None, Pin, OpenWith, IntoFolder, Recycle, AddStack, ShowInExplorer }
    internal sealed record DropPlan(DropKind Kind, DockItem? Target, int InsertIndex, DragDropEffects Effects, bool Copy = false);

    private static readonly string[] AppExtensions = { ".exe", ".lnk", ".appref-ms" };

    private void SetupDrop()
    {
        _surface.AllowDrop = true;
        _surface.DragEnter += OnDropOver;
        _surface.DragOver += OnDropOver;
        _surface.DragLeave += (_, _) => ClearDropVisuals();
        _surface.Drop += OnDrop;
    }

    private static string[]? DroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    internal DropPlan PlanDrop(string[] paths, Point p, DragDropKeyStates keys)
    {
        var none = new DropPlan(DropKind.None, null, -1, DragDropEffects.None);
        if (paths.Length == 0) return none;
        _surface.LayoutItems();

        var item = _surface.HitItem(p);
        var onItem = item == null ? null : ItemDrop(item, paths, keys);
        // The middle of an icon targets the icon; the edges between icons are for inserting.
        if (onItem != null && Math.Abs(p.X - item!.CenterX) < item.Size * 0.4) return onItem;

        if (paths.All(IsApp))
            return new DropPlan(DropKind.Pin, null, InsertIndexAt(p.X), DragDropEffects.Link);
        if (paths.All(Directory.Exists))
            return new DropPlan(DropKind.AddStack, null, -1, DragDropEffects.Link);
        // Near-misses may still open files with an app, but never recycle or move them.
        return onItem is { Kind: DropKind.OpenWith or DropKind.ShowInExplorer } ? onItem : none;
    }

    private DropPlan? ItemDrop(DockItem item, string[] paths, DragDropKeyStates keys)
    {
        switch (item.Kind)
        {
            case DockItemKind.RecycleBin:
                return new DropPlan(DropKind.Recycle, item, -1, DragDropEffects.Move);
            case DockItemKind.Stack:
                var folder = item.Stack!.Config.Path;
                // Never into itself or its own subfolder.
                if (paths.Any(p => folder.StartsWith(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return null;
                bool copy = (keys & DragDropKeyStates.ControlKey) != 0 ||
                            ((keys & DragDropKeyStates.ShiftKey) == 0 && !FileOps.SameDrive(paths, folder));
                return new DropPlan(DropKind.IntoFolder, item, -1, copy ? DragDropEffects.Copy : DragDropEffects.Move, copy);
            default:
                if (item.Pin?.Permanent == true) return new DropPlan(DropKind.ShowInExplorer, item, -1, DragDropEffects.Link);
                return OpenWithExe(item) != null ? new DropPlan(DropKind.OpenWith, item, -1, DragDropEffects.Copy) : null;
        }
    }

    private static bool IsApp(string path) =>
        File.Exists(path) && AppExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The program to hand dropped files to, or null. Launcher shortcuts that carry their own arguments
    /// (Discord's Update.exe --processStart …) and Store apps are skipped.
    /// </summary>
    private static string? OpenWithExe(DockItem item)
    {
        string? exe = item.Pin != null
            ? (string.IsNullOrEmpty(item.Pin.ShortcutArguments) ? item.Pin.TargetExe : null)
            : item.Running?.ExePath;
        if (exe == null || exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : null;
    }

    /// <summary>Insert position among the apps (after File Explorer), from rest positions so it doesn't jitter.</summary>
    private int InsertIndexAt(double x)
    {
        int appCount = _surface.SeparatorIndex >= 0 ? _surface.SeparatorIndex : _surface.Items.Count;
        int first = _surface.Items.Count > 0 && _surface.Items[0].Pin?.Permanent == true ? 1 : 0;
        int insert = first;
        for (int i = first; i < appCount; i++)
            if (_surface.Items[i].RestCenterX < x) insert = i + 1;
        return insert;
    }

    private void OnDropOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = DroppedPaths(e);
        var plan = paths == null ? null : PlanDrop(paths, _surface.ToDock(e.GetPosition(_surface)), e.KeyStates);
        e.Effects = plan?.Effects ?? DragDropEffects.None;

        _surface.DropTarget = plan?.Target;
        _surface.Hovered = plan?.Target; // its label says which app or folder it'll go to
        double slot = _surface.SlotWidth;
        int insert = plan?.Kind == DropKind.Pin ? plan.InsertIndex : -1;
        for (int i = 0; i < _surface.Items.Count; i++)
            _surface.Items[i].TargetExtraBefore = i == insert ? slot : 0;
        _surface.TargetTrailingExtra = insert >= 0 && insert == _surface.Items.Count ? slot : 0;
        _surface.KeepAnimating = true;
        EnsureAnimating();
    }

    private void ClearDropVisuals()
    {
        _surface.DropTarget = null;
        _surface.Hovered = null;
        foreach (var i in _surface.Items) i.TargetExtraBefore = 0;
        _surface.TargetTrailingExtra = 0;
        _surface.KeepAnimating = false;
        EnsureAnimating();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = DroppedPaths(e);
        var plan = paths == null ? null : PlanDrop(paths, _surface.ToDock(e.GetPosition(_surface)), e.KeyStates);
        ClearDropVisuals();
        if (paths == null || plan == null || plan.Kind == DropKind.None) return;
        AppPaths.Log($"Drop: {plan.Kind} of {paths.Length} item(s) onto {plan.Target?.Name ?? "dock"}");

        switch (plan.Kind)
        {
            case DropKind.Pin:
                PinPaths(paths, plan.InsertIndex);
                break;
            case DropKind.OpenWith:
                var exe = OpenWithExe(plan.Target!)!;
                ShellOpen(exe, string.Join(" ", paths.Select(p => $"\"{p}\"")));
                if (!plan.Target!.IsRunning) { plan.Target.StartLaunchBounce(); EnsureAnimating(); }
                break;
            case DropKind.IntoFolder:
                var folder = plan.Target!.Stack!.Config.Path;
                RunFileOp(() => _ = plan.Copy ? FileOps.Copy(paths, folder, _hwnd) : FileOps.Move(paths, folder, _hwnd));
                break;
            case DropKind.Recycle:
                RunFileOp(() => FileOps.Recycle(paths, _hwnd), then: RefreshRecycleBin);
                break;
            case DropKind.AddStack:
                foreach (var dir in paths.Where(d => _config.Stacks.All(s => !string.Equals(s.Path, d, StringComparison.OrdinalIgnoreCase))))
                    _config.Stacks.Add(new StackConfig { Path = dir });
                _config.Save();
                RebuildItems();
                break;
            case DropKind.ShowInExplorer:
                foreach (var p in paths)
                {
                    if (Directory.Exists(p)) ShellOpen(p);
                    else Process.Start("explorer.exe", $"/select,\"{p}\"");
                }
                break;
        }
    }

    /// <summary>
    /// Shell file operations show their own progress and conflict dialogs and can take a while; run them
    /// on their own STA thread so the dock (and the app the files were dragged from) stay responsive.
    /// </summary>
    private void RunFileOp(Action op, Action? then = null)
    {
        var t = new Thread(() =>
        {
            try { op(); }
            catch (Exception ex) { AppPaths.Log($"File operation failed: {ex.Message}"); }
            if (then != null) Dispatcher.BeginInvoke(then);
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    private void PinPaths(IEnumerable<string> paths, int insertIndex)
    {
        // Surface index → pin-list index: count the pinned items before the drop point.
        int at = _surface.Items.Take(Math.Max(0, insertIndex)).Count(i => i.Pin != null);
        at = Math.Clamp(at, 1, _config.Pinned.Count); // File Explorer stays first
        foreach (var path in paths)
        {
            var pin = PinFromPath(path);
            bool duplicate = _config.Pinned.Any(p =>
                string.Equals(p.LaunchPath, pin.LaunchPath, StringComparison.OrdinalIgnoreCase) ||
                (p.TargetExe != null && pin.TargetExe != null && string.IsNullOrEmpty(pin.ShortcutArguments) &&
                 string.Equals(ShortcutResolver.NormalizePath(p.TargetExe), ShortcutResolver.NormalizePath(pin.TargetExe), StringComparison.OrdinalIgnoreCase)));
            if (duplicate) continue;
            _config.Pinned.Insert(at++, pin);
        }
        _config.Save();
        RebuildItems();
    }

    internal static PinnedItem PinFromPath(string path)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var info = ShortcutResolver.Resolve(path);
            return new PinnedItem
            {
                Name = DockConfig.FriendlyName(path), LaunchPath = path, TargetExe = info.TargetPath,
                ShortcutArguments = info.Arguments, Aumid = info.Aumid
            };
        }
        string name = DockConfig.FriendlyName(path);
        try
        {
            var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
        }
        catch { }
        return new PinnedItem
        {
            Name = name, LaunchPath = path,
            TargetExe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null
        };
    }
}
