using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnowDock.Widgets.Online;

/// <summary>
/// A widget that fetches data from the web in the background on a schedule. The fetch runs off the UI thread;
/// its result (or a short error) is handed back to the dock, which redraws only when something arrived.
///
/// Until the widget can fetch (no key yet, no city chosen), it shows <see cref="Sample"/> data, always drawn
/// with a "SAMPLE" mark so made-up numbers never pass for real ones.
/// </summary>
internal abstract class OnlineWidget<T> : DockWidget where T : class
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource _cts = new();
    private bool _busy, _changed;
    private DateTime _lastFetch = DateTime.MinValue;

    /// <summary>The latest real data (null until the first successful fetch).</summary>
    protected T? Data { get; private set; }
    /// <summary>Why the last fetch failed, or null.</summary>
    protected string? Error { get; private set; }
    protected DateTime? UpdatedAt { get; private set; }

    /// <summary>How often to refresh. Keep it gentle: most services rate-limit.</summary>
    protected virtual TimeSpan Interval => TimeSpan.FromMinutes(15);
    /// <summary>False until the widget has what it needs (a key, a city…).</summary>
    protected virtual bool CanFetch => true;
    /// <summary>Made-up data shown, marked "SAMPLE", until <see cref="CanFetch"/>.</summary>
    protected virtual T? Sample => null;

    protected abstract Task<T> FetchAsync(CancellationToken ct);

    /// <summary>What to draw: real data when connected, otherwise the sample.</summary>
    protected T? Shown => CanFetch ? Data : Sample;
    protected bool IsSample => !CanFetch;

    public override bool Tick(DateTime now)
    {
        if (CanFetch && !_busy && (now - _lastFetch) >= (Data == null && Error == null ? TimeSpan.Zero : Interval))
            StartFetch();
        bool changed = _changed;
        _changed = false;
        return changed | TickLive(now);
    }

    /// <summary>For widgets whose tile also changes between fetches (a ticking chart, a clock).</summary>
    protected virtual bool TickLive(DateTime now) => false;

    /// <summary>Fetch again on the next tick (after a key or setting changed).</summary>
    public void RefreshSoon()
    {
        _lastFetch = DateTime.MinValue;
        Data = null;
        Error = null;
        _changed = true;
    }

    private void StartFetch()
    {
        _busy = true;
        _lastFetch = DateTime.Now;
        var ct = _cts.Token;
        Task.Run(async () =>
        {
            try
            {
                var result = await FetchAsync(ct).ConfigureAwait(false);
                _ = _ui.BeginInvoke(() => { Data = result; Error = null; UpdatedAt = DateTime.Now; Done(); });
            }
            catch (OperationCanceledException) { _ = _ui.BeginInvoke(Done); }
            catch (Exception ex)
            {
                AppPaths.Log($"{Config.Type} fetch failed: {ex.Message}");
                _ = _ui.BeginInvoke(() => { Error = Web.Friendly(ex); Done(); });
            }
        }, ct);

        void Done()
        {
            _busy = false;
            _changed = true;
            Host?.Redraw();
        }
    }

    public override void OnRemoved()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
    }

    // ---- drawing helpers ----

    /// <summary>The small "SAMPLE" mark in a tile's top-right corner.</summary>
    protected static void SampleMark(DrawingContext dc, Rect r, WidgetPaint p)
    {
        var t = Text("SAMPLE", Math.Max(7, r.Height * 0.1), p.Warn, p, FontWeights.Bold);
        dc.DrawText(t, new Point(r.Right - t.Width - r.Height * 0.12, r.Y + r.Height * 0.07));
    }

    /// <summary>Draw sample/error/loading state common to all online tiles. Returns false if there's nothing to draw yet.</summary>
    protected bool DrawState(DrawingContext dc, Rect r, WidgetPaint p)
    {
        if (IsSample) { SampleMark(dc, r, p); return Shown != null; }
        if (Shown != null) return true;
        Centered(dc, Error ?? "Loading…", new Point(r.X + r.Width / 2, r.Y + r.Height / 2), Math.Min(12, r.Height * 0.17),
            Error != null ? p.Bad : p.SubInk, p, r.Width - 10, FontWeights.Normal);
        return false;
    }
}
