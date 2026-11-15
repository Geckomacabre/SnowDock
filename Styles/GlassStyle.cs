using System.Windows;
using System.Windows.Media;

namespace SnowDock.Styles;

/// <summary>
/// Tahoe's Liquid Glass (26): a clear, floating, very rounded slab with a bright rim catching light on its
/// edges and a soft sheen across the top. Real Liquid Glass bends and blurs what's behind it; a layered window
/// can't, so this is the rim-and-sheen impression of it (an approximation).
/// </summary>
internal sealed class GlassStyle : DockStyle
{
    public required string StyleId { get; init; }
    public required string StyleName { get; init; }
    public required string StyleVersions { get; init; }
    public bool Dark { get; init; }

    public override string Id => StyleId;
    public override string Name => StyleName;
    public override string Versions => StyleVersions;
    public override bool DarkMenus => Dark;

    private static readonly Brush LightFill = Vertical((C(78, 255, 255, 255), 0), (C(40, 255, 255, 255), 1));
    private static readonly Brush DarkFill = Vertical((C(110, 34, 34, 40), 0), (C(130, 18, 18, 22), 1));
    private static readonly Brush Sheen = Vertical((C(85, 255, 255, 255), 0), (C(18, 255, 255, 255), 0.55), (C(0, 255, 255, 255), 1));
    /// <summary>Light catching the top-left and bottom-right edges of the glass.</summary>
    private static readonly Pen RimLight = Frozen(new Pen(Frozen(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(C(240, 255, 255, 255), 0), new GradientStop(C(70, 255, 255, 255), 0.35),
            new GradientStop(C(45, 255, 255, 255), 0.65), new GradientStop(C(190, 255, 255, 255), 1)
        }
    }), 1.4));
    private static readonly Pen LightHairline = Line(45, 0, 0, 0, 1), DarkHairline = Line(110, 0, 0, 0, 1);
    private static readonly Brush Shadow1 = Solid(24, 0, 0, 0), Shadow2 = Solid(12, 0, 0, 0);
    private static readonly Brush LightDot = Solid(220, 28, 28, 30), DarkDot = Solid(235, 242, 242, 246);
    private static readonly Pen DotHalo = Line(110, 255, 255, 255, 1);
    private static readonly Pen LightSeparator = Line(70, 0, 0, 0, 1), DarkSeparator = Line(90, 255, 255, 255, 1);
    private static readonly Brush LightLabel = Solid(190, 250, 250, 252), DarkLabel = Solid(200, 40, 40, 46);

    private static double FloatGap(double icon) => Math.Round(icon * 0.12);
    public override double BaselineInset(double icon) => FloatGap(icon) + Math.Round(icon * 0.12) + 4;
    public override double ShelfPadding(double icon) => Math.Round(icon * 0.2);
    public override double TopPadding(double icon) => Math.Round(icon * 0.12);

    private Rect Slab(DockSurface s, out double radius)
    {
        double icon = s.IconSize, top = s.Baseline - icon - TopPadding(icon), bottom = s.CrossLength - FloatGap(icon);
        radius = Math.Min((bottom - top) / 2, icon * 0.45);
        return new Rect(s.ShelfLeft, top, s.ShelfRight - s.ShelfLeft, bottom - top);
    }

    public override void DrawBackground(DrawingContext dc, DockSurface s)
    {
        var rect = Slab(s, out double r);
        var sh = rect; sh.Offset(0, 3); sh.Inflate(2, 1);
        dc.DrawRoundedRectangle(Shadow1, null, sh, r + 2, r + 2);
        sh.Offset(0, 2); sh.Inflate(2, 2);
        dc.DrawRoundedRectangle(Shadow2, null, sh, r + 4, r + 4);

        var outer = rect; outer.Inflate(0.5, 0.5);
        dc.DrawRoundedRectangle(Dark ? DarkFill : LightFill, Dark ? DarkHairline : LightHairline, outer, r, r);
        // Sheen over the top half, clipped to the slab.
        dc.PushClip(new RectangleGeometry(rect, r, r));
        dc.DrawRectangle(Sheen, null, new Rect(rect.X, rect.Y, rect.Width, rect.Height * 0.6));
        dc.Pop();
        var rim = rect; rim.Inflate(-0.7, -0.7);
        dc.DrawRoundedRectangle(null, RimLight, rim, r - 0.7, r - 0.7);

        if (!double.IsNaN(s.SeparatorX))
        {
            double x = Math.Round(s.SeparatorX) + 0.5, inset = r * 0.55;
            dc.DrawLine(Dark ? DarkSeparator : LightSeparator, new Point(x, rect.Top + inset), new Point(x, rect.Bottom - inset));
        }
    }

    public override void DrawIndicator(DrawingContext dc, DockSurface s, DockItem item)
    {
        var rect = Slab(s, out _);
        double y = (s.Baseline + rect.Bottom) / 2, r = Math.Max(2, s.IconSize * 0.045);
        dc.DrawEllipse(Dark ? DarkDot : LightDot, Dark ? null : DotHalo, new Point(item.CenterX, y), r, r);
    }

    public override Typeface LabelFace => new(ModernFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public override double LabelSize => 13;
    public override Brush LabelForeground => Dark ? Brushes.White : Brushes.Black;
    public override Thickness LabelPadding => new(12, 4, 12, 5);

    public override void DrawLabel(DrawingContext dc, FormattedText text, Rect box, bool withTail)
    {
        dc.DrawRoundedRectangle(Dark ? DarkLabel : LightLabel, RimLight, box, box.Height / 2, box.Height / 2);
        DrawText(dc, text, box);
    }
}
