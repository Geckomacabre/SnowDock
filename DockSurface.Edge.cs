using System.Windows;
using System.Windows.Media;

namespace SnowDock;

/// <summary>
/// Left and right docks. Layout always works in "dock space": the dock lies along X with the screen edge at the
/// bottom (Y = CrossLength), exactly like the bottom dock. One matrix maps dock space onto the window for the
/// chosen edge, and every mouse position goes back through its inverse. Icons and labels are counter-turned
/// so they stay upright.
/// </summary>
internal sealed partial class DockSurface
{
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    /// <summary>Left or right: the dock runs down the screen.</summary>
    public bool IsSide => Edge is DockEdge.Left or DockEdge.Right;
    public bool IsBottom => Edge == DockEdge.Bottom;

    /// <summary>Length along the dock (window width for a bottom dock, height for a side dock).</summary>
    public double MainLength => IsSide ? ActualHeight : ActualWidth;
    /// <summary>Depth from the screen edge into the screen.</summary>
    public double CrossLength => IsSide ? ActualWidth : ActualHeight;

    /// <summary>Dock space → window.</summary>
    public Matrix ToWindowMatrix => Edge switch
    {
        // (x, y) → (C - y, x): the dock's bottom edge becomes the window's left edge, first item at the top.
        DockEdge.Left => new Matrix(0, 1, -1, 0, CrossLength, 0),
        // (x, y) → (y, x): the bottom edge becomes the right edge, first item at the top.
        DockEdge.Right => new Matrix(0, 1, 1, 0, 0, 0),
        // (x, y) → (x, C - y): the bottom edge becomes the top edge (flipped, so drawings are turned back upright).
        DockEdge.Top => new Matrix(1, 0, 0, -1, 0, CrossLength),
        _ => Matrix.Identity
    };

    public Point ToWindow(Point dock) => ToWindowMatrix.Transform(dock);

    public Point ToDock(Point window)
    {
        var m = ToWindowMatrix;
        m.Invert();
        return m.Transform(window);
    }

    /// <summary>A dock-space point in physical screen pixels.</summary>
    public Point ScreenPoint(double x, double y) => PointToScreen(ToWindow(new Point(x, y)));

    /// <summary>A physical screen point in dock space (works outside the window too).</summary>
    public Point DockFromScreen(Point screenPx) => ToDock(PointFromScreen(screenPx));

    /// <summary>A dock-space rectangle in window coordinates (for placing popups).</summary>
    public Rect WindowRect(Rect dock) => new(ToWindow(dock.TopLeft), ToWindow(dock.BottomRight));

    /// <summary>
    /// Push a transform that undoes the edge turn around <paramref name="center"/>, so an icon or label drawn
    /// there in dock space shows upright. Always pair with dc.Pop().
    /// </summary>
    public void PushUpright(DrawingContext dc, Point center)
    {
        if (IsBottom)
        {
            dc.PushTransform(Transform.Identity);
            return;
        }
        var linear = ToWindowMatrix;
        linear.OffsetX = linear.OffsetY = 0;
        linear.Invert();
        var m = Matrix.Identity;
        m.Translate(-center.X, -center.Y);
        m.Append(linear);
        m.Translate(center.X, center.Y);
        dc.PushTransform(new MatrixTransform(m));
    }
}
