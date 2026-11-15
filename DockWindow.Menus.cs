using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using SnowDock.Native;
using SnowDock.Widgets;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

internal sealed partial class DockWindow
{
    private bool _menuOpen;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    private void ShowItemMenu(DockItem item, Point? at = null)
    {
        if (item.Kind == DockItemKind.Stack)
        {
            ShowStackMenu(item);
            return;
        }
        if (item.Kind == DockItemKind.Widget)
        {
            if (at is Point p && WidgetRightClicked(item, p)) return;
            var widgetMenu = NewMenu();
            var widget = item.Widget!;
            if (widget.HasCard) Add(widgetMenu, "Open", () => ShowCard(widget));
            Add(widgetMenu, "Remove Widget", () => RemoveWidget(widget.Config));
            Sep(widgetMenu);
            Add(widgetMenu, "Widgets…", () => Dispatcher.BeginInvoke(ShowPicker));
            Open(widgetMenu, item);
            return;
        }

        var menu = NewMenu();

        if (item.Kind == DockItemKind.RecycleBin)
        {
            Add(menu, "Open", () => ShellOpen("shell:RecycleBinFolder"));
            Sep(menu);
            // Flags 0: Windows shows its own "are you sure" confirmation.
            Add(menu, "Empty Recycle Bin", () => SHEmptyRecycleBin(_hwnd, null, 0), enabled: _recycleFull);
            Open(menu, item);
            return;
        }

        if (item.IsRunning)
        {
            var fg = GetForegroundWindow();
            foreach (var w in item.Running!.Windows)
            {
                var title = WindowTracker.GetTitle(w);
                if (title.Length > 60) title = title[..57] + "…";
                var mi = Add(menu, title, () => ForceForeground(w));
                mi.IsChecked = w == fg;
            }
            Sep(menu);
        }

        bool isExplorer = item.Pin?.Permanent == true;
        if (isExplorer) Add(menu, "New File Explorer Window", () => ShellOpen("explorer.exe"));

        var keep = Add(menu, "Keep in Dock", () => SetKeepInDock(item, !item.IsPinned), enabled: !isExplorer);
        keep.IsChecked = item.IsPinned;
        if (!isExplorer) Add(menu, "Show in File Explorer", () => ShowInExplorer(item));
        Sep(menu);

        if (item.IsRunning)
        {
            var app = item.Running!;
            Add(menu, "Hide", () => { foreach (var w in app.Windows) ShowWindowAsync(w, SW_MINIMIZE); });
            if (!isExplorer || app.Windows.Count > 0)
                Add(menu, isExplorer ? "Close All Windows" : "Quit",
                    () => { foreach (var w in app.Windows) PostMessage(w, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); });
        }
        else
        {
            Add(menu, "Open", () => Launch(item));
        }

        Open(menu, item);
    }

    private void ShowDockMenu(Point p)
    {
        var menu = NewMenu();
        Add(menu, _config.Magnification ? "Turn Magnification Off" : "Turn Magnification On", () =>
        {
            _config.Magnification = !_config.Magnification;
            _surface.MagnificationEnabled = _config.Magnification;
            _config.Save();
            EnsureAnimating();
        });
        Add(menu, _config.AutoHide ? "Turn Hiding Off" : "Turn Hiding On", () => SetAutoHide(!_config.AutoHide));
        // Opened after this menu has closed, so the two never overlap.
        Add(menu, "Widgets…", () => Dispatcher.BeginInvoke(ShowPicker));
        Add(menu, $"Dock Style: {_surface.Look.Name}…", () => Dispatcher.BeginInvoke(() => ShowStyleMenu(p)));
        Sep(menu);
        foreach (var (name, icon) in new[] { ("Small", 40.0), ("Medium", 52.0), ("Large", 64.0) })
            Add(menu, $"Size: {name}", () => SetIconSize(icon)).IsChecked = Math.Abs(_config.IconSize - icon) < 0.5;
        Add(menu, "24-Hour Clock", () => SetClock24h(!WidgetEnv.Clock24h)).IsChecked = WidgetEnv.Clock24h;
        Sep(menu);
        AddPositionItems(menu);
        Sep(menu);
        var monitors = Monitors.All();
        if (monitors.Count > 1)
        {
            var current = TargetMonitor.DeviceName;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                var mi = Add(menu, $"Show on Display {i + 1} ({m.Bounds.Width}×{m.Bounds.Height}{(m.IsPrimary ? ", main" : "")})",
                    () => MoveToMonitor(m.DeviceName));
                mi.IsChecked = string.Equals(m.DeviceName, current, StringComparison.OrdinalIgnoreCase);
            }
            Sep(menu);
        }
        var startup = Add(menu, "Start SnowDock at Sign-In", () => StartupEntry.Set(!StartupEntry.IsEnabled()));
        startup.IsChecked = StartupEntry.IsEnabled();
        Add(menu, "Add Folder Stack…", AddStack);
        Add(menu, "Dock Settings…", () => ShellOpen("notepad.exe", $"\"{AppPaths.ConfigFile}\""));
        Sep(menu);
        Add(menu, _preview ? "Quit SnowDock" : "Quit SnowDock and Restore Taskbar",
            () => Application.Current.Shutdown());

