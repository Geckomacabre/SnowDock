using System.Windows.Media;
using static SnowDock.Styles.ShelfStyle;

namespace SnowDock.Styles;

/// <summary>Every dock look, oldest first. Each covers the releases that shared a look.</summary>
internal static class DockStyles
{
    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static RadialGradientBrush Glow(params (Color Color, double Offset)[] stops)
    {
        var b = new RadialGradientBrush();
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }

    // ---- 3D glass shelf: Leopard through Mavericks ----

    public static readonly DockStyle Leopard = new ShelfStyle
    {
        StyleId = "leopard", StyleName = "Leopard", StyleVersions = "10.5",
        Floor = FloorOf(C(140, 190, 198, 212), C(170, 218, 224, 236), C(205, 240, 243, 250)),
        Lip = LipOf(C(220, 170, 178, 192), C(235, 100, 108, 124)),
        ReflectionOpacity = 0.68,
        Indicator = ShelfIndicator.Orb, IndicatorScale = 1.15,
        // Leopard's bluish sphere.
        IndicatorBrush = Glow((C(255, 235, 245, 255), 0), (C(240, 120, 175, 255), 0.3), (C(150, 50, 120, 245), 0.6), (C(0, 40, 100, 230), 1))
    };

    public static readonly DockStyle SnowLeopard = new ShelfStyle
    {
        StyleId = "snow-leopard", StyleName = "Snow Leopard & Lion", StyleVersions = "10.6–10.7", Dark = true,
        Floor = FloorOf(C(120, 196, 202, 212), C(150, 222, 226, 234), C(185, 240, 242, 247)),
        Lip = LipOf(C(215, 178, 183, 192), C(230, 112, 117, 128)),
        IndicatorBrush = Glow((C(255, 255, 255, 255), 0), (C(235, 190, 225, 255), 0.25), (C(120, 110, 175, 255), 0.55), (C(0, 90, 160, 255), 1))
    };

    public static readonly DockStyle MountainLion = new ShelfStyle
    {
        StyleId = "mountain-lion", StyleName = "Mountain Lion", StyleVersions = "10.8", Dark = true,
        // Frosted: whiter, more opaque glass with fainter reflections.
        Floor = FloorOf(C(165, 208, 212, 220), C(190, 228, 231, 237), C(215, 244, 245, 248)),
        Lip = LipOf(C(225, 190, 194, 201), C(235, 128, 132, 141)),
        ReflectionOpacity = 0.3,
        Indicator = ShelfIndicator.Bar,
        IndicatorBrush = Glow((C(255, 255, 255, 255), 0), (C(220, 200, 230, 255), 0.35), (C(90, 120, 180, 255), 0.7), (C(0, 120, 180, 255), 1))
    };

    public static readonly DockStyle Mavericks = new ShelfStyle
    {
        StyleId = "mavericks", StyleName = "Mavericks", StyleVersions = "10.9",
        // Smokier, greyer frosted glass (approximation).
        Floor = FloorOf(C(165, 104, 107, 115), C(185, 134, 137, 145), C(205, 168, 171, 178)),
        Lip = LipOf(C(230, 120, 123, 131), C(240, 70, 72, 79)),
        ReflectionOpacity = 0.32,
        Indicator = ShelfIndicator.Bar,
        IndicatorBrush = Glow((C(255, 255, 255, 255), 0), (C(230, 245, 248, 255), 0.35), (C(110, 230, 235, 255), 0.7), (C(0, 230, 235, 255), 1)),
        SidePanel = Solid(150, 225, 227, 232), SidePanelRim = Line(170, 255, 255, 255, 1)
    };

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
    {
        var s = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        s.Freeze();
        return s;
    }

    private static Pen Line(byte a, byte r, byte g, byte b, double w)
    {
        var p = new Pen(Solid(a, r, g, b), w);
        p.Freeze();
        return p;
    }

    // ---- Aqua: 10.0–10.4 ----

