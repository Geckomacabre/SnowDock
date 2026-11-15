using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnowDock.Widgets.Media;

[WidgetFamily(36)]
internal static class AudioFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Media;
        add("audio-output", "Volume", c,
            "The speakers' volume as a ring: click the speaker to mute, the ring for a slider, or scroll over it to adjust.",
            () => new AudioOutputWidget());
        add("microphone", "Mic & Sound", c, "Two buttons in one tile: mute the default microphone, and mute the sound.",
            () => new MicrophoneWidget());
    }
}

/// <summary>Shared bits of the two audio tiles.</summary>
internal abstract class AudioTile : DockWidget
{
    private int _seen = -1;

    public override bool Tick(DateTime now)
    {
        AudioService.Request();
        if (_seen == AudioService.Version) return false;
        _seen = AudioService.Version;
        return true;
    }

    protected static int Percent(Endpoint e) => (int)Math.Round(e.Volume * 100);
}

internal sealed class AudioOutputWidget : AudioTile
{
    public override bool HasCard => true;

    public override string Title
    {
        get
        {
            var s = AudioService.Speakers;
            if (!AudioService.Ready) return "Volume";
            if (!s.Present) return "No speakers found";
            return s.Muted ? $"Volume {Percent(s)}% (muted)" : $"Volume {Percent(s)}%";
        }
    }

    /// <summary>The speaker in the middle mutes; the rest of the tile (the ring) opens the slider card.</summary>
    public override void OnClickAt(Point inTile, Size tile)
    {
        var s = AudioService.Speakers;
        var c = new Point(tile.Width / 2, tile.Height / 2);
        bool onSpeaker = (inTile - c).Length <= tile.Height * 0.27;
        if (onSpeaker && s.Present)
        {
            AudioService.SetMute(capture: false, !s.Muted);
            Host?.Redraw();
        }
        else OnClick();
    }

    public override bool OnWheel(int delta)
    {
        var s = AudioService.Speakers;
        if (!s.Present) return false;
        AudioService.SetVolume(s.Volume + delta / 120f * 0.02f);
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var s = AudioService.Speakers;
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double h = r.Height;
        Ring(dc, c, h * 0.37, s.Present ? s.Volume : 0, s.Muted ? p.SubInk : p.Accent, p, Math.Max(3, h * 0.075));
        if (!AudioService.Ready || !s.Present)
        {
            Glyphs.Speaker(dc, new Point(c.X, c.Y - h * 0.05), h * 0.14, p.SubInk, 0, muted: true, p.SubInk);
            Centered(dc, AudioService.Ready ? "none" : "…", new Point(c.X, c.Y + h * 0.16), h * 0.12, p.SubInk, p);
            return;
        }
        int waves = s.Volume <= 0.01 ? 0 : s.Volume < 0.34 ? 1 : s.Volume < 0.67 ? 2 : 3;
        Glyphs.Speaker(dc, new Point(c.X, c.Y - h * 0.07), h * 0.13, s.Muted ? p.SubInk : p.Ink, waves, s.Muted, p.Bad);
        Centered(dc, s.Muted ? "MUTED" : $"{Percent(s)}%", new Point(c.X, c.Y + h * 0.15), h * (s.Muted ? 0.1 : 0.14),
            s.Muted ? p.Bad : p.Ink, p, h * 0.5);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 270 };
        var name = new TextBlock { Opacity = 0.75, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new DockPanel();
        var value = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(value, Dock.Right);
        var slider = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        row.Children.Add(value);
        row.Children.Add(slider);
        var mute = new CheckBox { Content = "Mute", Margin = new Thickness(0, 10, 0, 0), Foreground = p.Ink };
        var settings = new Button { Content = "Sound settings…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

        void Show()
        {
            var s = AudioService.Speakers;
            name.Text = s.Present ? s.Name : AudioService.Ready ? "No speakers or headphones found." : "Reading…";
            if (!slider.IsMouseCaptureWithin) slider.Value = Percent(s);
            value.Text = $"{Percent(s)}%";
            mute.IsChecked = s.Muted;
            slider.IsEnabled = mute.IsEnabled = s.Present;
        }
        // Fill the controls first, then listen: building the card must never change the volume.
        Show();
        slider.ValueChanged += (_, e) =>
        {
            value.Text = $"{e.NewValue:0}%";
            if (Math.Abs(e.NewValue - Percent(AudioService.Speakers)) >= 0.5) AudioService.SetVolume((float)(e.NewValue / 100));
            Host?.Redraw();
        };
        mute.Click += (_, _) => { AudioService.SetMute(capture: false, mute.IsChecked == true); Host?.Redraw(); };
        settings.Click += (_, _) => { DockWindow.ShellOpen("ms-settings:sound"); Host?.CloseCard(); };
        Action changed = () => Show();
        AudioService.Changed += changed;
        panel.Unloaded += (_, _) => AudioService.Changed -= changed;

        panel.Children.Add(name);
        panel.Children.Add(row);
        panel.Children.Add(mute);
        panel.Children.Add(settings);
        return panel;
    }
}

/// <summary>One tile, two buttons: the microphone on the left, the sound on the right. Each click mutes or unmutes.</summary>
internal sealed class MicrophoneWidget : AudioTile
{
    public override double WidthUnits => 1.8;

    public override string Title
    {
        get
        {
            if (!AudioService.Ready) return "Microphone and sound";
            var m = AudioService.Microphone;
            var s = AudioService.Speakers;
            string mic = !m.Present ? "no microphone" : m.Muted ? "microphone muted" : "microphone on";
            string snd = !s.Present ? "no speakers" : s.Muted ? "sound muted" : "sound on";
            return $"{char.ToUpper(mic[0])}{mic[1..]} · {snd} (click either to mute)";
        }
    }

    public override void OnClickAt(Point inTile, Size tile)
    {
        bool mic = inTile.X < tile.Width / 2;
        var e = mic ? AudioService.Microphone : AudioService.Speakers;
        if (!e.Present) { DockWindow.ShellOpen("ms-settings:sound"); return; }
        AudioService.SetMute(mic, !e.Muted);
        Host?.Redraw();
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height, rad = Math.Min(h * 0.33, r.Width * 0.2);
        var left = new Point(r.X + r.Width * 0.28, r.Y + h / 2);
        var right = new Point(r.X + r.Width * 0.72, r.Y + h / 2);
        Button(dc, p, left, rad, AudioService.Microphone, mic: true);
        Button(dc, p, right, rad, AudioService.Speakers, mic: false);
    }

    private static void Button(DrawingContext dc, WidgetPaint p, Point c, double rad, Endpoint e, bool mic)
    {
        bool muted = e.Present && e.Muted;
        dc.DrawEllipse(muted ? p.Bad : p.Track, null, c, rad, rad);
        var ink = !e.Present ? p.SubInk : muted ? Brushes.White : p.Ink;
        if (mic) Glyphs.Mic(dc, c, rad * 0.55, ink, muted || !e.Present, muted ? Brushes.White : p.SubInk, muted ? p.Bad : p.Track);
        else Glyphs.Speaker(dc, c, rad * 0.52, ink, e.Present ? 2 : 0, muted || !e.Present, muted ? Brushes.White : p.SubInk);
    }
}
