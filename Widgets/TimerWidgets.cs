using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnowDock.Widgets;

/// <summary>A countdown: click to start or pause; its card sets the length.</summary>
internal sealed class TimerWidget : DockWidget
{
    private DateTime? _endsAt;
    private TimeSpan _remaining;
    private bool _done;
    private string _last = "";

    private TimeSpan Length => TimeSpan.FromMinutes(int.TryParse(Get("minutes", "10"), out var m) ? m : 10);
    public override string Title => _endsAt != null ? "Timer running (click to pause)" : _done ? "Timer done" : "Timer (click to start)";
    public override bool HasCard => true;

    private TimeSpan Left => _endsAt is DateTime end ? end - DateTime.Now : _remaining == default ? Length : _remaining;

    public override bool Tick(DateTime now)
    {
        if (_endsAt is DateTime end && now >= end)
        {
            _endsAt = null;
            _remaining = default;
            _done = true;
            SystemSounds.Exclamation.Play();
            Host?.Attention(this);
        }
        var key = $"{(int)Left.TotalSeconds}{_done}{_endsAt == null}";
        if (key == _last) return false;
        _last = key;
        return true;
    }

    public override void OnClick()
    {
        _done = false;
        if (_endsAt is DateTime end) { _remaining = end - DateTime.Now; _endsAt = null; }
        else _endsAt = DateTime.Now + (_remaining == default ? Length : _remaining);
        Host?.Redraw();
    }

    private void Reset() { _endsAt = null; _remaining = default; _done = false; Host?.Redraw(); }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var left = Left < TimeSpan.Zero ? TimeSpan.Zero : Left;
        double f = Length.TotalSeconds <= 0 ? 0 : left.TotalSeconds / Length.TotalSeconds;
        Ring(dc, c, r.Height * 0.38, _done ? 1 : f, _done ? p.Good : _endsAt != null ? p.Warn : p.Track, p, Math.Max(3, r.Height * 0.07));
        string text = _done ? "Done" : left.TotalHours >= 1 ? left.ToString(@"h\:mm") : left.ToString(@"m\:ss");
        Centered(dc, text, new Point(c.X, c.Y - r.Height * 0.03), r.Height * 0.2, p.Ink, p, r.Height * 0.6);
        Centered(dc, _endsAt != null ? "RUNNING" : "TIMER", new Point(c.X, c.Y + r.Height * 0.16), r.Height * 0.1, p.SubInk, p);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 260 };
        panel.Children.Add(new TextBlock { Text = "Length", Margin = new Thickness(0, 0, 0, 6) });
        var presets = new WrapPanel();
        foreach (int m in new[] { 1, 3, 5, 10, 15, 25, 30, 45, 60 })
        {
            var b = new Button { Content = $"{m} min", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += (_, _) => { Set("minutes", m.ToString()); Reset(); OnClick(); Host?.CloseCard(); };
            presets.Children.Add(b);
        }
        panel.Children.Add(presets);
        var reset = new Button { Content = "Reset", Padding = new Thickness(12, 2, 12, 2), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { Reset(); Host?.CloseCard(); };
        panel.Children.Add(reset);
        return panel;
    }
}

/// <summary>Start, pause and lap from the dock; the card keeps the laps.</summary>
internal sealed class StopwatchWidget : DockWidget
{
    private DateTime? _startedAt;
    private TimeSpan _banked;
    private readonly List<TimeSpan> _laps = new();
    private string _last = "";

    private TimeSpan Elapsed => _banked + (_startedAt is DateTime s ? DateTime.Now - s : TimeSpan.Zero);
    public override string Title => _startedAt != null ? "Stopwatch (click to pause)" : "Stopwatch (click to start)";
    public override double WidthUnits => 1.6;
    public override bool HasCard => true;

    public override bool Tick(DateTime now)
    {
        var key = $"{(int)Elapsed.TotalSeconds}{_startedAt == null}";
        if (key == _last) return false;
        _last = key;
        return true;
    }

