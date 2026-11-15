using System.Windows;
using System.Windows.Threading;
using SnowDock.Widgets;

namespace SnowDock;

/// <summary>Widgets in the dock: their items, the shared one-second tick, cards and the picker.</summary>
internal sealed partial class DockWindow : IWidgetHost
{
    private readonly Dictionary<WidgetConfig, DockItem> _widgetItems = new();
    private DispatcherTimer? _widgetTimer;
    private CardWindow? _card;
    private PickerWindow? _picker;

    /// <summary>Widget items in config order. Side docks don't show widgets (there's no room for wide tiles).</summary>
    private IEnumerable<DockItem> WidgetItems()
    {
        foreach (var gone in _widgetItems.Keys.Where(c => !_config.Widgets.Contains(c)).ToList())
        {
            _widgetItems[gone].Widget?.OnRemoved();
            _widgetItems.Remove(gone);
        }
        foreach (var cfg in _config.Widgets)
        {
            if (!_widgetItems.TryGetValue(cfg, out var item))
            {
                var widget = WidgetCatalog.Create(cfg);
                if (widget == null) continue; // a type from a newer/older build
                widget.Host = this;
                widget.Tick(DateTime.Now);
                item = new DockItem { Kind = DockItemKind.Widget, Widget = widget, Name = widget.Title };
                _widgetItems[cfg] = item;
            }
            if (_config.Edge is DockEdge.Bottom or DockEdge.Top) yield return item;
        }
    }

    private void StartWidgetTimer()
    {
        _widgetTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _widgetTimer.Tick -= OnWidgetTick;
        _widgetTimer.Tick += OnWidgetTick;
        _widgetTimer.Start();
    }

    private void OnWidgetTick(object? sender, EventArgs e)
    {
        if (_widgetItems.Count == 0) return;
        var now = DateTime.Now;
        bool changed = false;
        foreach (var item in _surface.Items.Where(i => i.Kind == DockItemKind.Widget))
        {
            try { changed |= item.Widget!.Tick(now); }
            catch (Exception ex) { AppPaths.Log($"Widget {item.Widget?.Config.Type} tick failed: {ex.Message}"); }
            item.Name = item.Widget!.Title;
        }
        // Only redraw when something visible changed (and the dock isn't tucked away).
        if (changed && _surface.HideAmount < 0.999) _surface.InvalidateVisual();
    }

    private bool IsWidgetAdded(string type) => _config.Widgets.Any(w => w.Type == type);

    /// <summary>Picker: add a widget at the end of the widget section, or take it off if it's there.</summary>
    private void ToggleWidget(string type)
    {
        var existing = _config.Widgets.FirstOrDefault(w => w.Type == type);
        if (existing != null) RemoveWidget(existing);
        else
        {
            _config.Widgets.Add(new WidgetConfig { Type = type });
            _config.Save();
            RebuildItems();
        }
    }

    private void RemoveWidget(WidgetConfig config)
    {
        if (_card?.Widget?.Config == config) _card.CloseCard();
        _config.Widgets.Remove(config);
        _config.Save();
        RebuildItems();
    }

    private void ShowPicker()
    {
        if (_picker is { IsOpen: true })
        {
            _picker.Activate();
            return;
        }
        if (_config.Edge is DockEdge.Left or DockEdge.Right)
            AppPaths.Log("Widgets show on top and bottom docks only.");
        _picker = new PickerWindow(IsWidgetAdded, ToggleWidget);
        _picker.PickerClosed += () =>
        {
            _surface.EditMode = false;
            _surface.InvalidateVisual();
        };
        _surface.EditMode = true;
        _surface.InvalidateVisual();
        _picker.ShowOn(TargetMonitor, DpiScale);
    }

