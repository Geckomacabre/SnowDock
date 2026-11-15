using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnowDock;

internal sealed partial class DockSurface
{
    private static readonly Brush HitBrush = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    private static readonly Brush FloorBrush = Frozen(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(120, 196, 202, 212), 0.0),
            new GradientStop(Color.FromArgb(150, 222, 226, 234), 0.55),
            new GradientStop(Color.FromArgb(185, 240, 242, 247), 1.0)
        }
    });

    private static readonly Brush LipBrush = Frozen(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(215, 178, 183, 192), 0.0),
            new GradientStop(Color.FromArgb(230, 112, 117, 128), 1.0)
        }
    });

    private static readonly Pen FrontHighlight = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), 1.2));
    private static readonly Pen BackEdge = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), 1.0));
    private static readonly Pen SideEdge = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1.0));
    private static readonly Pen LipBottom = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 40, 42, 48)), 1.0));
    private static readonly Brush SeparatorBrush = Frozen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)));
    private static readonly Brush PressShade = Frozen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)));

    private static readonly Brush IndicatorBrush = Frozen(new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0),
            new GradientStop(Color.FromArgb(235, 190, 225, 255), 0.25),
            new GradientStop(Color.FromArgb(120, 110, 175, 255), 0.55),
            new GradientStop(Color.FromArgb(0, 90, 160, 255), 1.0)
        }
    });

    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromArgb(170, 22, 22, 24)));
    private static readonly Typeface LabelFace =
        new(new FontFamily("Lucida Sans Unicode, Lucida Grande, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

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
        double h = CrossLength, yF = YFront, yB = YBack, s = IconSize;
        double maxSize = Items.Max(i => i.Size);

        // Invisible (alpha 1) hit area: a layered window passes clicks through fully transparent pixels.
        double hitTop = IsHovering ? Baseline - maxSize - 6 : Baseline - s - 2;
        dc.DrawRectangle(HitBrush, null, new Rect(ShelfLeft, hitTop, ShelfRight - ShelfLeft, h - hitTop));

        if (IsSide) DrawSidePanel(dc);
        else DrawShelf(dc, h, yF, yB, s);

        // ---- running-app lights ----
        foreach (var item in Items.Where(i => i.IsRunning && !ReferenceEquals(i, HiddenItem)))
        {
            double rx = Math.Max(5, s * 0.15), ry = rx * 0.6;
            dc.DrawEllipse(IndicatorBrush, null, new Point(item.CenterX, yF - FloorDepth * 0.16), rx, ry);
        }

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

    private static readonly Brush SidePanelBrush = Frozen(new SolidColorBrush(Color.FromArgb(160, 32, 32, 36)));
    private static readonly Pen SidePanelPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1));
    private static readonly Pen SideSeparatorPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), 1.2)
        { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) });

    /// <summary>
    /// Side docks are the flat 2D dock: a dark translucent panel with a light rim. It runs past the screen edge
    /// so only its inner corners show rounded.
    /// </summary>
    private void DrawSidePanel(DrawingContext dc)
    {
        double top = Baseline - IconSize - 9;
        var panel = new Rect(ShelfLeft, top, ShelfRight - ShelfLeft, CrossLength + 12 - top);
        dc.DrawRoundedRectangle(SidePanelBrush, SidePanelPen, panel, 9, 9);
        if (!double.IsNaN(SeparatorX))
            dc.DrawLine(SideSeparatorPen, new Point(SeparatorX, top + 6), new Point(SeparatorX, CrossLength));
    }

    private void DrawShelf(DrawingContext dc, double h, double yF, double yB, double s)
    {
        // ---- the glass floor, in perspective ----
        double inset = FloorDepth * 0.95;
        var floor = Trapezoid(ShelfLeft, ShelfRight, yF, yB, inset);
        dc.DrawGeometry(FloorBrush, null, floor);
        dc.DrawLine(BackEdge, new Point(ShelfLeft + inset, yB + 0.5), new Point(ShelfRight - inset, yB + 0.5));
        dc.DrawLine(SideEdge, new Point(ShelfLeft, yF), new Point(ShelfLeft + inset, yB));
        dc.DrawLine(SideEdge, new Point(ShelfRight, yF), new Point(ShelfRight - inset, yB));

        // Reflections on the glass, mirrored around the baseline.
        dc.PushClip(floor);
        dc.PushOpacity(0.6);
        foreach (var item in Items)
        {
            if (ReferenceEquals(item, HiddenItem)) continue;
            double lift = item.BounceOffset() * s;
            dc.DrawImage(item.Reflection,new Rect(item.CenterX - item.Size / 2, Baseline + lift, item.Size, item.Size));
        }
        dc.Pop();
        dc.Pop();

        // Front face of the slab.
        dc.DrawRectangle(LipBrush, null, new Rect(ShelfLeft, yF, ShelfRight - ShelfLeft, h - yF));
        dc.DrawLine(LipBottom, new Point(ShelfLeft, h - 0.5), new Point(ShelfRight, h - 0.5));
        dc.DrawLine(FrontHighlight, new Point(ShelfLeft, yF), new Point(ShelfRight, yF));

        if (!double.IsNaN(SeparatorX)) DrawSeparator(dc, SeparatorX, yF, yB, inset);
    }

    private static StreamGeometry Trapezoid(double left, double right, double yFront, double yBack, double inset)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(left, yFront), true, true);
            ctx.LineTo(new Point(right, yFront), false, false);
            ctx.LineTo(new Point(right - inset, yBack), false, false);
            ctx.LineTo(new Point(left + inset, yBack), false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The dashed "crosswalk" divider, receding toward the back of the shelf.</summary>
    private void DrawSeparator(DrawingContext dc, double x, double yF, double yB, double inset)
    {
        double center = (ShelfLeft + ShelfRight) / 2;
        double half = (ShelfRight - ShelfLeft) / 2;
        double k = half <= 0 ? 1 : (half - inset) / half;
        double xBack = center + (x - center) * k;

        const int dashes = 4;
        for (int d = 0; d < dashes; d++)
        {
            double t0 = (d + 0.15) / dashes, t1 = (d + 0.75) / dashes;
            double w0 = 3.4 - 1.2 * t0, w1 = 3.4 - 1.2 * t1;
            var a = Lerp(x, yF, xBack, yB, t0);
            var b = Lerp(x, yF, xBack, yB, t1);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(a.X - w0 / 2, a.Y), true, true);
                ctx.LineTo(new Point(a.X + w0 / 2, a.Y), false, false);
                ctx.LineTo(new Point(b.X + w1 / 2, b.Y), false, false);
                ctx.LineTo(new Point(b.X - w1 / 2, b.Y), false, false);
            }
            dc.DrawGeometry(SeparatorBrush, null, g);
        }

        // The line also runs down the front face of the slab.
        dc.DrawRectangle(SeparatorBrush, null, new Rect(x - 1, yF + 1, 2, FrontLip - 1.5));
    }

    private static Point Lerp(double x0, double y0, double x1, double y1, double t) =>
        new(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);

    private void DrawLabel(DrawingContext dc, DockItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(item.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelFace, 13.5, Brushes.White, dpi);

        double padX = 11, padY = 3;
        double w = text.Width + padX * 2, hgt = text.Height + padY * 2;
        double iconTop = Baseline - item.Size - item.BounceOffset() * IconSize;

        if (IsSide)
        {
            // Beside the icon, pointing into the screen: in dock space that's "above" it, and the label's
            // upright width runs along the dock's depth.
            var center = new Point(Math.Clamp(item.CenterX, hgt / 2 + 2, MainLength - hgt / 2 - 2), Math.Max(w / 2 + 1, iconTop - 10 - w / 2));
            PushUpright(dc, center);
            var r = new Rect(center.X - w / 2, center.Y - hgt / 2, w, hgt);
            dc.DrawRoundedRectangle(LabelBrush, null, r, hgt / 2, hgt / 2);
            dc.DrawText(text, new Point(r.X + padX, r.Y + padY));
            dc.Pop();
            return;
        }

        double top = Math.Max(1, iconTop - hgt - 10);
        double left = Math.Clamp(item.CenterX - w / 2, 2, MainLength - w - 2);
        dc.DrawRoundedRectangle(LabelBrush, null, new Rect(left, top, w, hgt), hgt / 2, hgt / 2);
        dc.DrawText(text, new Point(left + padX, top + padY));
    }
}
