using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SnowDock.Native;

namespace SnowDock.Stacks;

/// <summary>
/// Draws a fan: the stack's items rising from the dock in an arc that leans to the right, each row
/// (name label + icon) turned along the arc, with an "Open in File Explorer" row on top.
/// Row 0 is the bottom row, nearest the dock.
/// </summary>
internal sealed class FanSurface : FrameworkElement
{
    public IReadOnlyList<StackEntry> Entries { get; set; } = Array.Empty<StackEntry>();
    public int MoreCount { get; set; }
    public double TileSize { get; set; } = 56;
    public int HoverIndex { get; set; } = -1;
    /// <summary>Seconds into the opening animation (counts back down while closing).</summary>
    public double AnimT { get; set; }

    public const double LabelMaxWidth = 240;
    private const double MaxAngleDeg = 17;
    private const double RowDuration = 0.22, RowStagger = 0.016;

    private static readonly Typeface Face =
        new(new FontFamily("Lucida Sans Unicode, Lucida Grande, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromArgb(185, 24, 24, 26)));
    private static readonly Brush HighlightBrush = Frozen(new LinearGradientBrush(
        Color.FromRgb(0x5A, 0x8F, 0xF0), Color.FromRgb(0x2B, 0x63, 0xDB), 90));
    private static readonly Brush ButtonBrush = Frozen(new RadialGradientBrush(
        Color.FromArgb(235, 90, 92, 98), Color.FromArgb(235, 28, 29, 32)));
    private static readonly Pen ButtonRim = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 2.2));
    private static readonly Pen ArrowPen = Frozen(new Pen(Brushes.White, 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public double Step => TileSize + 12;
    public int Rows => Entries.Count + 1;
    public double OriginX => LabelMaxWidth + 14 + TileSize / 2 + 8;
    public double OpenDuration => RowDuration + Rows * RowStagger;

    private double Radius(double height) => height / Math.Sin(MaxAngleDeg * Math.PI / 180);
    private double FanHeight => Rows * Step + 12;

    /// <summary>Window size (DIPs) needed for a fan of <paramref name="rows"/> rows.</summary>
    public static Size RequiredSize(int rows, double tile)
    {
        var probe = new FanSurface { TileSize = tile, Entries = new StackEntry[Math.Max(0, rows - 1)] };
        // Headroom above the top row: its label tilts up to the left along with the row.
        double h = probe.FanHeight + tile * 0.4 + LabelMaxWidth * Math.Sin(MaxAngleDeg * Math.PI / 180);
        double r = probe.Radius(probe.FanHeight);
        double lean = r * (1 - Math.Cos(MaxAngleDeg * Math.PI / 180));
        return new Size(probe.OriginX + tile / 2 + lean + tile * 0.7 + 12, h);
    }

    /// <summary>Final (fully open) placement of a row: center and angle in degrees.</summary>
    private (Point Center, double Angle) RowTarget(int row)
    {
        double y = (row + 0.5) * Step + 6;
        double r = Radius(FanHeight);
        double theta = Math.Asin(Math.Min(1, y / r));
        return (new Point(OriginX + r * (1 - Math.Cos(theta)), ActualHeight - y), theta * 180 / Math.PI);
    }

    private double RowProgress(int row)
    {
        double p = Math.Clamp((AnimT - row * RowStagger) / RowDuration, 0, 1);
        return 1 - Math.Pow(1 - p, 3);
    }

    private readonly Dictionary<int, double> _labelWidths = new();

    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var start = new Point(OriginX, ActualHeight + TileSize * 0.25);

        // Top row first so lower rows (closer to the dock) overlap it, as in a real fan.
        for (int row = Rows - 1; row >= 0; row--)
        {
            double e = RowProgress(row);
            if (e <= 0) continue;
            var (target, angle) = RowTarget(row);
            var c = new Point(start.X + (target.X - start.X) * e, start.Y + (target.Y - start.Y) * e);
            double scale = 0.35 + 0.65 * e;

            dc.PushTransform(new TranslateTransform(c.X, c.Y));
            dc.PushTransform(new RotateTransform(angle * e));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushOpacity(Math.Min(1, e * 1.6));

            bool isButton = row == Entries.Count;
            var tile = new Rect(-TileSize / 2, -TileSize / 2, TileSize, TileSize);
            if (isButton) DrawOpenButton(dc, TileSize);
            else
            {
                var img = Entries[row].Image;
                dc.DrawImage(img, IconLoader.FitSquare(img, tile));
            }

            string text = isButton
                ? (MoreCount > 0 ? $"{MoreCount} More in File Explorer" : "Open in File Explorer")
                : Entries[row].Name;
            dc.PushOpacity(e * e); // labels arrive a moment after the icons
            _labelWidths[row] = DrawLabel(dc, text, row == HoverIndex, -TileSize / 2 - 8, dpi);
            dc.Pop();

            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
    }

    /// <summary>Right-aligned label ending at <paramref name="right"/>. Returns its width.</summary>
    private static double DrawLabel(DrawingContext dc, string text, bool highlighted, double right, double dpi)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 12.5, Brushes.White, dpi)
        {
            MaxTextWidth = LabelMaxWidth - 20,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        double w = ft.WidthIncludingTrailingWhitespace + 20, h = ft.Height + 6;
        var rect = new Rect(right - w, -h / 2, w, h);
        dc.DrawRoundedRectangle(highlighted ? HighlightBrush : LabelBrush, null, rect, h / 2, h / 2);
        dc.DrawText(ft, new Point(rect.X + 10, rect.Y + 3));
        return w;
    }

    /// <summary>A round dark button with a white arrow, standing in for "open the folder".</summary>
    private static void DrawOpenButton(DrawingContext dc, double tile)
    {
        double r = tile * 0.36;
        dc.DrawEllipse(ButtonBrush, ButtonRim, new Point(0, 0), r, r);
        double a = r * 0.5;
        dc.DrawLine(ArrowPen, new Point(-a, 0), new Point(a, 0));
        dc.DrawLine(ArrowPen, new Point(a * 0.25, -a * 0.7), new Point(a, 0));
        dc.DrawLine(ArrowPen, new Point(a * 0.25, a * 0.7), new Point(a, 0));
    }

    /// <summary>Row under a point (icon or label), or -1. Uses the fully open layout.</summary>
    public int RowAt(Point p)
    {
        for (int row = 0; row < Rows; row++)
        {
            var (c, angle) = RowTarget(row);
            // Into the row's own (unrotated) frame.
            double rad = -angle * Math.PI / 180, dx = p.X - c.X, dy = p.Y - c.Y;
            double x = dx * Math.Cos(rad) - dy * Math.Sin(rad), y = dx * Math.Sin(rad) + dy * Math.Cos(rad);
            double half = Step / 2;
            if (y < -half || y > half) continue;
            double labelW = _labelWidths.TryGetValue(row, out var w) ? w : LabelMaxWidth;
            if (x >= -TileSize / 2 - 8 - labelW && x <= TileSize / 2) return row;
        }
        return -1;
    }
}
