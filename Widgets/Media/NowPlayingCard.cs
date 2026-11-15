using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnowDock.Widgets.Media;

/// <summary>The now-playing card: a large cover, the track's details, its progress and the three buttons.</summary>
internal static class NowPlayingCard
{
    public static FrameworkElement Build(WidgetPaint p, IWidgetHost? host)
    {
        var root = new Grid { Width = 330 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var art = new Image { Width = 92, Height = 92, Stretch = Stretch.UniformToFill };
        var artBox = new Border
        {
            Width = 92, Height = 92, CornerRadius = new CornerRadius(10), Background = p.Track, VerticalAlignment = VerticalAlignment.Top,
            Child = art, Clip = new RectangleGeometry(new Rect(0, 0, 92, 92), 10, 10)
        };
        root.Children.Add(artBox);

        var info = new StackPanel();
        Grid.SetColumn(info, 1);
        var title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var artist = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var source = new TextBlock { Opacity = 0.65, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = p.Track, Margin = new Thickness(0, 10, 0, 0) };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = p.Accent, HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = fill;
        var times = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        var elapsed = new TextBlock { FontSize = 10.5, Opacity = 0.65 };
        var remaining = new TextBlock { FontSize = 10.5, Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(elapsed, Dock.Left);
        times.Children.Add(elapsed);
        times.Children.Add(remaining);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var prev = Button(p, MediaCommand.Previous, host);
        var play = Button(p, MediaCommand.PlayPause, host);
        var next = Button(p, MediaCommand.Next, host);
        buttons.Children.Add(prev);
        buttons.Children.Add(play);
        buttons.Children.Add(next);

        foreach (var e in new UIElement[] { title, artist, source, track, times, buttons }) info.Children.Add(e);
        root.Children.Add(info);

        void Progress()
        {
            var t = MediaService.Current;
            bool timed = t != null && t.Duration > TimeSpan.Zero;
            track.Visibility = times.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
            if (!timed) return;
            fill.Width = Math.Max(4, (info.ActualWidth > 0 ? info.ActualWidth : 226) * t!.Progress);
            elapsed.Text = NowPlayingBar.Clock(t.PositionNow);
            remaining.Text = "−" + NowPlayingBar.Clock(t.Duration - t.PositionNow);
        }

        void Show()
        {
            var t = MediaService.Current;
            art.Source = MediaService.Art;
            title.Text = t?.Title is { Length: > 0 } s ? s : MediaService.Ready ? "Nothing playing" : "Loading…";
            artist.Text = t?.Artist ?? (MediaService.Ready ? "Play something in any app that shows in Windows' media controls." : "");
            artist.TextWrapping = t == null ? TextWrapping.Wrap : TextWrapping.NoWrap;
            source.Text = t == null ? "" : string.Join(" · ", new[] { t.Album, t.App }.Where(x => x.Length > 0));
            Paint(prev, p, MediaCommand.Previous, t?.CanPrevious == true, false);
            Paint(play, p, MediaCommand.PlayPause, t?.CanPlayPause == true, t?.Playing == true);
            Paint(next, p, MediaCommand.Next, t?.CanNext == true, false);
            buttons.Visibility = t == null ? Visibility.Collapsed : Visibility.Visible;
            Progress();
        }

        Show();
        Action changed = () => Show();
        MediaService.Changed += changed;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { MediaService.Request(); Progress(); };
        root.Loaded += (_, _) => { timer.Start(); Progress(); };
        root.Unloaded += (_, _) => { timer.Stop(); MediaService.Changed -= changed; };
        return root;
    }

    private static Border Button(WidgetPaint p, MediaCommand cmd, IWidgetHost? host)
    {
        var b = new Border
        {
            Width = cmd == MediaCommand.PlayPause ? 44 : 36, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand, Background = cmd == MediaCommand.PlayPause ? p.Accent : p.Track
        };
        b.MouseLeftButtonUp += (_, _) =>
        {
            if (!b.IsEnabled) return;
            MediaService.Send(cmd);
            host?.Redraw();
        };
        return b;
    }

    /// <summary>Draw the button's glyph for its current state.</summary>
    private static void Paint(Border b, WidgetPaint p, MediaCommand cmd, bool enabled, bool playing)
    {
        b.IsEnabled = enabled;
        b.Opacity = enabled ? 1 : 0.45;
        const double size = 22;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            var c = new Point(size / 2, size / 2);
            var ink = cmd == MediaCommand.PlayPause ? Brushes.White : p.Ink;
            if (cmd == MediaCommand.PlayPause) Glyphs.PlayPause(dc, c, 6.5, ink, playing);
            else Glyphs.Skip(dc, c, 6.5, ink, cmd == MediaCommand.Next);
        }
        var img = new DrawingImage(dv.Drawing);
        img.Freeze();
        b.Child = new Image { Source = img, Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
}