    public static readonly DockStyle Cheetah = new AquaStyle
    {
        StyleId = "cheetah", StyleName = "Cheetah & Puma", StyleVersions = "10.0–10.1", Pinstripes = 70, FillAlpha = 150
    };

    /// <summary>Jaguar dropped the pinstripes, leaving a translucent white bar; Panther and Tiger kept it.</summary>
    public static readonly DockStyle Jaguar = new AquaStyle
    {
        StyleId = "jaguar", StyleName = "Jaguar, Panther & Tiger", StyleVersions = "10.2–10.4", FillAlpha = 120
    };

    // ---- Flat: Yosemite through Sequoia ----

    public static readonly DockStyle YosemiteLight = new FlatStyle
    {
        StyleId = "yosemite", StyleName = "Yosemite to Catalina", StyleVersions = "10.10–10.15",
        Fill = Solid(205, 236, 236, 239), Rim = Line(70, 140, 140, 148, 1), InnerRim = Line(90, 255, 255, 255, 1),
        Dot = Solid(225, 28, 28, 30), Separator = Line(95, 0, 0, 0, 1),
        LabelBox = Solid(235, 238, 238, 240), LabelRim = Line(60, 0, 0, 0, 1)
    };

    public static readonly DockStyle YosemiteDark = new FlatStyle
    {
        StyleId = "yosemite-dark", StyleName = "Yosemite to Catalina (Dark)", StyleVersions = "10.10–10.15", Dark = true,
        Fill = Solid(190, 38, 38, 42), Rim = Line(80, 110, 110, 118, 1),
        Dot = Solid(225, 225, 225, 230), Separator = Line(110, 255, 255, 255, 1),
        LabelBox = Solid(230, 44, 44, 48), LabelRim = Line(70, 255, 255, 255, 1)
    };

    public static readonly DockStyle BigSurLight = new FlatStyle
    {
        StyleId = "big-sur", StyleName = "Big Sur to Sequoia", StyleVersions = "11–15", Floating = true, RadiusFactor = 0.34,
        Fill = Solid(178, 246, 246, 248), Rim = Line(55, 0, 0, 0, 1), InnerRim = Line(110, 255, 255, 255, 1),
        Dot = Solid(215, 40, 40, 44), Separator = Line(70, 0, 0, 0, 1),
        LabelBox = Solid(235, 246, 246, 248), LabelRim = Line(50, 0, 0, 0, 1)
    };

    public static readonly DockStyle BigSurDark = new FlatStyle
    {
        StyleId = "big-sur-dark", StyleName = "Big Sur to Sequoia (Dark)", StyleVersions = "11–15", Dark = true,
        Floating = true, RadiusFactor = 0.34,
        Fill = Solid(165, 32, 32, 36), Rim = Line(90, 0, 0, 0, 1), InnerRim = Line(70, 255, 255, 255, 1),
        Dot = Solid(220, 220, 220, 226), Separator = Line(90, 255, 255, 255, 1),
        LabelBox = Solid(230, 46, 46, 50), LabelRim = Line(70, 255, 255, 255, 1)
    };

    // ---- Liquid Glass: Tahoe ----

    public static readonly DockStyle Tahoe = new GlassStyle { StyleId = "tahoe", StyleName = "Tahoe", StyleVersions = "26" };
    public static readonly DockStyle TahoeDark = new GlassStyle
    {
        StyleId = "tahoe-dark", StyleName = "Tahoe (Dark)", StyleVersions = "26", Dark = true
    };

    public static IReadOnlyList<DockStyle> All { get; } = new List<DockStyle>
    {
        Cheetah, Jaguar, Leopard, SnowLeopard, MountainLion, Mavericks,
        YosemiteLight, YosemiteDark, BigSurLight, BigSurDark, Tahoe, TahoeDark
    };

    public static DockStyle Default => SnowLeopard;

    /// <summary>The style with this id, or Snow Leopard if it's unknown (e.g. a hand-edited config).</summary>
    public static DockStyle Find(string? id) => All.FirstOrDefault(s => s.Id == id) ?? Default;
}
