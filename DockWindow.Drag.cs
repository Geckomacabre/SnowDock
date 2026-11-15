using System.Windows;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

/// <summary>
/// Drag to reorder, Mac style: the icon lifts off and follows the cursor, the other icons part to show
/// where it will land, and dragging it off the dock removes it with a puff of smoke.
///
/// The dock window never activates, so Windows won't give it mouse capture outside its own pixels.
/// The drag is therefore driven by polling the cursor and button state every frame.
/// </summary>
internal sealed partial class DockWindow
{
    private sealed class DragState
    {
        public required DockItem Item { get; init; }
        public required POINT StartPx { get; init; }
        public DateTime PressedAt { get; init; } = DateTime.UtcNow;
        public bool CanDrag { get; init; } = true;
        public bool Started;
        public Vector GrabOffsetPx;
        public bool OverDock = true;
        public int InsertIndex = -1;
        /// <summary>Width of the gap the item needs: its own width plus spacing.</summary>
        public double Slot;
    }

    private DragState? _drag;
    private DragGhost? _ghost;
    private const double DragThresholdDip = 5;

    private bool IsDragging => _drag?.Started == true;

    /// <summary>File Explorer (the Finder's stand-in) and the Recycle Bin stay put, as on the Mac.</summary>
    private static bool CanDrag(DockItem item) =>
        (item.Kind == DockItemKind.App && item.Pin?.Permanent != true) || item.Kind == DockItemKind.Widget;

    /// <summary>Hold this long without moving to open Dock Exposé for a running app.</summary>
    private const double HoldSeconds = 0.55;

    /// <summary>
    /// Track a press: movement turns it into a drag (if the item can move), holding still on a running app
    /// opens Exposé, and a quick release is an ordinary click (handled by OnLeftUp).
    /// </summary>
    private void BeginPress(DockItem item)
    {
        bool canDrag = CanDrag(item);
        if (!canDrag && !item.IsRunning) return;
        GetCursorPos(out var p);
        _drag = new DragState { Item = item, StartPx = p, CanDrag = canDrag };
        _surface.KeepAnimating = true;
        EnsureAnimating();
    }

    /// <summary>Stop watching a press that moved but can't drag; the release may still be a click.</summary>
    private void ClearPressTracking()
    {
        _drag = null;
        _surface.KeepAnimating = false;
    }

    private void ClearPress()
    {
        if (_drag is { Started: false }) _drag = null;
        if (_drag == null) _surface.KeepAnimating = false;
    }

