using System.Windows;
using System.Windows.Media;

namespace SnowDock.Styles;

internal enum ShelfIndicator { Orb, Dot, Bar }

/// <summary>
/// The 3D glass shelf (Leopard through Mavericks): a translucent floor in perspective with icon reflections,
/// a front lip with a bright edge, and a dashed "crosswalk" separator. On the side of the screen these
/// releases switched to a flat 2D panel, and so does this style.
/// </summary>
internal sealed class ShelfStyle : DockStyle
{
    public required string StyleId { get; init; }
    public required string StyleName { get; init; }
    public required string StyleVersions { get; init; }
    public bool Dark { get; init; }

    public required Brush Floor { get; init; }
    public required Brush Lip { get; init; }
    public Pen FrontHighlight { get; init; } = Line(240, 255, 255, 255, 1.2);
    public Pen BackEdge { get; init; } = Line(170, 255, 255, 255, 1);
    public Pen SideEdge { get; init; } = Line(120, 255, 255, 255, 1);
    public Pen LipBottom { get; init; } = Line(160, 40, 42, 48, 1);
    public Brush SeparatorBrush { get; init; } = Solid(230, 255, 255, 255);
    public double ReflectionOpacity { get; init; } = 0.6;
    public ShelfIndicator Indicator { get; init; } = ShelfIndicator.Dot;
    public required Brush IndicatorBrush { get; init; }
    public double IndicatorScale { get; init; } = 1;
    public Brush SidePanel { get; init; } = Solid(160, 32, 32, 36);
    public Pen SidePanelRim { get; init; } = Line(150, 255, 255, 255, 1);
    public Brush LabelBox { get; init; } = Solid(170, 22, 22, 24);

    public Brush SideSeparatorBrush { get; init; } = Solid(170, 255, 255, 255);
    private Pen? _sideSeparator;
    private Pen SideSeparator => _sideSeparator ??= Frozen(new Pen(SideSeparatorBrush, 1.2) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) });

    public override string Id => StyleId;
    public override string Name => StyleName;
    public override string Versions => StyleVersions;
    public override bool DarkMenus => Dark;

    private const double FrontLip = 4;
    private static double FloorDepth(double icon) => Math.Round(icon * 0.48);
    public override double BaselineInset(double icon) => FrontLip + FloorDepth(icon) * 0.42;
    public override double ShelfPadding(double icon) => Math.Round(icon * 0.42);

    public override void DrawBackground(DrawingContext dc, DockSurface s)
    {
        if (s.IsSide) DrawSidePanel(dc, s);
        else DrawShelf(dc, s);
    }

    private void DrawShelf(DrawingContext dc, DockSurface s)
    {
        double h = s.CrossLength, depth = FloorDepth(s.IconSize), yF = h - FrontLip, yB = yF - depth;
        double inset = depth * 0.95;
        var floor = Trapezoid(s.ShelfLeft, s.ShelfRight, yF, yB, inset);
        dc.DrawGeometry(Floor, null, floor);
        dc.DrawLine(BackEdge, new Point(s.ShelfLeft + inset, yB + 0.5), new Point(s.ShelfRight - inset, yB + 0.5));
        dc.DrawLine(SideEdge, new Point(s.ShelfLeft, yF), new Point(s.ShelfLeft + inset, yB));
        dc.DrawLine(SideEdge, new Point(s.ShelfRight, yF), new Point(s.ShelfRight - inset, yB));

        // Reflections on the glass, mirrored around the baseline.
        dc.PushClip(floor);
        dc.PushOpacity(ReflectionOpacity);
        foreach (var item in s.Items)
        {
            if (ReferenceEquals(item, s.HiddenItem)) continue;
            double lift = item.BounceOffset() * s.IconSize;
            dc.DrawImage(item.Reflection, new Rect(item.CenterX - item.Size / 2, s.Baseline + lift, item.Size, item.Size));
        }
        dc.Pop();
        dc.Pop();

        // Front face of the slab.
        dc.DrawRectangle(Lip, null, new Rect(s.ShelfLeft, yF, s.ShelfRight - s.ShelfLeft, h - yF));
        dc.DrawLine(LipBottom, new Point(s.ShelfLeft, h - 0.5), new Point(s.ShelfRight, h - 0.5));
        dc.DrawLine(FrontHighlight, new Point(s.ShelfLeft, yF), new Point(s.ShelfRight, yF));

        if (!double.IsNaN(s.SeparatorX)) DrawCrosswalk(dc, s, s.SeparatorX, yF, yB, inset);
    }

    private void DrawSidePanel(DrawingContext dc, DockSurface s)
    {
        double top = s.Baseline - s.IconSize - 9;
        var panel = new Rect(s.ShelfLeft, top, s.ShelfRight - s.ShelfLeft, s.CrossLength + 12 - top);
        dc.DrawRoundedRectangle(SidePanel, SidePanelRim, panel, 9, 9);
        if (!double.IsNaN(s.SeparatorX))
            dc.DrawLine(SideSeparator, new Point(s.SeparatorX, top + 6), new Point(s.SeparatorX, s.CrossLength));
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
    private void DrawCrosswalk(DrawingContext dc, DockSurface s, double x, double yF, double yB, double inset)
    {
        double center = (s.ShelfLeft + s.ShelfRight) / 2, half = (s.ShelfRight - s.ShelfLeft) / 2;
        double k = half <= 0 ? 1 : (half - inset) / half;
        double xBack = center + (x - center) * k;
        const int dashes = 4;
        for (int d = 0; d < dashes; d++)
        {
            double t0 = (d + 0.15) / dashes, t1 = (d + 0.75) / dashes;
            double w0 = 3.4 - 1.2 * t0, w1 = 3.4 - 1.2 * t1;
            Point a = new(x + (xBack - x) * t0, yF + (yB - yF) * t0), b = new(x + (xBack - x) * t1, yF + (yB - yF) * t1);
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
        dc.DrawRectangle(SeparatorBrush, null, new Rect(x - 1, yF + 1, 2, FrontLip - 1.5));
    }

    public override void DrawIndicator(DrawingContext dc, DockSurface s, DockItem item)
    {
        double y = s.CrossLength - FrontLip - FloorDepth(s.IconSize) * 0.16, icon = s.IconSize;
        switch (Indicator)
        {
            case ShelfIndicator.Bar: // Mountain Lion / Mavericks: a short horizontal bar of light
                dc.DrawEllipse(IndicatorBrush, null, new Point(item.CenterX, y), icon * 0.24 * IndicatorScale, 2.6);
                break;
            default: // Leopard's blue orb, Snow Leopard's blue-white dot
                double rx = Math.Max(5, icon * 0.15) * IndicatorScale;
                dc.DrawEllipse(IndicatorBrush, null, new Point(item.CenterX, y), rx, rx * 0.6);
                break;
        }
    }

    public override void DrawLabel(DrawingContext dc, FormattedText text, Rect box, bool withTail)
    {
        dc.DrawRoundedRectangle(LabelBox, null, box, box.Height / 2, box.Height / 2);
        DrawText(dc, text, box);
    }

    // Shared builders so the registry reads simply.
    public static Brush FloorOf(Color back, Color mid, Color front) => Vertical((back, 0), (mid, 0.55), (front, 1));
    public static Brush LipOf(Color top, Color bottom) => Vertical((top, 0), (bottom, 1));
}
