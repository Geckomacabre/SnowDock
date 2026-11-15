using System.Windows;
using System.Windows.Media;

namespace SnowDock.Styles;

/// <summary>
/// One era's look for the dock: its background, running-app indicator, labels, separator and menu colour.
/// Everything is drawn in code to resemble that era; no Apple artwork is used.
///
/// Styles draw in dock space (the dock along X, screen edge at the bottom; see DockSurface.Edge.cs), so the
/// same drawing serves left, bottom and right docks. Layout reads the style's geometry through
/// <see cref="BaselineInset"/>, <see cref="ShelfPadding"/> and <see cref="TopPadding"/>.
/// </summary>
internal abstract class DockStyle
{
    public abstract string Id { get; }
    /// <summary>Name shown in the picker, e.g. "Snow Leopard &amp; Lion".</summary>
    public abstract string Name { get; }
    /// <summary>The releases this look covers, e.g. "10.6–10.7".</summary>
    public abstract string Versions { get; }
    public virtual bool DarkMenus => false;

    // ---- geometry (DIPs) ----
    /// <summary>Distance from the screen edge to the bottom of the icons.</summary>
    public abstract double BaselineInset(double icon);
    /// <summary>Background extending past the first and last icon.</summary>
    public abstract double ShelfPadding(double icon);
    /// <summary>Background above the top of the icons (counts toward the reserved screen strip).</summary>
    public virtual double TopPadding(double icon) => 6;

    // ---- drawing ----
    /// <summary>Background, separator and anything else under the icons (reflections included).</summary>
    public abstract void DrawBackground(DrawingContext dc, DockSurface s);
    public abstract void DrawIndicator(DrawingContext dc, DockSurface s, DockItem item);

    // ---- labels ----
    protected static readonly FontFamily ClassicFont = new("Lucida Sans Unicode, Lucida Grande, Segoe UI");
    protected static readonly FontFamily ModernFont = new("Segoe UI Variable Text, Segoe UI");
    public virtual Typeface LabelFace => new(ClassicFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    public virtual double LabelSize => 13.5;
    public virtual Brush LabelForeground => Brushes.White;
    public virtual Thickness LabelPadding => new(11, 3, 11, 3);
    /// <summary>Height of the little pointer under a bottom dock's label (0 for none).</summary>
    public virtual double LabelTail => 0;
    /// <summary>Draw the label in <paramref name="box"/> (upright), with its pointer below if asked.</summary>
    public abstract void DrawLabel(DrawingContext dc, FormattedText text, Rect box, bool withTail);

    protected void DrawText(DrawingContext dc, FormattedText text, Rect box) =>
        dc.DrawText(text, new Point(box.X + LabelPadding.Left, box.Y + LabelPadding.Top));

    /// <summary>A rounded label box with a small pointer centred under it.</summary>
    protected static Geometry TailedBox(Rect box, double radius, double tail)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double r = Math.Min(radius, box.Height / 2), cx = box.X + box.Width / 2;
            c.BeginFigure(new Point(box.Left + r, box.Top), true, true);
            c.LineTo(new Point(box.Right - r, box.Top), true, true);
            c.ArcTo(new Point(box.Right, box.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(box.Right, box.Bottom - r), true, true);
            c.ArcTo(new Point(box.Right - r, box.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            if (tail > 0)
            {
                c.LineTo(new Point(cx + tail, box.Bottom), true, true);
                c.LineTo(new Point(cx, box.Bottom + tail), true, true);
                c.LineTo(new Point(cx - tail, box.Bottom), true, true);
            }
            c.LineTo(new Point(box.Left + r, box.Bottom), true, true);
            c.ArcTo(new Point(box.Left, box.Bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(box.Left, box.Top + r), true, true);
            c.ArcTo(new Point(box.Left + r, box.Top), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    protected static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    protected static SolidColorBrush Solid(byte a, byte r, byte g, byte b) => Frozen(new SolidColorBrush(Color.FromArgb(a, r, g, b)));
    protected static Pen Line(byte a, byte r, byte g, byte b, double width) => Frozen(new Pen(Solid(a, r, g, b), width));

    /// <summary>Soft round glow (indicators): bright centre fading out.</summary>
    protected static RadialGradientBrush Glow(params (Color Color, double Offset)[] stops)
    {
        var b = new RadialGradientBrush();
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }

    protected static LinearGradientBrush Vertical(params (Color Color, double Offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }

    protected static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}
