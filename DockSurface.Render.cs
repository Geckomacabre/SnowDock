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

        // ---- icons (kept upright on side docks) ----
        foreach (var item in Items)
        {
            if (ReferenceEquals(item, HiddenItem)) continue;
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
        Look.DrawLabel(dc, text, new Rect(left, top, w, hgt), withTail: true);
    }
}
