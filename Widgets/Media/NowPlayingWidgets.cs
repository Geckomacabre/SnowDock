using System.Windows;
using System.Windows.Media;

namespace SnowDock.Widgets.Media;

[WidgetFamily(35)]
internal static class NowPlayingFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Media;
        add("now-playing", "Now Playing", c, "Cover, title and artist of whatever is playing, with back, play/pause and skip buttons.",
            () => new NowPlayingWidget());
        add("now-playing-compact", "Now Playing (compact)", c, "Title and artist with a play/pause button.", () => new NowPlayingCompact());
        add("now-playing-mini", "Now Playing (mini)", c, "A round play/pause button circled by how far the track has got.",
            () => new NowPlayingMini());
        add("now-playing-bar", "Now Playing (bar)", c, "The title over a progress bar; click to play or pause, or the arrow to skip.",
            () => new NowPlayingBar());
    }
}

/// <summary>Shared state, drawing and hit testing for the now-playing tiles.</summary>
internal abstract class NowPlayingTile : DockWidget
{
    private int _seen = -1;
    private int _second = -1;

    protected static Track? T => MediaService.Current;
    protected static bool Playing => T?.Playing == true;
    /// <summary>Tiles that show progress redraw every second while playing.</summary>
    protected virtual bool ShowsProgress => false;

    /// <summary>The hover label and card header: title and artist, kept short so the card doesn't stretch.</summary>
    public override string Title => T is Track t && t.Title.Length > 0
        ? Short(t.Title, 40) + (t.Artist.Length > 0 ? " — " + Short(t.Artist, 24) : "")
        : MediaService.Ready ? "Nothing playing" : "Now Playing";

    private static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    public override bool Tick(DateTime now)
    {
        MediaService.Request();
        bool changed = _seen != MediaService.Version;
        _seen = MediaService.Version;
        if (ShowsProgress && Playing && T!.Duration > TimeSpan.Zero && _second != now.Second) { _second = now.Second; changed = true; }
        return changed;
    }

    /// <summary>The zone under a click, from the same layout the tile is drawn with.</summary>
    protected abstract MediaCommand? ZoneAt(Point p, Rect tile);

    public override void OnClickAt(Point inTile, Size tile)
    {
        if (ZoneAt(inTile, new Rect(tile)) is MediaCommand cmd)
        {
            MediaService.Send(cmd);
            Host?.Redraw();
        }
        else OnClick();
    }

    public override bool HasCard => true;
    public override FrameworkElement BuildCard(WidgetPaint p) => NowPlayingCard.Build(p, Host);

    // ---- drawing helpers ----

