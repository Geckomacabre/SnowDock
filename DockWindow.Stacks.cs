using System.Windows;
using System.Windows.Controls;
using SnowDock.Native;
using SnowDock.Stacks;

namespace SnowDock;

/// <summary>Folder stacks to the right of the separator, and the fan that opens from them.</summary>
internal sealed partial class DockWindow
{
    private readonly Dictionary<StackConfig, DockItem> _stackItems = new();
    private FanWindow? _fan;
    /// <summary>The window that had focus before the fan took it, to hand back on Esc or toggle-close.</summary>
    private IntPtr _focusBeforeFan;

    /// <summary>Dock items for the configured stacks, creating and retiring folder watchers as needed.</summary>
    private IEnumerable<DockItem> StackItems()
    {
        foreach (var gone in _stackItems.Keys.Where(c => !_config.Stacks.Contains(c)).ToList())
        {
            _stackItems[gone].Stack?.Dispose();
            _stackItems.Remove(gone);
        }

        foreach (var cfg in _config.Stacks)
        {
            if (!_stackItems.TryGetValue(cfg, out var item))
            {
                var stack = new StackFolder(cfg);
                item = new DockItem { Kind = DockItemKind.Stack, Stack = stack, Name = cfg.DisplayName };
                item.SetIcon(stack.Icon);
                var captured = item;
                stack.Changed += () =>
                {
                    captured.SetIcon(stack.Icon);
                    _surface.InvalidateVisual();
                };
                _stackItems[cfg] = item;
            }
            yield return item;
        }
    }

    /// <summary>Click on a stack: open it as a fan, grid or list (or close it if it's already open).</summary>
    private void OpenStack(DockItem item)
    {
        var stack = item.Stack!;
        if (_fan is { IsOpen: true } && ReferenceEquals(_fan.Stack, stack)) { _fan.CloseFan(returnFocus: true); return; }
        if (_grid is { IsOpen: true } && ReferenceEquals(_grid.Stack, stack)) { _grid.CloseGrid(returnFocus: true); return; }
        if (!stack.Exists)
        {
            AppPaths.Log($"Stack folder missing: {stack.Config.Path}");
            return;
        }

        stack.Refresh();
        var basePx = _surface.ScreenPoint(item.CenterX, _surface.Baseline - _surface.IconSize - 4);
        double tile = Math.Clamp(_surface.IconSize * 1.05, 44, 64);
        var fg = Native.NativeMethods.GetForegroundWindow();
        if (fg != _fan?.Handle && fg != _grid?.Handle) _focusBeforeFan = fg; // switching stacks keeps the original owner

        var view = stack.Config.View;
        if (view == StackView.Automatic)
            view = stack.TotalCount <= FanCapacity(basePx.Y, tile) ? StackView.Fan : StackView.Grid;
        // Fans and the grid's pointer only work from a bottom dock; side docks open stacks as a list.
        if (_config.Edge != DockEdge.Bottom) view = StackView.List;

        if (view != StackView.Fan) _fan?.CloseFan();
        if (view != StackView.Grid) _grid?.CloseGrid();

        _surface.LabelSuppressedFor = item;
        _surface.InvalidateVisual();
        switch (view)
        {
            case StackView.Grid:
                _grid ??= CreateGrid();
                _grid.Open(stack, basePx, DpiScale, TargetMonitor);
                break;
            case StackView.List:
                ShowStackList(item);
                break;
            default:
                _fan ??= CreateFan();
                _fan.Open(stack, _config.FanMaxItems, tile, basePx, DpiScale, TargetMonitor.Bounds.Top);
                break;
        }
    }

    /// <summary>How many items a fan can show above this point (same rule as FanWindow.Open).</summary>
    private int FanCapacity(double baseYPx, double tile)
    {
        double available = (baseYPx - TargetMonitor.Bounds.Top) / DpiScale - FanSurface.LabelMaxWidth * 0.3 - tile;
        return Math.Min(_config.FanMaxItems, Math.Max(1, (int)(available / (tile + 12)) - 1));
    }

    private FanWindow CreateFan()
    {
        var fan = new FanWindow();
        fan.FanClosed += OnStackPopupClosed;
        return fan;
    }

    private GridWindow CreateGrid()
    {
        var grid = new GridWindow();
        grid.GridClosed += OnStackPopupClosed;
        return grid;
    }

    private void OnStackPopupClosed(bool returnFocus)
    {
        if (_fan?.IsOpen == true || _grid?.IsOpen == true) return; // another stack view took over
        _surface.LabelSuppressedFor = null;
        _surface.InvalidateVisual();
        // Closed by Esc or a second click on the stack: give focus back to the app the user was in.
        // (A click elsewhere or an opened file has already put focus where it belongs.)
        var previous = _focusBeforeFan;
        _focusBeforeFan = IntPtr.Zero;
        if (returnFocus && previous != IntPtr.Zero && Native.NativeMethods.IsWindow(previous))
            ForceForeground(previous);
    }

