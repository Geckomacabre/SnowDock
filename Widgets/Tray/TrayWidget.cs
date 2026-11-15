using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnowDock.Native;

namespace SnowDock.Widgets.Tray;

[WidgetFamily(33)]
internal static class TrayWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add) =>
        add("tray", "Tray Icons", WidgetCatalog.System,
            "Apps that sit in the notification area (Discord, OBS and the like), hidden ones too. Click one to open it, right-click for its menu.",
            () => new TrayWidget());
}

/// <summary>
/// The notification area on the dock: a small grid of tray icons that act like they do in the taskbar, with a
/// card listing every icon (including the ones Windows tucks into its hidden-icons flyout).
/// </summary>
internal sealed class TrayWidget : DockWidget
{
    private const int Rows = 2;
    private bool _using;
    private int _seen = -1;

    public override string Title => TrayService.Icons.Count is var n and > 0 ? $"Tray Icons ({n})" : "Tray Icons";
    /// <summary>Two columns hold four icons; past that, three (the last cell shows how many more there are).</summary>
    public override double WidthUnits => TrayService.Icons.Count <= 4 ? 1 : 1.45;
    public override bool HasCard => true;

    public override bool Tick(DateTime now)
    {
        // Start listening only once the widget is really on a dock (the picker draws a host-less preview).
        if (!_using && Host != null)
        {
            _using = true;
            TrayService.Acquire();
        }
        int v = TrayService.Version;
        if (v == _seen) return false;
        _seen = v;
        return true;
    }

    public override void OnRemoved()
    {
        if (!_using) return;
        _using = false;
        TrayService.Release();
    }

    // ---- tile ----

    private readonly record struct Slot(Rect Cell, TrayIcon? Icon, int More);

    /// <summary>Where each icon goes in a tile of this size (tile-relative). Used for drawing and for clicks.</summary>
    private static List<Slot> Slots(IReadOnlyList<TrayIcon> icons, Size tile)
    {
        var slots = new List<Slot>();
        if (icons.Count == 0 || tile.Height <= 0) return slots;
        int cols = tile.Width / tile.Height > 1.2 ? 3 : 2, capacity = cols * Rows;
        int used = Math.Min(icons.Count, capacity);
        int rowsUsed = used <= cols ? 1 : Rows, colsUsed = rowsUsed == 1 ? used : cols;
        double pad = tile.Height * 0.12, cell = (tile.Height - pad * 2) / Rows;
        double x0 = (tile.Width - cell * colsUsed) / 2, y0 = (tile.Height - cell * rowsUsed) / 2;
        Rect At(int i) => new(x0 + i % cols * cell, y0 + i / cols * cell, cell, cell);

        int shown = icons.Count <= capacity ? icons.Count : capacity - 1;
        for (int i = 0; i < shown; i++) slots.Add(new Slot(At(i), icons[i], 0));
        if (shown < icons.Count) slots.Add(new Slot(At(capacity - 1), null, icons.Count - shown));
        return slots;
    }

    private static Slot? Hit(Point p, Size tile)
    {
        foreach (var s in Slots(TrayService.Icons, tile))
            if (s.Cell.Contains(p)) return s;
        return null;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var icons = TrayService.Icons;
        if (icons.Count == 0)
        {
            Chevron(dc, r, p);
            return;
        }
        foreach (var s in Slots(icons, r.Size))
        {
            var cell = s.Cell;
            cell.Offset(r.X, r.Y);
            if (s.Icon == null) More(dc, cell, s.More, p);
            else DrawIcon(dc, cell, s.Icon, p);
        }
    }

    private static void DrawIcon(DrawingContext dc, Rect cell, TrayIcon icon, WidgetPaint p)
    {
        double size = cell.Width * 0.72;
        var box = new Rect(cell.X + (cell.Width - size) / 2, cell.Y + (cell.Height - size) / 2, size, size);
        if (ImageFor(icon) is { } img)
        {
            if (ChipFor(img, p) is { } chip)
            {
                var back = new Rect(cell.X + cell.Width * 0.06, cell.Y + cell.Height * 0.06, cell.Width * 0.88, cell.Height * 0.88);
                dc.DrawRoundedRectangle(chip, null, back, back.Width * 0.28, back.Width * 0.28);
            }
            dc.DrawImage(img, box);
        }
        else
        {
            // Nothing to show: a plain rounded square with a dot.
            dc.DrawRoundedRectangle(null, new Pen(p.SubInk, Math.Max(1, size * 0.08)), box, size * 0.22, size * 0.22);
            dc.DrawEllipse(p.SubInk, null, new Point(box.X + size / 2, box.Y + size / 2), size * 0.14, size * 0.14);
        }
    }

    private static readonly Brush DarkChip = Chip(0x3A, 0x3A, 0x3E), LightChip = Chip(0xEC, 0xEC, 0xF0);

