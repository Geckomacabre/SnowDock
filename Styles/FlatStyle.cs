using System.Windows;
using System.Windows.Media;

namespace SnowDock.Styles;

/// <summary>
/// The flat docks. Yosemite–Catalina (10.10–10.15): a translucent bar resting on the screen edge with small
/// rounded corners and dots under running apps. Big Sur–Sequoia (11–15): the same idea floating just off the
/// edge with large rounded corners. Each comes in light and dark (the dark Dock option arrived with
/// Yosemite). The real ones blur what's behind them; a layered window can't, so these are tinted instead.
/// </summary>
internal sealed class FlatStyle : DockStyle
{
    public required string StyleId { get; init; }
    public required string StyleName { get; init; }
    public required string StyleVersions { get; init; }
    public bool Dark { get; init; }
    /// <summary>Big Sur onward: lifted off the screen edge, all corners rounded.</summary>
    public bool Floating { get; init; }
    public double RadiusFactor { get; init; } = 0.1;

    public required Brush Fill { get; init; }
    public required Pen Rim { get; init; }
    public Pen? InnerRim { get; init; }
    public required Brush Dot { get; init; }
    public required Pen Separator { get; init; }
    public required Brush LabelBox { get; init; }
    public required Pen LabelRim { get; init; }

    private static readonly Brush Shadow1 = Solid(22, 0, 0, 0), Shadow2 = Solid(12, 0, 0, 0);

    public override string Id => StyleId;
    public override string Name => StyleName;
    public override string Versions => StyleVersions;
    public override bool DarkMenus => Dark;

    private double FloatGap(double icon) => Floating ? Math.Round(icon * 0.08) : 0;
    private double DotSpace(double icon) => Math.Round(icon * 0.1) + 5;
    public override double BaselineInset(double icon) => FloatGap(icon) + DotSpace(icon);
    public override double ShelfPadding(double icon) => Math.Round(icon * (Floating ? 0.16 : 0.14));
    public override double TopPadding(double icon) => Math.Round(icon * 0.1) + (Floating ? 1 : 0);

    public override void DrawBackground(DrawingContext dc, DockSurface s)
    {
        double icon = s.IconSize, top = s.Baseline - icon - TopPadding(icon);
        double bottom = Floating ? s.CrossLength - FloatGap(icon) : s.CrossLength;
        double r = Floating ? Math.Min((bottom - top) / 2, Math.Round(icon * RadiusFactor)) : Math.Round(icon * RadiusFactor);
        // Edge-hugging docks run past the screen edge so only the outer corners show rounded.
        var rect = new Rect(s.ShelfLeft, top, s.ShelfRight - s.ShelfLeft, (Floating ? bottom : bottom + r + 2) - top);

        if (Floating)
        {
            // A soft lift shadow under the floating dock.
            var sh = rect; sh.Offset(0, 2); sh.Inflate(1, 1);
            dc.DrawRoundedRectangle(Shadow1, null, sh, r + 1, r + 1);
            sh.Offset(0, 1); sh.Inflate(1, 1);
            dc.DrawRoundedRectangle(Shadow2, null, sh, r + 2, r + 2);
        }
        dc.DrawRoundedRectangle(Fill, Rim, rect, r, r);
        if (InnerRim != null)
        {
            var inner = rect; inner.Inflate(-1, -1);
            dc.DrawRoundedRectangle(null, InnerRim, inner, Math.Max(0, r - 1), Math.Max(0, r - 1));
        }

        if (!double.IsNaN(s.SeparatorX))
        {
            double x = Math.Round(s.SeparatorX) + 0.5, inset = Math.Round(icon * 0.1);
            dc.DrawLine(Separator, new Point(x, top + inset), new Point(x, bottom - inset));
        }
    }

    /// <summary>A small dot between the icon and the bottom of the dock.</summary>
    public override void DrawIndicator(DrawingContext dc, DockSurface s, DockItem item)
    {
        double icon = s.IconSize, bottom = s.CrossLength - FloatGap(icon);
        double y = (s.Baseline + bottom) / 2 + 0.5, r = Math.Max(1.8, icon * (Floating ? 0.043 : 0.038));
        dc.DrawEllipse(Dot, null, new Point(item.CenterX, y), r, r);
    }

    public override Typeface LabelFace => new(ModernFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public override double LabelSize => 13;
    public override Brush LabelForeground => Dark ? Brushes.White : Brushes.Black;
    public override Thickness LabelPadding => new(10, 3, 10, 4);
    public override double LabelTail => 6;

    public override void DrawLabel(DrawingContext dc, FormattedText text, Rect box, bool withTail)
    {
        dc.DrawGeometry(LabelBox, LabelRim, TailedBox(box, 6, withTail ? LabelTail : 0));
        DrawText(dc, text, box);
    }
}