    private void ShowStackMenu(DockItem item)
    {
        var stack = item.Stack!;
        var cfg = stack.Config;
        var menu = NewMenu();

        void Option<T>(string header, T value, T current, Action<T> set) where T : struct
        {
            var mi = Add(menu, header, () =>
            {
                set(value);
                _config.Save();
                stack.Refresh();
            });
            mi.IsChecked = EqualityComparer<T>.Default.Equals(value, current);
        }

        Option("Sort by Name", StackSort.Name, cfg.Sort, v => cfg.Sort = v);
        Option("Sort by Date Added", StackSort.DateAdded, cfg.Sort, v => cfg.Sort = v);
        Option("Sort by Date Modified", StackSort.DateModified, cfg.Sort, v => cfg.Sort = v);
        Option("Sort by Kind", StackSort.Kind, cfg.Sort, v => cfg.Sort = v);
        Sep(menu);
        Option("Display as Folder", StackDisplay.Folder, cfg.Display, v => cfg.Display = v);
        Option("Display as Stack", StackDisplay.Stack, cfg.Display, v => cfg.Display = v);
        Sep(menu);
        Option("View as Fan", StackView.Fan, cfg.View, v => cfg.View = v);
        Option("View as Grid", StackView.Grid, cfg.View, v => cfg.View = v);
        Option("View as List", StackView.List, cfg.View, v => cfg.View = v);
        Option("View Automatically", StackView.Automatic, cfg.View, v => cfg.View = v);
        Sep(menu);
        Add(menu, $"Open “{cfg.DisplayName}”", () => ShellOpen(cfg.Path));
        Add(menu, "Remove from Dock", () =>
        {
            if (_fan?.Stack == stack) _fan.CloseFan();
            _config.Stacks.Remove(cfg);
            _config.Save();
            RebuildItems();
        });
        Open(menu, item);
    }

    private const int ListMaxItems = 200;

    /// <summary>List view: a dark menu of the folder's items; folders open as submenus, loaded on demand.</summary>
    private void ShowStackList(DockItem item)
    {
        var stack = item.Stack!;
        LazyIcon.NewBatch();
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("DockListMenu") };
        FillList(menu.Items, stack.Config.Path, stack.Config.Sort);
        menu.Closed += (_, _) => OnStackPopupClosed(false);
        Open(menu, item);
    }

    private void FillList(ItemCollection items, string folder, StackSort sort)
    {
        var style = (Style)Application.Current.FindResource("DockListMenuItem");
        items.Clear();
        items.Add(ListItem($"Open in File Explorer", null, style, () => ShellOpen(folder)));
        items.Add(new Separator { Style = (Style)Application.Current.FindResource("DockMenuSeparator") });

        var (entries, total) = StackFolder.Enumerate(folder, sort, ListMaxItems);
        foreach (var entry in entries)
        {
            var mi = ListItem(entry.Name, entry, style, entry.IsFolder ? null : () => ShellOpen(entry.Path));
            if (entry.IsFolder)
            {
                // A placeholder child makes it a submenu; the real contents load the first time it opens.
                mi.Items.Add(new MenuItem { Header = "…", Style = style, IsEnabled = false });
                bool loaded = false;
                mi.SubmenuOpened += (_, e) =>
                {
                    if (loaded || !ReferenceEquals(e.OriginalSource, mi)) return;
                    loaded = true;
                    FillList(mi.Items, entry.Path, sort);
                };
            }
            items.Add(mi);
        }
        if (total > entries.Count)
            items.Add(ListItem($"{total - entries.Count} More in File Explorer", null, style, () => ShellOpen(folder)));
        if (entries.Count == 0)
            items.Add(new MenuItem { Header = "Empty", Style = style, IsEnabled = false });
    }

    private static MenuItem ListItem(string header, StackEntry? entry, Style style, Action? onClick)
    {
        var mi = new MenuItem { Header = header, Style = style, ItemContainerStyle = style };
        if (entry != null)
        {
            var image = new System.Windows.Controls.Image { Width = 18, Height = 18 };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
            LazyIcon.Load(entry, img => image.Source = img);
            mi.Icon = image;
        }
        if (onClick != null)
            mi.Click += (_, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, mi)) return;
                try { onClick(); }
                catch (Exception ex) { AppPaths.Log($"List item '{header}' failed: {ex.Message}"); }
            };
        return mi;
    }

    private void AddStack()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to add to the Dock" };
        if (dlg.ShowDialog() != true) return;
        if (_config.Stacks.Any(s => string.Equals(s.Path, dlg.FolderName, StringComparison.OrdinalIgnoreCase))) return;
        _config.Stacks.Add(new StackConfig { Path = dlg.FolderName });
        _config.Save();
        RebuildItems();
    }

    private void DisposeStacks()
    {
        foreach (var item in _stackItems.Values) item.Stack?.Dispose();
        _stackItems.Clear();
    }
}
