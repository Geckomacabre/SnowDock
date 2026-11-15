using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnowDock;

/// <summary>
/// Drawing shared by every style: the invisible hit area, icons (with bounce and press shading) and label
/// placement. The style draws the background, indicators and label boxes.
/// </summary>
internal sealed partial class DockSurface
{
    private static readonly Brush HitBrush = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
    private static readonly Brush PressShade = Frozen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Items.Count == 0) return;
        LayoutItems();
        if (HideAmount >= 0.999) return; // fully tucked away: draw nothing, so nothing catches the mouse
        dc.PushTransform(new MatrixTransform(ToWindowMatrix)); // dock space → this edge
        // Auto-hide slides the whole dock off the screen edge.
        dc.PushTransform(new TranslateTransform(0, HideAmount * CrossLength));
        RenderDock(dc);
        dc.Pop();
        dc.Pop();
    }

    private void RenderDock(DrawingContext dc)
    {
        double h = CrossLength, s = IconSize;
        double maxSize = Items.Max(i => i.Size);

        // Invisible (alpha 1) hit area, all the way to the screen edge (floating styles leave a gap below the
        // dock that must still catch the mouse): a layered window passes clicks through fully transparent pixels.
        double hitTop = IsHovering ? Baseline - maxSize - 6 : Baseline - s - Look.TopPadding(s);
        dc.DrawRectangle(HitBrush, null, new Rect(ShelfLeft, hitTop, ShelfRight - ShelfLeft, h - hitTop));

        Look.DrawBackground(dc, this);

        foreach (var item in Items.Where(i => i.IsRunning && !ReferenceEquals(i, HiddenItem)))
            Look.DrawIndicator(dc, this, item);

        // ---- icons and widgets (kept upright on side and top docks) ----
        foreach (var item in Items)
        {
            if (ReferenceEquals(item, HiddenItem)) continue;
            if (item.Kind == DockItemKind.Widget)
            {
                DrawWidget(dc, item);
                continue;
            }
            double lift = item.BounceOffset() * s;
            var rect = new Rect(item.CenterX - item.Size / 2, Baseline - item.Size - lift, item.Size, item.Size);
            PushUpright(dc, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            dc.DrawImage(item.Icon, rect);
            if (ReferenceEquals(item, Pressed) || ReferenceEquals(item, DropTarget))
            {
                // Darken only the icon's own pixels, like the pressed state on the Mac dock.
                dc.PushOpacityMask(new ImageBrush(item.Icon));
                dc.DrawRectangle(PressShade, null, rect);
                dc.Pop();
            }
            dc.Pop();
        }

        if (Hovered != null && !ReferenceEquals(Hovered, LabelSuppressedFor)) DrawLabel(dc, Hovered);
        if (UpdatePill != null) DrawUpdatePill(dc);
    }

    /// <summary>"Update to x.y.z" button shown above the dock's right end when a new release is out.</summary>
    public string? UpdatePill { get; set; }
    private Rect _updatePillRect = Rect.Empty;
    private static readonly Brush PillFill = Frozen(new SolidColorBrush(Color.FromRgb(0x3B, 0x8B, 0xF6)));

    private void DrawUpdatePill(DrawingContext dc)
    {
        var text = new FormattedText(UpdatePill!, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Widgets.WidgetPaint.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double w = text.Width + 22, h = text.Height + 8;
        var r = new Rect(ShelfRight - w - 6, Baseline - IconSize - Look.TopPadding(IconSize) - h - 6, w, h);
        _updatePillRect = r;
        PushUpright(dc, new Point(r.X + w / 2, r.Y + h / 2));
        dc.DrawRoundedRectangle(PillFill, null, r, h / 2, h / 2);
        dc.DrawText(text, new Point(r.X + 11, r.Y + 4));
        dc.Pop();
    }

    public bool HitsUpdatePill(Point dockPoint) => UpdatePill != null && _updatePillRect.Contains(dockPoint);

    /// <summary>The picker is open: widgets show a remove badge.</summary>
    public bool EditMode { get; set; }

    private Widgets.WidgetPaint? _paint;
    private (bool Light, double Dpi) _paintKey;

    /// <summary>Widget colours for the current style (light tiles on light docks).</summary>
    public Widgets.WidgetPaint Paint
    {
        get
        {
            var key = (Look.LightTiles, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (_paint == null || key != _paintKey) (_paint, _paintKey) = (Widgets.WidgetPaint.For(key.LightTiles, key.PixelsPerDip), key);
            return _paint;
        }
    }

    private static readonly Brush BadgeFill = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x60)));
    private static readonly Pen BadgeRim = Frozen(new Pen(Brushes.White, 1.4));
    private static readonly Pen BadgeMinus = Frozen(new Pen(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    public Rect WidgetRect(DockItem item) =>
        new(item.CenterX - item.Width / 2, Baseline - item.Size - item.BounceOffset() * IconSize, item.Width, item.Size);

    /// <summary>The remove badge's centre in dock space (its top-left corner, as seen on screen).</summary>
    private Point BadgeCenter(DockItem item)
    {
        var r = WidgetRect(item);
        // On a top dock the tile is drawn flipped back upright, so its on-screen top-left is dock-space bottom-left.
        return Edge == DockEdge.Top ? new Point(r.X + 3, r.Bottom - 3) : new Point(r.X + 3, r.Y + 3);
    }

    public bool HitsRemoveBadge(DockItem item, Point p)
    {
        var c = BadgeCenter(item);
        return (p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y) <= 12 * 12;
    }

    private void DrawWidget(DrawingContext dc, DockItem item)
    {
        var r = WidgetRect(item);
        PushUpright(dc, new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
        try { item.Widget!.Draw(dc, r, Paint); }
        catch (Exception ex) { AppPaths.Log($"Widget {item.Widget?.Config.Type} draw failed: {ex.Message}"); }
        if (ReferenceEquals(item, Pressed)) dc.DrawRoundedRectangle(PressShade, null, r, 10, 10);
        if (EditMode)
        {
            var c = new Point(r.X + 3, r.Y + 3);
            dc.DrawEllipse(BadgeFill, BadgeRim, c, 9, 9);
            dc.DrawLine(BadgeMinus, new Point(c.X - 4.5, c.Y), new Point(c.X + 4.5, c.Y));
        }
        dc.Pop();
    }

    private void DrawLabel(DrawingContext dc, DockItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(item.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Look.LabelFace, Look.LabelSize, Look.LabelForeground, dpi);

        var pad = Look.LabelPadding;
        double w = text.Width + pad.Left + pad.Right, hgt = text.Height + pad.Top + pad.Bottom;
        double iconTop = Baseline - item.Size - item.BounceOffset() * IconSize;

        if (IsSide)
        {
            // Beside the icon, pointing into the screen: in dock space that's "above" it, and the label's
            // upright width runs along the dock's depth.
            var center = new Point(Math.Clamp(item.CenterX, hgt / 2 + 2, MainLength - hgt / 2 - 2), Math.Max(w / 2 + 1, iconTop - 10 - w / 2));
            PushUpright(dc, center);
            Look.DrawLabel(dc, text, new Rect(center.X - w / 2, center.Y - hgt / 2, w, hgt), withTail: false);
            dc.Pop();
            return;
        }

        double top = Math.Max(1, iconTop - hgt - 10 - Look.LabelTail);
        double left = Math.Clamp(item.CenterX - w / 2, 2, MainLength - w - 2);
        var box = new Rect(left, top, w, hgt);
        // A top dock is drawn flipped; turn the label back upright (its pointer would face the wrong way, so drop it).
        PushUpright(dc, new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
        Look.DrawLabel(dc, text, box, withTail: IsBottom);
        dc.Pop();
    }
}
