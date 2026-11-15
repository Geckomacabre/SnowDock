using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnowDock.Widgets.Media;

namespace SnowDock.Widgets.SystemPlus;

/// <summary>Screen brightness: a sun and the level; the card has a slider for each screen that answers.</summary>
internal sealed class BrightnessWidget : DockWidget
{
    private int _seen = -1;
    public override bool HasCard => true;

    private static Screen? Main => BrightnessService.Screens.FirstOrDefault();

    public override string Title => !BrightnessService.Ready ? "Brightness"
        : BrightnessService.Screens.Count == 0 ? "Brightness: not supported on these screens"
        : "Brightness " + string.Join(" · ", BrightnessService.Screens.Select(s => BrightnessService.Screens.Count > 1 ? $"{s.Name} {s.Percent}%" : $"{s.Percent}%"));

    public override bool Tick(DateTime now)
    {
        BrightnessService.Request();
        if (_seen == BrightnessService.Version) return false;
        _seen = BrightnessService.Version;
        return true;
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        double h = r.Height;
        var c = new Point(r.X + r.Width / 2, r.Y + h * 0.4);
        var main = Main;
        Glyphs.Sun(dc, c, h * (main == null ? 0.2 : 0.16 + 0.08 * main.Percent / 100.0), main == null ? p.SubInk : p.Warn);
        string text = main != null ? $"{main.Percent}%" : BrightnessService.Ready ? "n/a" : "…";
        Centered(dc, text, new Point(c.X, r.Bottom - h * 0.19), h * 0.17, main == null ? p.SubInk : p.Ink, p, r.Width - 8);
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 280 };
        void Fill()
        {
            // Don't rebuild under a slider that's being dragged.
            if (panel.Children.OfType<DockPanel>().Any(d => d.IsMouseCaptureWithin)) return;
            panel.Children.Clear();
            if (!BrightnessService.Ready) { panel.Children.Add(new TextBlock { Text = "Asking the screens…", Opacity = 0.7 }); return; }
            if (BrightnessService.Screens.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Not supported: none of these screens let apps set brightness (external monitors need DDC/CI switched on in their own menu).",
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8
                });
                return;
            }
            foreach (var s in BrightnessService.Screens)
            {
                panel.Children.Add(new TextBlock { Text = s.Name + (s.BuiltIn ? "" : "  (DDC/CI)"), Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var value = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = $"{s.Percent}%" };
                DockPanel.SetDock(value, Dock.Right);
                // The value is set before listening, so opening the card never changes a screen.
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = s.Percent, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
                string key = s.Key;
                slider.ValueChanged += (_, e) =>
                {
                    value.Text = $"{e.NewValue:0}%";
                    BrightnessService.Set(key, (int)Math.Round(e.NewValue));
                    Host?.Redraw();
                };
                row.Children.Add(value);
                row.Children.Add(slider);
                panel.Children.Add(row);
            }
        }
        Fill();
        BrightnessService.Request(now: true);
        Action changed = Fill;
        BrightnessService.Changed += changed;
        panel.Unloaded += (_, _) => BrightnessService.Changed -= changed;
        return panel;
    }
}
