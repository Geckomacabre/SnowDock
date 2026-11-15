using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Media;

namespace SnowDock.Widgets.SystemPlus;

/// <summary>
/// Wi-Fi, Bluetooth, mobile hotspot and airplane mode as four round buttons. Airplane mode has no public API,
/// so its button opens the Settings page instead.
/// </summary>
internal sealed class QuickTogglesWidget : DockWidget
{
    private static readonly QuickKind[] Order = { QuickKind.Wifi, QuickKind.Bluetooth, QuickKind.Hotspot, QuickKind.Airplane };
    private int _seen = -1;

    public override double WidthUnits => 2.6;
    public override bool HasCard => true;

    public override string Title => RadioService.Ready
        ? $"Wi-Fi {Word(RadioService.Wifi)} · Bluetooth {Word(RadioService.Bluetooth)} · Hotspot {Word(RadioService.Hotspot)}"
        : "Quick Toggles";

    private static string Word(Switch s) => s switch
    {
        Switch.On => "on", Switch.Off => "off", Switch.Busy => "switching…", Switch.Missing => "unavailable",
        Switch.Denied => "blocked", _ => "unknown"
    };

    private static string Name(QuickKind k) => k switch
    {
        QuickKind.Wifi => "Wi-Fi", QuickKind.Bluetooth => "Bluetooth", QuickKind.Hotspot => "Mobile hotspot", _ => "Airplane mode"
    };

    public override bool Tick(DateTime now)
    {
        RadioService.Request();
        if (_seen == RadioService.Version) return false;
        _seen = RadioService.Version;
        return true;
    }

    /// <summary>Each quarter of the tile is one button. One that can't be switched opens the card, which says why.</summary>
    public override void OnClickAt(Point inTile, Size tile)
    {
        var k = Order[Math.Clamp((int)(inTile.X / (tile.Width / 4)), 0, 3)];
        if (k != QuickKind.Airplane && RadioService.Get(k) is not (Switch.On or Switch.Off)) OnClick();
        else Activate(k);
        Host?.Redraw();
    }

    private static void Activate(QuickKind k)
    {
        var s = RadioService.Get(k);
        if (k == QuickKind.Airplane) DockWindow.ShellOpen("ms-settings:network-airplanemode");
        else if (s is Switch.On or Switch.Off) RadioService.Toggle(k);
        else if (s == Switch.Denied) DockWindow.ShellOpen("ms-settings:privacy-radios");
        else if (s == Switch.Missing) DockWindow.ShellOpen(k == QuickKind.Hotspot ? "ms-settings:network-mobilehotspot" : "ms-settings:bluetooth");
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double cell = r.Width / 4, rad = Math.Min(r.Height * 0.3, cell * 0.4);
        for (int i = 0; i < 4; i++)
        {
            var c = new Point(r.X + cell * (i + 0.5), r.Y + r.Height / 2);
            var k = Order[i];
            var s = RadioService.Get(k);
            bool on = s == Switch.On, busy = s == Switch.Busy, off = k == QuickKind.Airplane || s is Switch.Off or Switch.Unknown;
            Brush fill = on ? p.Accent : p.Track;
            if (busy) { fill = p.Accent.Clone(); fill.Opacity = 0.45; }
            dc.DrawEllipse(fill, null, c, rad, rad);
            Brush ink = on || busy ? Brushes.White : off ? p.Ink : p.SubInk;
            double g = rad * 0.55;
            switch (k)
            {
                case QuickKind.Wifi: Glyphs.Wifi(dc, new Point(c.X, c.Y + g * 0.05), g, ink); break;
                case QuickKind.Bluetooth: Glyphs.Bluetooth(dc, c, g * 1.12, ink); break;
                case QuickKind.Hotspot: Glyphs.Hotspot(dc, c, g * 1.15, ink); break;
                default: Glyphs.Airplane(dc, c, g, ink); break;
            }
            // Unavailable or blocked: a diagonal strike through the button.
            if (s is Switch.Missing or Switch.Denied)
                dc.DrawLine(Glyphs.Stroke(p.SubInk, Math.Max(1.2, rad * 0.1)), new Point(c.X - rad * 0.6, c.Y + rad * 0.6), new Point(c.X + rad * 0.6, c.Y - rad * 0.6));
        }
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 300 };
        var rows = new StackPanel();
        panel.Children.Add(rows);
        void Fill()
        {
            rows.Children.Clear();
            foreach (var k in Order)
            {
                var s = RadioService.Get(k);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                string action = k == QuickKind.Airplane ? "Settings…" : s switch
                {
                    Switch.On => "Turn off", Switch.Off => "Turn on", Switch.Denied => "Privacy…", Switch.Missing => "Settings…", _ => ""
                };
                if (action.Length > 0)
                {
                    var b = new Button { Content = action, Padding = new Thickness(10, 1, 10, 1), MinWidth = 76 };
                    b.Click += (_, _) => { Activate(k); Host?.Redraw(); if (action.EndsWith('…')) Host?.CloseCard(); };
                    DockPanel.SetDock(b, Dock.Right);
                    row.Children.Add(b);
                }
                string state = k == QuickKind.Airplane ? "no switch for apps" : !RadioService.Ready ? "reading…" : Word(s);
                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                label.Inlines.Add(new System.Windows.Documents.Run(Name(k)) { FontWeight = FontWeights.SemiBold });
                label.Inlines.Add(new System.Windows.Documents.Run("  " + state) { Foreground = p.SubInk });
                row.Children.Add(label);
                rows.Children.Add(row);
            }
            if (RadioService.HotspotNote.Length > 0)
                rows.Children.Add(Note("Hotspot: " + RadioService.HotspotNote));
            rows.Children.Add(Note("Windows doesn't let apps switch airplane mode, so its button opens the Settings page."));
        }
        Fill();
        Action changed = Fill;
        RadioService.Changed += changed;
        panel.Unloaded += (_, _) => RadioService.Changed -= changed;
        return panel;
    }

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) };
}