    /// <summary>Runs every frame while a press or drag is in progress.</summary>
    private void DragTick()
    {
        if (_drag == null || _syntheticDrag) return;
        GetCursorPos(out var p);
        bool down = PrimaryButtonDown();

        if (!_drag.Started)
        {
            if (!down) { ClearPress(); return; } // a plain click; OnLeftUp handles it
            double dx = p.X - _drag.StartPx.X, dy = p.Y - _drag.StartPx.Y;
            if (Math.Sqrt(dx * dx + dy * dy) / DpiScale < DragThresholdDip)
            {
                if (_drag.Item.IsRunning && (DateTime.UtcNow - _drag.PressedAt).TotalSeconds >= HoldSeconds)
                {
                    // Click and hold: Exposé. Swallow the press so the release is neither a click nor a drop.
                    var item = _drag.Item;
                    _drag = null;
                    _surface.KeepAnimating = false;
                    _surface.Pressed = null;
                    ShowExpose(item);
                }
                return;
            }
            if (!_drag.CanDrag) { ClearPressTracking(); return; }
            StartDrag(p);
            if (!IsDragging) return;
        }

        if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) { EndDrag(cancel: true); return; }
        if (!down) { EndDrag(cancel: false); return; }
        UpdateDrag(p);
    }

    private void StartDrag(POINT p)
    {
        var d = _drag!;
        var item = d.Item;
        int idx = _surface.Items.IndexOf(item);
        if (idx < 0) { _drag = null; _surface.KeepAnimating = false; return; }

        d.Started = true;
        _surface.HiddenItem = null; // a previous drop's fly-in may still be in flight
        if (!_syntheticDrag) _surface.CaptureMouse(); // keep the release away from the app underneath
        var centerPx = _surface.ScreenPoint(item.CenterX, _surface.Baseline - item.Size / 2);
        d.GrabOffsetPx = new Vector(p.X - centerPx.X, p.Y - centerPx.Y);

        _surface.Pressed = null;
        _surface.Hovered = null;
        _surface.IsHovering = false; // no magnification while dragging: keeps the drop gap under the cursor

        // Lift the icon out of the row, leaving a gap the same size so nothing jumps.
        _surface.Items.RemoveAt(idx);
        if (_surface.SeparatorIndex > idx) _surface.SeparatorIndex--;
        if (_surface.RightSeparatorIndex > idx) _surface.RightSeparatorIndex--;
        double slot = item.Width + _surface.Gap;
        d.Slot = item.RestWidth(_surface.IconSize) + _surface.Gap;
        if (idx < _surface.Items.Count) _surface.Items[idx].ExtraBefore = slot;
        else _surface.TrailingExtra = slot;

        _ghost ??= new DragGhost(Math.Max(_surface.MagnifiedSize, _surface.IconSize * 3.4) + 24);
        _ghost.Begin(item.Kind == DockItemKind.Widget ? RenderWidgetImage(item) : item.Icon, item.Size, DpiScale, centerPx);
        UpdateDrag(p);
    }

    private void UpdateDrag(POINT p)
    {
        var d = _drag!;
        _ghost!.MoveTo(new Point(p.X - d.GrabOffsetPx.X, p.Y - d.GrabOffsetPx.Y));

        var local = _surface.DockFromScreen(new Point(p.X, p.Y));
        double s = _surface.IconSize;
        d.OverDock = local.X >= _surface.ShelfLeft - s && local.X <= _surface.ShelfRight + s
                     && local.Y >= _surface.Baseline - s * 1.6 && local.Y <= _surface.CrossLength + s;

        // Faded = "letting go here takes it out of the Dock" (a running app stays until it quits).
        bool widget = d.Item.Kind == DockItemKind.Widget;
        _ghost.SetRemoving(!d.OverDock && (d.Item.Pin != null || widget));

        // Apps move among the apps; widgets among the widgets.
        int first, appCount;
        if (widget)
        {
            first = _surface.SeparatorIndex >= 0 ? _surface.SeparatorIndex : _surface.Items.Count;
            appCount = first;
            while (appCount < _surface.Items.Count && _surface.Items[appCount].Kind == DockItemKind.Widget) appCount++;
        }
        else
        {
            appCount = _surface.SeparatorIndex >= 0 ? _surface.SeparatorIndex : _surface.Items.Count;
            first = _surface.Items.Count > 0 && _surface.Items[0].Pin?.Permanent == true ? 1 : 0;
        }
        int insert = -1;
        if (d.OverDock)
        {
            insert = first;
            for (int i = first; i < appCount; i++)
                if (_surface.Items[i].RestCenterX < local.X) insert = i + 1;
        }
        d.InsertIndex = insert;

        double slot = d.Slot;
        for (int i = 0; i < _surface.Items.Count; i++)
            _surface.Items[i].TargetExtraBefore = i == insert ? slot : 0;
        _surface.TargetTrailingExtra = insert >= 0 && insert == _surface.Items.Count ? slot : 0;
    }

    private void EndDrag(bool cancel)
    {
        var d = _drag;
        _drag = null;
        _surface.KeepAnimating = false;
        if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();
        if (d is not { Started: true }) return;

        var item = d.Item;
        if (item.Kind == DockItemKind.Widget)
        {
            EndWidgetDrag(d, cancel);
            return;
        }
        PinnedItem? targetPin = item.Pin;
        string? runningKey = item.Running?.Key;
        bool poof = false;

        if (!cancel && d.OverDock && d.InsertIndex >= 0)
        {
            targetPin = ApplyReorder(item, d.InsertIndex) ?? targetPin;
        }
        else if (!cancel && item.Pin != null)
        {
            // Off the dock: unpin. A running app stays (now unpinned) until it quits.
            poof = !item.IsRunning;
            _config.Pinned.Remove(item.Pin);
            _config.Save();
            targetPin = null;
        }

        RebuildItems();
        _surface.ResetGaps();

        if (poof)
        {
            _ghost!.Poof();
            EnsureAnimating();
            return;
        }

        var target = _surface.Items.FirstOrDefault(i =>
            (targetPin != null && ReferenceEquals(i.Pin, targetPin)) ||
            (targetPin == null && i.Pin == null && runningKey != null && i.Running?.Key == runningKey));
        if (target == null) { _ghost!.Hide(); return; }

        // Glide into the slot, keeping the real icon hidden until the ghost arrives.
        _surface.HiddenItem = target;
        _surface.LayoutItems();
        var px = _surface.ScreenPoint(target.CenterX, _surface.Baseline - target.Size / 2);
        _ghost!.FlyTo(px, target.Size, () =>
        {
            _surface.HiddenItem = null;
            _surface.InvalidateVisual();
        });
        EnsureAnimating();
    }

    /// <summary>
    /// Rewrites the pin order from the dock's app section with <paramref name="item"/> inserted at
    /// <paramref name="insert"/>. Dropping a running, unpinned app onto the dock pins it.
    /// Returns the item's pin.
    /// </summary>
    private PinnedItem? ApplyReorder(DockItem item, int insert)
    {
        int appCount = _surface.SeparatorIndex >= 0 ? _surface.SeparatorIndex : _surface.Items.Count;
        var apps = _surface.Items.Take(appCount).ToList();
        apps.Insert(Math.Clamp(insert, 0, apps.Count), item);

        PinnedItem? result = item.Pin;
        var pins = new List<PinnedItem>();
        foreach (var it in apps)
        {
            if (it.Pin != null) pins.Add(it.Pin);
            else if (ReferenceEquals(it, item) && (result = CreatePinFor(it)) != null) pins.Add(result);
        }

        // Pins the dock didn't show (shouldn't happen, but never lose one) keep their place at the end.
        pins.AddRange(_config.Pinned.Where(p => !pins.Contains(p)));
        _config.Pinned.Clear();
        _config.Pinned.AddRange(pins);
        _config.Save();
        return result;
    }
}
