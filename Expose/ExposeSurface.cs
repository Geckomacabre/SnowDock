using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnowDock.Expose;

internal sealed class ExposeTile
{
    public IntPtr Hwnd;
    public IntPtr Thumb;
    public SnowDock.Native.NativeMethods.RECT? Source; // visible part of the thumbnail (drops invisible borders)
    public string Title = "";
    public Rect Start, Target, Current; // physical pixels, relative to the overlay
}

/// <summary>
/// Everything in Exposé except the live window images (DWM draws those on top): the dimmed desktop,
/// a placeholder with the app's icon under each window, the blue hover ring and the window title.
/// </summary>
internal sealed class ExposeSurface : FrameworkElement
{
    public BitmapSource? Backdrop { get; set; }
    public BitmapSource? AppIcon { get; set; }
    public List<ExposeTile> Tiles { get; } = new();
    public int Hover { get; set; } = -1;
    /// <summary>0..1 animation progress (dimming and labels follow it).</summary>
    public double Progress { get; set; }
    /// <summary>Physical pixels per DIP.</summary>
    public double Scale { get; set; } = 1;

    private static readonly Typeface Face =
        new(new FontFamily("Lucida Sans Unicode, Lucida Grande, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Pen HoverPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xF0)), 4));
    private static readonly Brush TileBrush = Frozen(new SolidColorBrush(Color.FromArgb(200, 40, 42, 48)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromArgb(200, 22, 22, 24)));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private Rect Dip(Rect px) => new(px.X / Scale, px.Y / Scale, px.Width / Scale, px.Height / Scale);

    protected override void OnRender(DrawingContext dc)
    {
        var all = new Rect(0, 0, ActualWidth, ActualHeight);
        if (Backdrop != null) dc.DrawImage(Backdrop, all);
        else dc.DrawRectangle(Brushes.Black, null, all);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(150 * Progress), 8, 8, 10)), null, all);

        for (int i = 0; i < Tiles.Count; i++)
        {
            var r = Dip(Tiles[i].Current);
            if (r.Width < 2 || r.Height < 2) continue;
            // Shadow, then a placeholder (seen only if Windows has no image for the window, e.g. some minimized ones).
            var shadow = r; shadow.Offset(0, 6); shadow.Inflate(4, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(90 * Progress), 0, 0, 0)), null, shadow, 8, 8);
            dc.DrawRectangle(TileBrush, null, r);
            if (AppIcon != null)
            {
                double s = Math.Min(96, Math.Min(r.Width, r.Height) * 0.5);
                dc.DrawImage(AppIcon, new Rect(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s));
            }
        }

        if (Hover >= 0 && Hover < Tiles.Count && Progress > 0.95)
        {
            var r = Dip(Tiles[Hover].Current);
            var ring = r; ring.Inflate(5, 5);
            dc.DrawRoundedRectangle(null, HoverPen, ring, 6, 6);
            DrawTitle(dc, Tiles[Hover].Title, r);
        }
    }

    /// <summary>Title in a dark pill just below the window (DWM paints over anything drawn on top of it).</summary>
    private void DrawTitle(DrawingContext dc, string title, Rect under)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        var ft = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 14, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(120, under.Width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        double w = ft.WidthIncludingTrailingWhitespace + 24, h = ft.Height + 8;
        double x = Math.Clamp(under.X + (under.Width - w) / 2, 4, ActualWidth - w - 4);
        double y = Math.Min(under.Bottom + 12, ActualHeight - h - 4);
        dc.DrawRoundedRectangle(LabelBrush, null, new Rect(x, y, w, h), h / 2, h / 2);
        dc.DrawText(ft, new Point(x + 12, y + 4));
    }
}