    private static Brush Chip(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(235, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>A backing for a one-colour glyph that would vanish on this tile (a white icon on a light tile), else null.</summary>
    private static Brush? ChipFor(ImageSource img, WidgetPaint p) => TrayIcon.ToneOf(img) switch
    {
        1 when p.Light => DarkChip,
        -1 when !p.Light => LightChip,
        _ => null
    };

    /// <summary>The icon's own picture, else its program's icon, else nothing (drawn as a neutral glyph).</summary>
    private static ImageSource? ImageFor(TrayIcon icon) =>
        icon.Image ?? (icon.ExePath != null ? IconLoader.ForPath(icon.ExePath) : null);

    private static void More(DrawingContext dc, Rect cell, int more, WidgetPaint p)
    {
        var box = new Rect(cell.X + cell.Width * 0.1, cell.Y + cell.Height * 0.1, cell.Width * 0.8, cell.Height * 0.8);
        dc.DrawRoundedRectangle(p.Track, null, box, box.Height * 0.3, box.Height * 0.3);
        Centered(dc, "+" + more, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Height * 0.5, p.Ink, p, box.Width * 0.9);
    }

    /// <summary>An up-chevron, like the taskbar's "show hidden icons" button: shown while there's nothing to list.</summary>
    private static void Chevron(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double u = r.Height * 0.09;
        var pen = new Pen(p.Ink, Math.Max(1.6, u * 0.45)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - u * 2, c.Y + u), false, false);
            ctx.LineTo(new Point(c.X, c.Y - u), true, true);
            ctx.LineTo(new Point(c.X + u * 2, c.Y + u), true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }

    // ---- clicks ----

    public override void OnClickAt(Point inTile, Size tile)
    {
        if (Hit(inTile, tile)?.Icon is { } icon && TrayService.Click(icon, right: false)) return;
        // "+N", a gap, or an icon that can't be clicked from here: show the whole list.
        Host?.ShowCard(this);
    }

    public override bool OnRightClickAt(Point inTile, Size tile) =>
        Hit(inTile, tile)?.Icon is { } icon && TrayService.Click(icon, right: true);

    // ---- card ----

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var icons = TrayService.Current();
        var list = new StackPanel { Width = 330 };
        var shown = icons.Where(i => i.Promoted).ToList();
        var tucked = icons.Where(i => !i.Promoted).ToList();
        bool both = shown.Count > 0 && tucked.Count > 0;
        if (both) list.Children.Add(Header("On the taskbar"));
        foreach (var i in shown) list.Children.Add(Row(i, p));
        if (both) list.Children.Add(Header("Hidden icons"));
        foreach (var i in tucked) list.Children.Add(Row(i, p));
        if (icons.Count == 0) list.Children.Add(new TextBlock { Text = "No tray icons right now.", Opacity = 0.7, Margin = new Thickness(4) });

        var card = new StackPanel();
        card.Children.Add(new ScrollViewer { Content = list, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        card.Children.Add(new TextBlock
        {
            Text = TrayService.Listening ? "Click to open · right-click for the app's menu"
                : "Preview only: clicks reach the apps when SnowDock runs in place of the taskbar.",
            FontSize = 11, Opacity = 0.6, Margin = new Thickness(6, 8, 6, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 330
        });
        return card;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.6, Margin = new Thickness(6, 6, 6, 2)
    };

    private FrameworkElement Row(TrayIcon icon, WidgetPaint p)
    {
        var source = ImageFor(icon) ?? IconLoader.Placeholder();
        var picture = new Image { Source = source, Width = 18, Height = 18 };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var image = new Border
        {
            Child = picture, Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = ChipFor(source, p),
            Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = icon.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = Detail(icon);
        if (detail.Length > 0)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
        var line = new DockPanel();
        DockPanel.SetDock(image, Dock.Left);
        line.Children.Add(image);
        line.Children.Add(text);

        var row = new Border
        {
            Child = line, Padding = new Thickness(6, 5, 6, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent,
            Cursor = icon.CanClick ? Cursors.Hand : Cursors.Arrow
        };
        if (icon.Tip.Contains('\n')) row.ToolTip = icon.Tip.Trim();
        row.MouseEnter += (_, _) => row.Background = p.Track;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Activate(icon, right: false); };
        row.MouseRightButtonUp += (_, e) => { e.Handled = true; Activate(icon, right: true); };
        return row;
    }

    /// <summary>The rest of the tooltip and the program's name, whichever add something to the title.</summary>
    private static string Detail(TrayIcon icon)
    {
        var parts = icon.Tip.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToList();
        parts.Add(icon.AppName);
        return string.Join(" · ", parts.Where(s => s.Length > 0 && !icon.Name.Contains(s, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void Activate(TrayIcon icon, bool right)
    {
        Host?.CloseCard();
        try { TrayService.Click(icon, right); }
        catch (Exception ex) { AppPaths.Log($"Tray click failed: {ex.Message}"); }
    }
}
