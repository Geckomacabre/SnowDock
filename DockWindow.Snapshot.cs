using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnowDock;

/// <summary>
/// Debug helper: render the dock to a PNG with a simulated hover, without touching the real mouse.
///   SnowDock --snapshot out.png [--hover N] [--dump-icons dir]
/// </summary>
internal sealed partial class DockWindow
{
    /// <summary>Snapshots: draw over a bright backdrop instead of the dark one.</summary>
    public static bool SnapshotLightBackdrop { get; set; }

    /// <summary>Set while a snapshot fakes a drag: the frame loop must not read the real mouse and drop it.</summary>
    private bool _syntheticDrag;

    public void SaveSnapshot(string path, int? hoverIndex, string? iconDumpDir, int? dragFrom = null, int? dragTo = null,
        bool drop = false, double hide = 0)
    {
        _surface.HideAmount = _surface.HideTarget = hide; // --hide 0..1: auto-hide part way
        _surface.LayoutItems();
        Point? ghostAt = null;
        BitmapSource? ghostIcon = null;
        if (dragFrom is int from && dragTo is int to && from >= 0 && from < _surface.Items.Count)
        {
            // Fake a drag of item `from` to just left of item `to` (indices before the drag). Never dropped,
            // so the pin order on disk is untouched.
            _syntheticDrag = true;
            var item = _surface.Items[from];
            var targetItem = _surface.Items[Math.Clamp(to, 0, _surface.Items.Count - 1)];
            var startPx = _surface.ScreenPoint(item.CenterX, _surface.Baseline - item.Size / 2);
            _drag = new DragState { Item = item, StartPx = new Native.NativeMethods.POINT { X = (int)startPx.X, Y = (int)startPx.Y } };
            StartDrag(_drag.StartPx);
            _surface.LayoutItems();

            var dropLocal = new Point(targetItem.RestCenterX - _surface.SlotWidth * 0.45, _surface.Baseline - _surface.IconSize * 0.9);
            var dropPx = _surface.ScreenPoint(dropLocal.X, dropLocal.Y);
            UpdateDrag(new Native.NativeMethods.POINT { X = (int)dropPx.X, Y = (int)dropPx.Y });
            for (int f = 0; f < 40; f++) { _surface.Tick(0.03); _surface.LayoutItems(); }
            _ghost?.Hide();
            // The ghost follows the cursor in window space, offset by where the icon was grabbed.
            var cursorWin = _surface.ToWindow(dropLocal);
            ghostAt = new Point(cursorWin.X - _drag.GrabOffsetPx.X / DpiScale, cursorWin.Y - _drag.GrabOffsetPx.Y / DpiScale);
            ghostIcon = item.Icon;
        }

        if (hoverIndex is int i && i >= 0 && i < _surface.Items.Count)
        {
            var item = _surface.Items[i];
            _surface.IsHovering = true;
            _surface.LastMouseX = item.RestCenterX;
            _surface.Hovered = item;
            _surface.Tick(1.0); // jump straight to full magnification
        }
        _surface.InvalidateVisual();
        _surface.UpdateLayout();

        int w = (int)Math.Ceiling(_surface.ActualWidth), h = (int)Math.Ceiling(_surface.ActualHeight);
        double cropLeft = Math.Max(0, _surface.ShelfLeft - 40);
        double cropWidth = Math.Min(_surface.MainLength - cropLeft, _surface.ShelfRight - _surface.ShelfLeft + 80);

        // A stand-in desktop so the glass has something to show through.
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
        {
            var sky = SnapshotLightBackdrop
                ? new LinearGradientBrush(Color.FromRgb(0xF2, 0xD7, 0xB8), Color.FromRgb(0x9F, 0xC7, 0xE6), 20) // bright, like a daytime wallpaper
                : new LinearGradientBrush(Color.FromRgb(0x2B, 0x4C, 0x7E), Color.FromRgb(0x8A, 0x6F, 0xA8), 20);
            dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(backdrop);
        rtb.Render(_surface);
        if (ghostAt is Point g && ghostIcon != null)
        {
            var ghost = new DrawingVisual();
            using (var dc = ghost.RenderOpen())
                dc.DrawImage(ghostIcon, new Rect(g.X - _surface.IconSize / 2, g.Y - _surface.IconSize / 2, _surface.IconSize, _surface.IconSize));
            rtb.Render(ghost);
        }
        // --drop really drops (rewrites the pin order on disk); otherwise the fake drag is discarded.
        if (drop && _drag != null) EndDrag(cancel: false);
        _drag = null;
        // Crop along the dock: across for a bottom dock, down for a side dock.
        var cropped = _surface.IsSide
            ? new CroppedBitmap(rtb, new Int32Rect(0, (int)cropLeft, w, (int)Math.Min(h - cropLeft, cropWidth)))
            : new CroppedBitmap(rtb, new Int32Rect((int)cropLeft, 0, (int)cropWidth, h));

        Save(cropped, path);

        if (iconDumpDir != null)
        {
            Directory.CreateDirectory(iconDumpDir);
            for (int k = 0; k < _surface.Items.Count; k++)
            {
                var icon = _surface.Items[k].Icon;
                Save(icon, Path.Combine(iconDumpDir, $"{k:00}_{icon.PixelWidth}px.png"));
            }
        }
    }

    /// <summary>Debug: the dock with stack number <paramref name="stackOrdinal"/> fanned out, fully open.</summary>
    public void SaveFanSnapshot(string path, int stackOrdinal, int hoverRow)
    {
        _surface.LayoutItems();
        var item = _surface.Items.Where(i => i.Kind == DockItemKind.Stack).ElementAtOrDefault(stackOrdinal);
        if (item?.Stack == null) { AppPaths.Log($"Fan snapshot: no stack #{stackOrdinal}"); return; }

        double tile = Math.Clamp(_surface.IconSize * 1.05, 44, 64);
        var fan = new Stacks.FanSurface
        {
            TileSize = tile,
            Entries = item.Stack.Entries.Take(_config.FanMaxItems).ToList(),
            HoverIndex = hoverRow
        };
        fan.MoreCount = Math.Max(0, item.Stack.TotalCount - fan.Entries.Count);
        var size = Stacks.FanSurface.RequiredSize(fan.Rows, tile);
        fan.AnimT = fan.OpenDuration + 1; // fully open; must be set before layout renders it
        fan.Measure(size);
        fan.Arrange(new Rect(size));
        fan.UpdateLayout();
        _surface.LabelSuppressedFor = item;
        _surface.InvalidateVisual();
        _surface.UpdateLayout();

        int dw = (int)Math.Ceiling(_surface.ActualWidth), dh = (int)Math.Ceiling(_surface.ActualHeight);
        var dockBmp = new RenderTargetBitmap(dw, dh, 96, 96, PixelFormats.Pbgra32);
        dockBmp.Render(_surface);
        var fanBmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        fanBmp.Render(fan);

        // Same placement as the live fan: its base sits just above the stack icon.
        double fanLeft = item.CenterX - fan.OriginX;
        double fanTop = _surface.Baseline - _surface.IconSize - 4 - size.Height;
        double left = Math.Min(fanLeft, _surface.ShelfLeft) - 20, right = Math.Max(fanLeft + size.Width, _surface.ShelfRight) + 20;
        double width = right - left, height = dh - fanTop;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2B, 0x4C, 0x7E), Color.FromRgb(0x8A, 0x6F, 0xA8), 20), null,
                new Rect(0, 0, width, height));
            dc.DrawImage(dockBmp, new Rect(-left, -fanTop, dw, dh));
            dc.DrawImage(fanBmp, new Rect(fanLeft - left, 0, size.Width, size.Height));
        }
        var outBmp = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        outBmp.Render(dv);
        Save(outBmp, path);
    }

    /// <summary>Debug: stack number <paramref name="stackOrdinal"/> as a grid or as a list, rendered offscreen.</summary>
    public void SaveStackViewSnapshot(string path, int stackOrdinal, bool list)
    {
        var item = _surface.Items.Where(i => i.Kind == DockItemKind.Stack).ElementAtOrDefault(stackOrdinal);
        if (item?.Stack == null) return;
        if (!list)
        {
            new Stacks.GridWindow().SaveSnapshot(item.Stack, path);
            return;
        }

        // The list's menu items, laid out in a panel styled like the menu (popups can't render offscreen).
        Stacks.LazyIcon.NewBatch();
        var items = new ItemsControl();
        FillList(items.Items, item.Stack.Config.Path, item.Stack.Config.Sort);
        while (items.Items.Count > 16) items.Items.RemoveAt(16);
        if (items.Items[3] is MenuItem highlighted) highlighted.Background = Brushes.Transparent; // keep default look
        Stacks.LazyIcon.Flush();
        var panel = new Border
        {
            CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 5, 0, 5), Child = items, Width = 360
        };
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x6A)), Padding = new Thickness(20), Child = panel };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        Save(rtb, path);
    }

    /// <summary>
    /// Debug: what dropping <paramref name="paths"/> would do at the center of every icon and in every gap.
    /// Plans only; nothing is pinned, opened, copied or deleted.
    /// </summary>
    public void WriteDropPlans(string outFile, string[] paths)
    {
        _surface.LayoutItems();
        var lines = new List<string> { "Dropping: " + string.Join(", ", paths.Select(Path.GetFileName)) };
        double y = _surface.Baseline - _surface.IconSize / 2;
        for (int i = 0; i < _surface.Items.Count; i++)
        {
            var it = _surface.Items[i];
            var center = PlanDrop(paths, new Point(it.CenterX, y), DragDropKeyStates.None);
            lines.Add($"  on  [{i,2}] {it.Name,-26} -> {center.Kind}{(center.Copy ? " (copy)" : "")}");
            if (i + 1 < _surface.Items.Count)
            {
                double gapX = (it.CenterX + it.Size / 2 + _surface.Items[i + 1].CenterX - _surface.Items[i + 1].Size / 2) / 2;
                var gap = PlanDrop(paths, new Point(gapX, y), DragDropKeyStates.None);
                lines.Add($"  gap after [{i,2}]                 -> {gap.Kind}{(gap.Kind == DropKind.Pin ? $" at {gap.InsertIndex}" : "")}");
            }
        }
        File.WriteAllLines(outFile, lines);
    }

    /// <summary>Debug: sample dock menus in the dark and light themes, side by side.</summary>
    public void SaveMenuPreview(string path)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x6A)) };
        foreach (bool dark in new[] { true, false })
        {
            var items = new ItemsControl();
            var itemStyle = (Style)FindResource(dark ? "DockMenuItem" : "DockMenuItemLight");
            var sepStyle = (Style)FindResource(dark ? "DockMenuSeparator" : "DockMenuSeparatorLight");
            items.Items.Add(new MenuItem { Header = "Turn Magnification Off", Style = itemStyle });
            items.Items.Add(new MenuItem { Header = "Dock Style: Snow Leopard & Lion…", Style = itemStyle });
            items.Items.Add(new Separator { Style = sepStyle });
            items.Items.Add(new MenuItem { Header = "Position on Bottom", Style = itemStyle, IsChecked = true });
            items.Items.Add(new MenuItem { Header = "Quit SnowDock", Style = itemStyle });
            row.Children.Add(new Border
            {
                Margin = new Thickness(20), CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 5, 0, 5), Child = items,
                Background = new SolidColorBrush(dark ? Color.FromArgb(0xE0, 0x20, 0x20, 0x20) : Color.FromArgb(0xF2, 0xF6, 0xF6, 0xF8)),
                BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x60, 255, 255, 255) : Color.FromArgb(0x40, 0, 0, 0)),
                BorderThickness = new Thickness(1)
            });
        }
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        row.Arrange(new Rect(row.DesiredSize));
        row.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)row.ActualWidth, (int)row.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(row);
        Save(rtb, path);
    }

    /// <summary>
    /// Debug: switch from a shelf style to a floating one at runtime (not saved) and log the window's bounds
    /// before and after, to check it resizes against the screen edge without a gap.
    /// </summary>
    public async Task RunStyleSwitchTest(string outFile)
    {
        var lines = new List<string>();
        void Snap(string when)
        {
            Native.NativeMethods.GetWindowRect(_hwnd, out var r);
            var work = TargetMonitor.Work;
            lines.Add($"{when}: style={_surface.Look.Id} window={r} height={r.Height}px workBottom={work.Bottom} gapToWorkBottom={work.Bottom - r.Bottom}px required={ToPx(_surface.RequiredHeight)}px");
        }
        Snap("before");
        SetLook(Styles.DockStyles.BigSurLight, save: false);
        await Task.Delay(400);
        Snap("after big-sur");
        SetLook(Styles.DockStyles.Cheetah, save: false);
        await Task.Delay(400);
        Snap("after cheetah");
        File.WriteAllLines(outFile, lines);
    }

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