        PlaceMenu(menu, p.X, _surface.Baseline - _surface.IconSize);
        ShowMenu(menu);
    }

    /// <summary>"Position on Screen": Left, Bottom, Right (as in the Mac's dock menu).</summary>
    private void AddPositionItems(ContextMenu menu)
    {
        foreach (var (edge, name) in new[] { (DockEdge.Left, "Left"), (DockEdge.Bottom, "Bottom"), (DockEdge.Right, "Right"), (DockEdge.Top, "Top") })
        {
            var mi = Add(menu, $"Position on {name}", () => SetEdge(edge));
            mi.IsChecked = _config.Edge == edge;
        }
    }

    /// <summary>
    /// Open a menu next to a dock-space point: above it on a bottom dock, beside it (toward the middle of the
    /// screen) on a side dock.
    /// </summary>
    private void PlaceMenu(ContextMenu menu, double dockX, double dockY)
    {
        menu.PlacementTarget = _surface;
        menu.Placement = PlacementMode.Custom;
        menu.PlacementRectangle = _surface.WindowRect(new Rect(dockX - 1, dockY, 2, 1));
        menu.CustomPopupPlacementCallback = _config.Edge switch
        {
            DockEdge.Left => (popup, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width + 4, target.Height / 2 - popup.Height / 2), PopupPrimaryAxis.Vertical) },
            DockEdge.Right => (popup, target, _) => new[] { new CustomPopupPlacement(new Point(-popup.Width - 4, target.Height / 2 - popup.Height / 2), PopupPrimaryAxis.Vertical) },
            DockEdge.Top => (popup, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width / 2 - popup.Width / 2, target.Height + 4), PopupPrimaryAxis.Horizontal) },
            _ => CenterAbove
        };
    }

    private void MoveToMonitor(string deviceName)
    {
        _config.Monitor = deviceName;
        _config.Save();
        _appBar?.Unregister(); // let go of the old monitor's edge before claiming the new one
        UpdateAppBar();
        PositionWindow();
        EnsureAnimating();
    }

    /// <summary>A dock menu in the current era's colours (the one place menus pick light or dark).</summary>
    private ContextMenu NewMenu() => new()
    {
        Style = (Style)Application.Current.FindResource(_surface.Look.DarkMenus ? "DockContextMenu" : "DockContextMenuLight")
    };

    private static MenuItem Add(ContextMenu menu, string header, Action onClick, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        mi.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { AppPaths.Log($"Menu action '{header}' failed: {ex.Message}"); }
        };
        menu.Items.Add(mi);
        return mi;
    }

    private void Sep(ContextMenu menu)
    {
        if (menu.Items.Count == 0 || menu.Items[^1] is Separator) return;
        menu.Items.Add(new Separator
        {
            Style = (Style)Application.Current.FindResource(_surface.Look.DarkMenus ? "DockMenuSeparator" : "DockMenuSeparatorLight")
        });
    }

    /// <summary>The style picker: every era, oldest first, the current one checked.</summary>
    private void ShowStyleMenu(Point p)
    {
        var menu = NewMenu();
        foreach (var look in Styles.DockStyles.All)
        {
            var mi = Add(menu, $"{look.Name}  ({look.Versions})", () => SetLook(look));
            mi.IsChecked = ReferenceEquals(look, _surface.Look);
        }
        PlaceMenu(menu, p.X, _surface.Baseline - _surface.IconSize);
        ShowMenu(menu);
    }

    /// <summary>Switch era. The dock's depth can change, so re-reserve the edge and re-place the window.</summary>
    private void SetLook(Styles.DockStyle look, bool save = true)
    {
        _config.Style = look.Id;
        if (save) _config.Save();
        _surface.Look = look;
        UpdateAppBar();
        PositionWindow();
        EnsureAnimating();
        AppPaths.Log($"Style: {look.Id}");
    }

    private void Open(ContextMenu menu, DockItem item)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is Separator) menu.Items.RemoveAt(menu.Items.Count - 1);
        double top = _surface.Baseline - item.Size - item.BounceOffset() * _surface.IconSize;
        PlaceMenu(menu, item.CenterX, top);
        ShowMenu(menu);
    }

    private void ShowMenu(ContextMenu menu)
    {
        _menuOpen = true;
        _surface.Hovered = null; // the label gives way to the menu
        _surface.InvalidateVisual();
        menu.Opened += (_, _) =>
        {
            // The dock never activates, so the menu wouldn't see clicks elsewhere or Escape.
            // Make the menu's popup the foreground window (allowed: we just got the click), like a tray menu.
            if (PresentationSource.FromVisual(menu) is HwndSource src) SetForegroundWindow(src.Handle);
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (!_surface.IsMouseOver)
            {
                _surface.IsHovering = false;
                _surface.Pressed = null;
            }
            EnsureAnimating();
        };
        menu.IsOpen = true;
    }

    private static CustomPopupPlacement[] CenterAbove(Size popup, Size target, Point offset) =>
        new[] { new CustomPopupPlacement(new Point(target.Width / 2 - popup.Width / 2, -popup.Height - 4), PopupPrimaryAxis.Horizontal) };
}
