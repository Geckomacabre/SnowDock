using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.Media;

/// <summary>
/// Small vector glyphs shared by the media and system tiles. Each is drawn around a centre point
/// <c>c</c> inside a square of half-size <c>s</c>, so it scales with the tile.
/// </summary>
internal static class Glyphs
{
    public static Pen Stroke(Brush b, double width) =>
        new(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    /// <summary>A polygon (or polyline) from points given in glyph units (-1..1) around <paramref name="c"/>.</summary>
    private static StreamGeometry Shape(Point c, double s, bool closed, params double[] xy)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + xy[0] * s, c.Y + xy[1] * s), closed, closed);
            for (int i = 2; i < xy.Length; i += 2) ctx.LineTo(new Point(c.X + xy[i] * s, c.Y + xy[i + 1] * s), true, true);
        }
        return g;
    }

    /// <summary>An arc of radius <paramref name="r"/> around <paramref name="c"/> from angle a0 to a1 (degrees, 0 = right, clockwise, y down).</summary>
    private static void Arc(DrawingContext dc, Pen pen, Point c, double r, double a0, double a1)
    {
        double t0 = a0 * Math.PI / 180, t1 = a1 * Math.PI / 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(t0), c.Y + r * Math.Sin(t0)), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(t1), c.Y + r * Math.Sin(t1)), new Size(r, r), 0, Math.Abs(a1 - a0) > 180,
                a1 > a0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        dc.DrawGeometry(null, pen, g);
    }

    public static void Play(DrawingContext dc, Point c, double s, Brush b)
    {
        var pen = new Pen(b, s * 0.16) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(b, pen, Shape(new Point(c.X + s * 0.12, c.Y), s, true, -0.72, -0.82, 0.8, 0, -0.72, 0.82));
    }

    public static void Pause(DrawingContext dc, Point c, double s, Brush b)
    {
        double w = s * 0.5, h = s * 1.6, gap = s * 0.38;
        dc.DrawRoundedRectangle(b, null, new Rect(c.X - gap / 2 - w, c.Y - h / 2, w, h), w * 0.3, w * 0.3);
        dc.DrawRoundedRectangle(b, null, new Rect(c.X + gap / 2, c.Y - h / 2, w, h), w * 0.3, w * 0.3);
    }

    public static void PlayPause(DrawingContext dc, Point c, double s, Brush b, bool playing)
    {
        if (playing) Pause(dc, c, s, b); else Play(dc, c, s, b);
    }

    /// <summary>Skip to the next (forward) or previous track: a triangle against a bar.</summary>
    public static void Skip(DrawingContext dc, Point c, double s, Brush b, bool forward)
    {
        double d = forward ? 1 : -1;
        var pen = new Pen(b, s * 0.14) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(b, pen, Shape(c, s, true, -0.62 * d, -0.72, 0.42 * d, 0, -0.62 * d, 0.72));
        double barX = c.X + (forward ? s * 0.56 : -s * 0.82);
        dc.DrawRoundedRectangle(b, null, new Rect(barX, c.Y - s * 0.74, s * 0.26, s * 1.48), s * 0.1, s * 0.1);
    }

    /// <summary>A speaker with 0–3 sound waves, or a cross when muted.</summary>
    public static void Speaker(DrawingContext dc, Point c, double s, Brush b, int waves, bool muted, Brush? crossInk = null)
    {
        var o = new Point(c.X - s * 0.2, c.Y);
        dc.DrawGeometry(b, new Pen(b, s * 0.1) { LineJoin = PenLineJoin.Round },
            Shape(o, s, true, -0.75, -0.3, -0.35, -0.3, 0.12, -0.75, 0.12, 0.75, -0.35, 0.3, -0.75, 0.3));
        var pen = Stroke(muted ? crossInk ?? b : b, s * 0.15);
        if (muted)
        {
            double x = o.X + s * 0.72, k = s * 0.26;
            dc.DrawLine(pen, new Point(x - k, c.Y - k), new Point(x + k, c.Y + k));
            dc.DrawLine(pen, new Point(x - k, c.Y + k), new Point(x + k, c.Y - k));
            return;
        }
        for (int i = 1; i <= Math.Clamp(waves, 0, 3); i++)
            Arc(dc, pen, new Point(o.X + s * 0.1, c.Y), s * (0.18 + 0.3 * i), -42, 42);
    }

    /// <summary>A microphone; muted adds a slash.</summary>
    public static void Mic(DrawingContext dc, Point c, double s, Brush b, bool muted, Brush slashInk, Brush gapInk)
    {
        var pen = Stroke(b, s * 0.15);
        dc.DrawRoundedRectangle(b, null, new Rect(c.X - s * 0.3, c.Y - s * 0.95, s * 0.6, s * 1.15), s * 0.3, s * 0.3);
        Arc(dc, pen, new Point(c.X, c.Y - s * 0.1), s * 0.58, 0, 180);
        dc.DrawLine(pen, new Point(c.X, c.Y + s * 0.48), new Point(c.X, c.Y + s * 0.85));
        dc.DrawLine(pen, new Point(c.X - s * 0.35, c.Y + s * 0.85), new Point(c.X + s * 0.35, c.Y + s * 0.85));
        if (!muted) return;
        // A slash with a thin gap of tile colour either side, so it reads over the glyph.
        dc.DrawLine(Stroke(gapInk, s * 0.38), new Point(c.X - s * 0.8, c.Y - s * 0.85), new Point(c.X + s * 0.8, c.Y + s * 0.85));
        dc.DrawLine(Stroke(slashInk, s * 0.17), new Point(c.X - s * 0.8, c.Y - s * 0.85), new Point(c.X + s * 0.8, c.Y + s * 0.85));
    }

    public static void Note(DrawingContext dc, Point c, double s, Brush b)
    {
        var pen = Stroke(b, s * 0.16);
        dc.PushTransform(new RotateTransform(-20, c.X - s * 0.28, c.Y + s * 0.55));
        dc.DrawEllipse(b, null, new Point(c.X - s * 0.28, c.Y + s * 0.55), s * 0.34, s * 0.25);
        dc.Pop();
        dc.DrawLine(pen, new Point(c.X + s * 0.04, c.Y + s * 0.5), new Point(c.X + s * 0.04, c.Y - s * 0.85));
        var flag = new StreamGeometry();
        using (var g = flag.Open())
        {
            g.BeginFigure(new Point(c.X + s * 0.04, c.Y - s * 0.85), false, false);
            g.BezierTo(new Point(c.X + s * 0.15, c.Y - s * 0.45), new Point(c.X + s * 0.7, c.Y - s * 0.5), new Point(c.X + s * 0.5, c.Y - s * 0.05), true, true);
        }
        dc.DrawGeometry(null, pen, flag);
    }

    public static void Sun(DrawingContext dc, Point c, double s, Brush b)
    {
        dc.DrawEllipse(b, null, c, s * 0.4, s * 0.4);
        var pen = Stroke(b, s * 0.15);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4, ca = Math.Cos(a), sa = Math.Sin(a);
            dc.DrawLine(pen, new Point(c.X + ca * s * 0.62, c.Y + sa * s * 0.62), new Point(c.X + ca * s * 0.9, c.Y + sa * s * 0.9));
        }
    }

    public static void Moon(DrawingContext dc, Point c, double s, Brush? fill, Pen? outline)
    {
        var moon = Geometry.Combine(new EllipseGeometry(c, s * 0.82, s * 0.82),
            new EllipseGeometry(new Point(c.X + s * 0.45, c.Y - s * 0.32), s * 0.66, s * 0.66), GeometryCombineMode.Exclude, null);
        dc.DrawGeometry(fill, outline, moon);
    }

    public static void Wifi(DrawingContext dc, Point c, double s, Brush b)
    {
        var o = new Point(c.X, c.Y + s * 0.62);
        dc.DrawEllipse(b, null, o, s * 0.15, s * 0.15);
        var pen = Stroke(b, s * 0.17);
        foreach (double r in new[] { 0.5, 0.93, 1.36 }) Arc(dc, pen, o, s * r, -135, -45);
    }

    public static void Bluetooth(DrawingContext dc, Point c, double s, Brush b) =>
        dc.DrawGeometry(null, Stroke(b, s * 0.15), Shape(c, s, false, -0.45, -0.42, 0.42, 0.4, 0, 0.86, 0, -0.86, 0.42, -0.4, -0.45, 0.42));

    public static void Hotspot(DrawingContext dc, Point c, double s, Brush b)
    {
        dc.DrawEllipse(b, null, c, s * 0.17, s * 0.17);
        var pen = Stroke(b, s * 0.15);
        foreach (double r in new[] { 0.48, 0.88 })
        {
            Arc(dc, pen, c, s * r, -38, 38);
            Arc(dc, pen, c, s * r, 142, 218);
        }
    }

    public static void Airplane(DrawingContext dc, Point c, double s, Brush b) =>
        dc.DrawGeometry(b, new Pen(b, s * 0.06) { LineJoin = PenLineJoin.Round }, Shape(c, s, true,
            0, -0.95, 0.11, -0.82, 0.11, -0.25, 0.95, 0.2, 0.95, 0.38, 0.11, 0.12, 0.11, 0.58, 0.4, 0.78, 0.4, 0.92, 0, 0.82,
            -0.4, 0.92, -0.4, 0.78, -0.11, 0.58, -0.11, 0.12, -0.95, 0.38, -0.95, 0.2, -0.11, -0.25, -0.11, -0.82));

    public static void Bolt(DrawingContext dc, Point c, double s, Brush b) =>
        dc.DrawGeometry(b, new Pen(b, s * 0.06) { LineJoin = PenLineJoin.Round },
            Shape(c, s, true, 0.18, -0.95, -0.55, 0.12, -0.05, 0.12, -0.22, 0.95, 0.55, -0.16, 0.05, -0.16));
}