    /// <summary>Drop a dragged widget: reorder within the widget section, or remove it if dropped off the dock.</summary>
    private void EndWidgetDrag(DragState d, bool cancel)
    {
        var config = d.Item.Widget!.Config;
        bool poof = false;
        if (!cancel && d.OverDock && d.InsertIndex >= 0)
        {
            // The widgets still in the row, with the dragged one put back at its new place.
            var order = _surface.Items.Where(i => i.Kind == DockItemKind.Widget).Select(i => i.Widget!.Config).ToList();
            int start = _surface.SeparatorIndex >= 0 ? _surface.SeparatorIndex : _surface.Items.Count;
            order.Insert(Math.Clamp(d.InsertIndex - start, 0, order.Count), config);
            order.AddRange(_config.Widgets.Where(w => !order.Contains(w)));
            _config.Widgets.Clear();
            _config.Widgets.AddRange(order);
            _config.Save();
        }
        else if (!cancel)
        {
            poof = true;
            _config.Widgets.Remove(config);
            _config.Save();
        }

        RebuildItems();
        _surface.ResetGaps();
        if (poof)
        {
            _ghost!.Poof();
            EnsureAnimating();
            return;
        }
        if (!_widgetItems.TryGetValue(config, out var target)) { _ghost!.Hide(); return; }
        _surface.HiddenItem = target;
        _surface.LayoutItems();
        var px = _surface.ScreenPoint(target.CenterX, _surface.Baseline - target.Size / 2);
        _ghost!.FlyTo(px, target.Size, () => { _surface.HiddenItem = null; _surface.InvalidateVisual(); });
        EnsureAnimating();
    }

    /// <summary>The widget as a picture (for the drag ghost), at its current size.</summary>
    private System.Windows.Media.Imaging.BitmapSource RenderWidgetImage(DockItem item)
    {
        double w = Math.Max(1, item.Width), h = Math.Max(1, item.Size), scale = 2;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            item.Widget!.Draw(dc, new Rect(0, 0, w, h), _surface.Paint);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale,
            System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ---- IWidgetHost ----

    public void SaveConfig() => _config.Save();
    public void Redraw() => _surface.InvalidateVisual();

    public void ShowCard(DockWidget widget)
    {
        var item = _surface.Items.FirstOrDefault(i => ReferenceEquals(i.Widget, widget));
        if (item == null) return;
        if (_card == null)
        {
            _card = new CardWindow();
            _card.CardClosed += () => { _surface.LabelSuppressedFor = null; _surface.InvalidateVisual(); };
        }
        _surface.LayoutItems();
        var anchor = _surface.ScreenPoint(item.CenterX, _surface.Baseline - item.Size - 4);
        _surface.LabelSuppressedFor = item;
        _card.Open(widget, WidgetPaint.For(_surface.Look.LightTiles, VisualTreeHelperDpi), anchor,
            above: _config.Edge != DockEdge.Top, DpiScale, TargetMonitor);
    }

    public void CloseCard() => _card?.CloseCard();

    public void Attention(DockWidget widget)
    {
        var item = _surface.Items.FirstOrDefault(i => ReferenceEquals(i.Widget, widget));
        if (item == null) return;
        item.StartAttentionBounce();
        EnsureAnimating();
    }

    private double VisualTreeHelperDpi => System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;

    /// <summary>A click on a widget tile: remove it if the badge was hit in edit mode, otherwise its own action.</summary>
    private void OnWidgetClicked(DockItem item, Point dockPoint)
    {
        if (_surface.EditMode && _surface.HitsRemoveBadge(item, dockPoint))
        {
            RemoveWidget(item.Widget!.Config);
            return;
        }
        if (_card is { IsOpen: true } && ReferenceEquals(_card.Widget, item.Widget))
        {
            _card.CloseCard();
            return;
        }
        try { item.Widget!.OnClick(); }
        catch (Exception ex) { AppPaths.Log($"Widget {item.Widget?.Config.Type} click failed: {ex.Message}"); }
        _surface.InvalidateVisual();
    }
}