    public override void OnClick()
    {
        if (_startedAt is DateTime s) { _banked += DateTime.Now - s; _startedAt = null; }
        else _startedAt = DateTime.Now;
        Host?.Redraw();
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var e = Elapsed;
        string t = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.ToString(@"mm\:ss");
        Centered(dc, t, new Point(r.X + r.Width / 2, r.Y + r.Height * 0.44), r.Height * 0.28, p.Ink, p, r.Width - 10, null, WidgetPaint.Mono);
        Centered(dc, _startedAt != null ? "● RUNNING" : _laps.Count > 0 ? $"{_laps.Count} LAPS" : "STOPWATCH",
            new Point(r.X + r.Width / 2, r.Bottom - r.Height * 0.2), r.Height * 0.11, _startedAt != null ? p.Bad : p.SubInk, p);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 240 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var lap = new Button { Content = "Lap", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0) };
        var reset = new Button { Content = "Reset", Padding = new Thickness(12, 2, 12, 2) };
        buttons.Children.Add(lap);
        buttons.Children.Add(reset);
        var list = new ListBox { MaxHeight = 220, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        void Fill()
        {
            list.Items.Clear();
            for (int i = _laps.Count - 1; i >= 0; i--)
                list.Items.Add(new TextBlock { Text = $"Lap {i + 1}    {_laps[i]:mm\\:ss\\.ff}", Foreground = p.Ink });
        }
        lap.Click += (_, _) => { _laps.Add(Elapsed); Fill(); };
        reset.Click += (_, _) => { _laps.Clear(); _banked = TimeSpan.Zero; _startedAt = null; Fill(); Host?.Redraw(); };
        Fill();
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        return panel;
    }
}

/// <summary>Focus rounds and breaks: 25 minutes on, 5 off, a longer break every fourth round.</summary>
internal sealed class PomodoroWidget : DockWidget
{
    private enum Phase { Focus, Break, LongBreak }
    private Phase _phase = Phase.Focus;
    private int _round = 1;
    private DateTime? _endsAt;
    private TimeSpan _remaining;
    private string _last = "";

    private TimeSpan Length(Phase ph) => TimeSpan.FromMinutes(ph switch
    {
        Phase.Focus => int.TryParse(Get("focus", "25"), out var f) ? f : 25,
        Phase.Break => int.TryParse(Get("break", "5"), out var b) ? b : 5,
        _ => int.TryParse(Get("long", "15"), out var l) ? l : 15
    });

    private TimeSpan Left => _endsAt is DateTime end ? end - DateTime.Now : _remaining == default ? Length(_phase) : _remaining;
    public override string Title => $"Pomodoro: {(_phase == Phase.Focus ? "focus" : "break")}, round {_round}";
    public override bool HasCard => true;

    public override bool Tick(DateTime now)
    {
        if (_endsAt is DateTime end && now >= end)
        {
            SystemSounds.Asterisk.Play();
            Host?.Attention(this);
            Advance(autoStart: true);
        }
        var key = $"{(int)Left.TotalSeconds}{_phase}{_endsAt == null}";
        if (key == _last) return false;
        _last = key;
        return true;
    }

    private void Advance(bool autoStart)
    {
        if (_phase == Phase.Focus) _phase = _round % 4 == 0 ? Phase.LongBreak : Phase.Break;
        else { _phase = Phase.Focus; _round++; }
        _remaining = default;
        _endsAt = autoStart ? DateTime.Now + Length(_phase) : null;
        Host?.Redraw();
    }

    public override void OnClick()
    {
        if (_endsAt is DateTime end) { _remaining = end - DateTime.Now; _endsAt = null; }
        else _endsAt = DateTime.Now + (_remaining == default ? Length(_phase) : _remaining);
        Host?.Redraw();
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var left = Left < TimeSpan.Zero ? TimeSpan.Zero : Left;
        double f = 1 - left.TotalSeconds / Math.Max(1, Length(_phase).TotalSeconds);
        Ring(dc, c, r.Height * 0.38, f, _phase == Phase.Focus ? p.Bad : p.Good, p, Math.Max(3, r.Height * 0.07));
        Centered(dc, left.ToString(@"m\:ss"), new Point(c.X, c.Y - r.Height * 0.03), r.Height * 0.2, p.Ink, p, r.Height * 0.6);
        Centered(dc, _phase == Phase.Focus ? $"FOCUS {_round}" : "BREAK", new Point(c.X, c.Y + r.Height * 0.16), r.Height * 0.1, p.SubInk, p);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 250 };
        panel.Children.Add(new TextBlock { Text = $"Round {_round} · {(_phase == Phase.Focus ? "Focus" : "Break")}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var skip = new Button { Content = "Skip", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0) };
        var reset = new Button { Content = "Reset", Padding = new Thickness(12, 2, 12, 2) };
        skip.Click += (_, _) => { Advance(autoStart: false); Host?.CloseCard(); };
        reset.Click += (_, _) => { _phase = Phase.Focus; _round = 1; _endsAt = null; _remaining = default; Host?.Redraw(); Host?.CloseCard(); };
        row.Children.Add(skip);
        row.Children.Add(reset);
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = "Click the tile to start or pause. Lengths are in config.json (focus, break, long).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 10, 0, 0), FontSize = 11.5
        });
        return panel;
    }
}