    protected static void DrawArt(DrawingContext dc, Rect r, WidgetPaint p)
    {
        double radius = r.Height * 0.16;
        if (MediaService.Art is ImageSource art && T != null)
        {
            dc.PushClip(new RectangleGeometry(r, radius, radius));
            // Fill the square, cropping a non-square cover.
            double scale = Math.Max(r.Width / art.Width, r.Height / art.Height);
            double w = art.Width * scale, h = art.Height * scale;
            dc.DrawImage(art, new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
            dc.Pop();
            return;
        }
        dc.DrawRoundedRectangle(p.Track, null, r, radius, radius);
        Glyphs.Note(dc, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Height * 0.3, p.SubInk);
    }

    /// <summary>Title and artist stacked in <paramref name="r"/>, trimmed to fit.</summary>
    protected static void DrawLines(DrawingContext dc, Rect r, WidgetPaint p, double titleSize, double artistSize, bool artist = true)
    {
        string title = T is Track t && t.Title.Length > 0 ? t.Title : MediaService.Ready ? "Nothing playing" : "…";
        string by = T?.Artist is { Length: > 0 } a ? a : T?.App ?? "";
        var ft = Line(title, titleSize, T == null ? p.SubInk : p.Ink, p, r.Width, FontWeights.SemiBold);
        var fa = artist && by.Length > 0 ? Line(by, artistSize, p.SubInk, p, r.Width, FontWeights.Normal) : null;
        double total = ft.Height + (fa?.Height ?? 0) * 0.95;
        double y = r.Y + (r.Height - total) / 2;
        dc.DrawText(ft, new Point(r.X, y));
        if (fa != null) dc.DrawText(fa, new Point(r.X, y + ft.Height * 0.95));
    }

    protected static FormattedText Line(string s, double size, Brush ink, WidgetPaint p, double width, FontWeight weight)
    {
        var t = Text(s, size, ink, p, weight);
        t.MaxTextWidth = Math.Max(1, width);
        t.MaxLineCount = 1;
        t.Trimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    protected static Brush Ink(WidgetPaint p, bool enabled) => enabled ? p.Ink : p.SubInk;
}

/// <summary>Cover, title and artist, and back / play-pause / skip.</summary>
internal sealed class NowPlayingWidget : NowPlayingTile
{
    public override double WidthUnits => 4.4;
    protected override bool ShowsProgress => true;

    private static (Rect Art, Rect Text, Rect Prev, Rect Play, Rect Next) Layout(Rect r)
    {
        double h = r.Height, pad = h * 0.12, cw = h * 0.46;
        var art = new Rect(r.X + pad, r.Y + pad, h - pad * 2, h - pad * 2);
        double right = r.Right - pad * 0.6;
        var next = new Rect(right - cw, r.Y, cw, h);
        var play = new Rect(next.X - cw, r.Y, cw, h);
        var prev = new Rect(play.X - cw, r.Y, cw, h);
        var text = new Rect(art.Right + pad, r.Y + pad, Math.Max(1, prev.X - art.Right - pad * 1.3), h - pad * 2);
        return (art, text, prev, play, next);
    }

    protected override MediaCommand? ZoneAt(Point p, Rect tile)
    {
        var l = Layout(tile);
        if (T == null) return null;
        return l.Prev.Contains(p) ? MediaCommand.Previous : l.Play.Contains(p) ? MediaCommand.PlayPause : l.Next.Contains(p) ? MediaCommand.Next : null;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var l = Layout(r);
        double h = r.Height;
        DrawArt(dc, l.Art, p);
        var text = new Rect(l.Text.X, l.Text.Y, l.Text.Width, l.Text.Height - h * 0.08);
        DrawLines(dc, text, p, Math.Min(13.5, h * 0.24), Math.Min(11.5, h * 0.2));
        if (T is Track t && t.Duration > TimeSpan.Zero)
            Bar(dc, new Rect(l.Text.X, l.Text.Bottom - h * 0.05, l.Text.Width, Math.Max(2, h * 0.045)), t.Progress, p.Accent, p);

        double s = h * 0.13;
        Glyphs.Skip(dc, Mid(l.Prev), s, Ink(p, T?.CanPrevious == true), forward: false);
        dc.DrawEllipse(T == null ? p.Track : p.Accent, null, Mid(l.Play), h * 0.21, h * 0.21);
        Glyphs.PlayPause(dc, Mid(l.Play), s * 0.95, T == null ? p.SubInk : Brushes.White, Playing);
        Glyphs.Skip(dc, Mid(l.Next), s, Ink(p, T?.CanNext == true), forward: true);
    }

    private static Point Mid(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}

/// <summary>Title and artist with a play/pause button on the right.</summary>
internal sealed class NowPlayingCompact : NowPlayingTile
{
    public override double WidthUnits => 2.6;

    private static (Rect Text, Rect Play) Layout(Rect r)
    {
        double h = r.Height, pad = h * 0.16;
        var play = new Rect(r.Right - h * 0.9, r.Y, h * 0.9, h);
        return (new Rect(r.X + pad, r.Y + pad * 0.5, Math.Max(1, play.X - r.X - pad), h - pad), play);
    }

    protected override MediaCommand? ZoneAt(Point p, Rect tile) => T != null && Layout(tile).Play.Contains(p) ? MediaCommand.PlayPause : null;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var l = Layout(r);
        double h = r.Height;
        DrawLines(dc, l.Text, p, Math.Min(13, h * 0.23), Math.Min(11.5, h * 0.19));
        var c = new Point(l.Play.X + l.Play.Width / 2 - h * 0.04, l.Play.Y + h / 2);
        dc.DrawEllipse(T == null ? p.Track : p.Accent, null, c, h * 0.27, h * 0.27);
        Glyphs.PlayPause(dc, c, h * 0.14, T == null ? p.SubInk : Brushes.White, Playing);
    }
}

/// <summary>A round play/pause button inside a ring showing the track's progress. A click plays or pauses.</summary>
internal sealed class NowPlayingMini : NowPlayingTile
{
    protected override bool ShowsProgress => true;
    public override bool HasCard => false;
    protected override MediaCommand? ZoneAt(Point p, Rect tile) => T != null ? MediaCommand.PlayPause : null;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height;
        var c = new Point(r.X + r.Width / 2, r.Y + h / 2);
        Ring(dc, c, h * 0.36, T?.Progress ?? 0, p.Accent, p, Math.Max(3, h * 0.07));
        dc.DrawEllipse(T == null ? p.Track : p.Ink, null, c, h * 0.24, h * 0.24);
        Glyphs.PlayPause(dc, c, h * 0.12, T == null ? p.SubInk : p.Tile, Playing);
    }
}

/// <summary>The title over a progress bar, with a skip arrow; clicking elsewhere plays or pauses.</summary>
internal sealed class NowPlayingBar : NowPlayingTile
{
    public override double WidthUnits => 3;
    protected override bool ShowsProgress => true;
    public override bool HasCard => false;

    private static (Rect Body, Rect Skip) Layout(Rect r)
    {
        double h = r.Height;
        var skip = new Rect(r.Right - h * 0.7, r.Y, h * 0.7, h);
        return (new Rect(r.X, r.Y, skip.X - r.X, h), skip);
    }

    protected override MediaCommand? ZoneAt(Point p, Rect tile) =>
        T == null ? null : Layout(tile).Skip.Contains(p) ? MediaCommand.Next : MediaCommand.PlayPause;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var l = Layout(r);
        double h = r.Height, pad = h * 0.18;
        // A small play/pause mark shows what a click will do.
        var mark = new Point(r.X + pad + h * 0.08, r.Y + h * 0.36);
        Glyphs.PlayPause(dc, mark, h * 0.09, T == null ? p.SubInk : p.Accent, Playing);
        var text = new Rect(mark.X + h * 0.18, r.Y + h * 0.14, l.Body.Right - mark.X - h * 0.18 - pad * 0.3, h * 0.44);
        DrawLines(dc, text, p, Math.Min(12.5, h * 0.22), 0, artist: false);
        var t = T;
        Bar(dc, new Rect(r.X + pad, r.Y + h * 0.64, l.Body.Width - pad * 1.3, Math.Max(3, h * 0.07)), t?.Progress ?? 0, p.Accent, p);
        if (t != null && t.Duration > TimeSpan.Zero)
        {
            var left = Text("−" + Clock(t.Duration - t.PositionNow), Math.Max(7, h * 0.13), p.SubInk, p, FontWeights.Normal);
            dc.DrawText(left, new Point(r.X + l.Body.Width - pad * 0.3 - left.Width, r.Y + h * 0.74));
        }
        Glyphs.Skip(dc, new Point(l.Skip.X + l.Skip.Width * 0.4, r.Y + h / 2), h * 0.14, Ink(p, t?.CanNext == true), forward: true);
    }

    public static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
