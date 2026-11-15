using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets;

/// <summary>Saved per widget instance in config.json.</summary>
internal sealed class WidgetConfig
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> Settings { get; set; } = new();
}

/// <summary>Colours and fonts for widget tiles, matched to the dock's style.</summary>
internal sealed class WidgetPaint
{
    public required bool Light { get; init; }
    public required Brush Tile { get; init; }
    public required Pen TileRim { get; init; }
    public required Brush Ink { get; init; }
    public required Brush SubInk { get; init; }
    public required Brush Track { get; init; }
    public Brush Accent { get; } = Frozen(new SolidColorBrush(Color.FromRgb(0x3B, 0x8B, 0xF6)));
    public Brush Good { get; } = Frozen(new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)));
    public Brush Warn { get; } = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)));
    public Brush Bad { get; } = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
    public required double PixelsPerDip { get; init; }

    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b) => Frozen(new SolidColorBrush(Color.FromArgb(a, r, g, b)));

    public static WidgetPaint For(bool light, double pixelsPerDip) => light
        ? new WidgetPaint
        {
            Light = true, PixelsPerDip = pixelsPerDip,
            Tile = Solid(235, 252, 252, 253), TileRim = Frozen(new Pen(Solid(40, 0, 0, 0), 1)),
            Ink = Solid(255, 20, 20, 22), SubInk = Solid(170, 40, 40, 46), Track = Solid(40, 0, 0, 0)
        }
        : new WidgetPaint
        {
            Light = false, PixelsPerDip = pixelsPerDip,
            Tile = Solid(225, 26, 26, 30), TileRim = Frozen(new Pen(Solid(55, 255, 255, 255), 1)),
            Ink = Solid(255, 255, 255, 255), SubInk = Solid(175, 235, 235, 240), Track = Solid(55, 255, 255, 255)
        };
}

/// <summary>What a widget can ask of the dock.</summary>
internal interface IWidgetHost
{
    /// <summary>Persist a changed setting.</summary>
    void SaveConfig();
    /// <summary>Redraw the dock now (e.g. after a click changed the widget).</summary>
    void Redraw();
    /// <summary>Open (or refresh) this widget's card.</summary>
    void ShowCard(DockWidget widget);
    void CloseCard();
    /// <summary>Bounce this widget's tile, like an app asking for attention (a timer finishing).</summary>
    void Attention(DockWidget widget);
}

/// <summary>
/// A live tile in the dock. Widgets draw themselves in a rect the dock gives them, update from one shared
/// one-second tick, and can open a card with more detail and controls when clicked.
/// </summary>
internal abstract class DockWidget
{
    public WidgetConfig Config { get; set; } = new();
    public IWidgetHost? Host { get; set; }

    /// <summary>Shown as the hover label and in the picker.</summary>
    public abstract string Title { get; }
    /// <summary>Width in icon sizes (1 = square).</summary>
    public virtual double WidthUnits => 1;

    /// <summary>Called once a second. Return true when the tile looks different and should be redrawn.</summary>
    public virtual bool Tick(DateTime now) => false;

    public abstract void Draw(DrawingContext dc, Rect r, WidgetPaint p);

    /// <summary>Content for the card opened by a click, or null for widgets that act directly.</summary>
    public virtual FrameworkElement? BuildCard(WidgetPaint p) => null;

    /// <summary>True if <see cref="BuildCard"/> returns something.</summary>
    public virtual bool HasCard => false;

    /// <summary>A click on the tile. By default it opens the card.</summary>
    public virtual void OnClick()
    {
        if (HasCard) Host?.ShowCard(this);
    }

    /// <summary>
    /// A click at a point in the tile, in the tile's upright coordinates (0,0 = its top-left as seen on screen).
    /// By default the same as <see cref="OnClick"/>.
    /// </summary>
    public virtual void OnClickAt(Point inTile, Size tile) => OnClick();

    /// <summary>A right-click in the tile. Return true when handled, so the dock's own widget menu stays closed.</summary>
    public virtual bool OnRightClickAt(Point inTile, Size tile) => false;

    /// <summary>The mouse wheel over the tile (delta in WPF units, 120 per notch). Return true if handled.</summary>
    public virtual bool OnWheel(int delta) => false;

    public virtual void OnRemoved() { }

    // ---- settings ----
    protected string Get(string key, string fallback) => Config.Settings.TryGetValue(key, out var v) ? v : fallback;
    protected void Set(string key, string value)
    {
        Config.Settings[key] = value;
        Host?.SaveConfig();
    }

    // ---- drawing helpers ----
    protected static void Tile(DrawingContext dc, Rect r, WidgetPaint p) =>
        dc.DrawRoundedRectangle(p.Tile, p.TileRim, r, Math.Min(14, r.Height * 0.24), Math.Min(14, r.Height * 0.24));

    protected static FormattedText Text(string s, double size, Brush ink, WidgetPaint p, FontWeight? weight = null, FontFamily? font = null) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(font ?? WidgetPaint.Font, FontStyles.Normal, weight ?? FontWeights.SemiBold, FontStretches.Normal),
            size, ink, p.PixelsPerDip);

    /// <summary>Draw text centred on a point, shrinking it to fit <paramref name="maxWidth"/>.</summary>
    protected static void Centered(DrawingContext dc, string s, Point c, double size, Brush ink, WidgetPaint p,
        double maxWidth = double.MaxValue, FontWeight? weight = null, FontFamily? font = null)
    {
        var t = Text(s, size, ink, p, weight, font);
        if (t.Width > maxWidth && t.Width > 0) t = Text(s, size * maxWidth / t.Width, ink, p, weight, font);
        dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
    }

    /// <summary>A progress ring (0..1) with a faint full track behind it.</summary>
    protected static void Ring(DrawingContext dc, Point c, double radius, double fraction, Brush fill, WidgetPaint p, double thickness = 4)
    {
        dc.DrawEllipse(null, new Pen(p.Track, thickness), c, radius, radius);
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction <= 0) return;
        var pen = new Pen(fill, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 0.999) { dc.DrawEllipse(null, pen, c, radius, radius); return; }
        double a = fraction * 2 * Math.PI;
        var start = new Point(c.X, c.Y - radius);
        var end = new Point(c.X + radius * Math.Sin(a), c.Y - radius * Math.Cos(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }

    /// <summary>A horizontal bar (0..1).</summary>
    protected static void Bar(DrawingContext dc, Rect r, double fraction, Brush fill, WidgetPaint p)
    {
        dc.DrawRoundedRectangle(p.Track, null, r, r.Height / 2, r.Height / 2);
        var f = new Rect(r.X, r.Y, Math.Max(r.Height, r.Width * Math.Clamp(fraction, 0, 1)), r.Height);
        if (fraction > 0) dc.DrawRoundedRectangle(fill, null, f, r.Height / 2, r.Height / 2);
    }
}
