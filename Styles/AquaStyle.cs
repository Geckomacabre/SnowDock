using System.Windows;
using System.Windows.Media;

namespace SnowDock.Styles;

/// <summary>
/// The Aqua dock (10.0–10.4): a translucent white bar resting on the screen edge with rounded outer corners,
/// pinstriped in 10.0–10.1, black triangles under running apps, and names in black text outlined in white
/// (the label treatment is an approximation).
/// </summary>
internal sealed class AquaStyle : DockStyle
{
    public required string StyleId { get; init; }
    public required string StyleName { get; init; }
    public required string StyleVersions { get; init; }
    /// <summary>0 for none (Jaguar onward); otherwise the strength of the classic pinstripes.</summary>
    public byte Pinstripes { get; init; }
    public byte FillAlpha { get; init; } = 140;

    private Brush? _fill, _stripes;
    private Brush Fill => _fill ??= Vertical((C((byte)Math.Min(255, FillAlpha + 25), 255, 255, 255), 0), (C(FillAlpha, 246, 247, 250), 1));
    private static readonly Pen Rim = Line(150, 128, 130, 140, 1);
    private static readonly Pen InnerRim = Line(130, 255, 255, 255, 1);
    private static readonly Pen SeparatorDark = Line(120, 60, 60, 66, 1);
    private static readonly Pen SeparatorLight = Line(160, 255, 255, 255, 1);
    private static readonly Brush Triangle = Solid(235, 0, 0, 0);
    private static readonly Pen Outline = Frozen(new Pen(Brushes.White, 3.2) { LineJoin = PenLineJoin.Round });
    private readonly Dictionary<string, Geometry> _labelCache = new();

    public override string Id => StyleId;
    public override string Name => StyleName;
    public override string Versions => StyleVersions;

    public override double BaselineInset(double icon) => Math.Round(icon * 0.2) + 2; // room for the triangle
    public override double ShelfPadding(double icon) => Math.Round(icon * 0.18);
    public override double TopPadding(double icon) => Math.Round(icon * 0.1);

    /// <summary>Alternating light rows, tiled: one frozen brush instead of a line per row.</summary>
    private Brush Stripes => _stripes ??= Frozen(new DrawingBrush
    {
        Drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing(new SolidColorBrush(C(Pinstripes, 255, 255, 255)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))),
                new GeometryDrawing(new SolidColorBrush(C(Pinstripes, 196, 198, 206)), null, new RectangleGeometry(new Rect(0, 1, 4, 1)))
            }
        },
        TileMode = TileMode.Tile, Stretch = Stretch.None,
        Viewport = new Rect(0, 0, 4, 2), ViewportUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, 4, 2), ViewboxUnits = BrushMappingMode.Absolute
    });

    public override void DrawBackground(DrawingContext dc, DockSurface s)
    {
        double icon = s.IconSize, r = Math.Round(icon * 0.18);
        double top = s.Baseline - icon - TopPadding(icon);
        // Runs past the screen edge so only the outer corners show rounded.
        var bar = new Rect(s.ShelfLeft, top, s.ShelfRight - s.ShelfLeft, s.CrossLength + r + 2 - top);
        dc.DrawRoundedRectangle(Fill, Rim, bar, r, r);
        if (Pinstripes > 0) dc.DrawRoundedRectangle(Stripes, null, bar, r, r);
        var inner = bar;
        inner.Inflate(-1, -1);
        dc.DrawRoundedRectangle(null, InnerRim, inner, r - 1, r - 1);

        if (!double.IsNaN(s.SeparatorX))
        {
            double x = Math.Round(s.SeparatorX) + 0.5;
            dc.DrawLine(SeparatorDark, new Point(x, top + 5), new Point(x, s.CrossLength));
            dc.DrawLine(SeparatorLight, new Point(x + 1, top + 5), new Point(x + 1, s.CrossLength));
        }
    }

    /// <summary>A small black triangle pointing at the icon.</summary>
    public override void DrawIndicator(DrawingContext dc, DockSurface s, DockItem item)
    {
        double w = Math.Max(6, s.IconSize * 0.14), h = w * 0.62;
        double cy = s.CrossLength - BaselineInset(s.IconSize) / 2 + 1;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(item.CenterX, cy - h / 2), true, true);
            c.LineTo(new Point(item.CenterX + w / 2, cy + h / 2), false, false);
            c.LineTo(new Point(item.CenterX - w / 2, cy + h / 2), false, false);
        }
        dc.DrawGeometry(Triangle, null, g);
    }

    public override Typeface LabelFace => new(ClassicFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    public override double LabelSize => 14;
    public override Brush LabelForeground => Brushes.Black;
    public override Thickness LabelPadding => new(3, 1, 3, 1);

    /// <summary>No box: black text with a white outline, readable on any desktop.</summary>
    public override void DrawLabel(DrawingContext dc, FormattedText text, Rect box, bool withTail)
    {
        if (!_labelCache.TryGetValue(text.Text, out var geometry))
        {
            if (_labelCache.Count > 64) _labelCache.Clear();
            geometry = text.BuildGeometry(new Point(0, 0));
            geometry.Freeze();
            _labelCache[text.Text] = geometry;
        }
        dc.PushTransform(new TranslateTransform(box.X + LabelPadding.Left, box.Y + LabelPadding.Top));
        dc.DrawGeometry(null, Outline, geometry);
        dc.DrawGeometry(Brushes.Black, null, geometry);
        dc.Pop();
    }
}
