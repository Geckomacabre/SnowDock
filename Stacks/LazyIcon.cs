using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnowDock.Stacks;

/// <summary>
/// Loads stack item images one at a time at background priority, so a grid or list of hundreds of files
/// opens at once and the icons fill in as they load. Starting a new batch cancels the previous one.
/// </summary>
internal static class LazyIcon
{
    private static readonly Queue<(StackEntry Entry, Action<BitmapSource> Apply, int Generation)> Pending = new();
    private static int _generation;
    private static bool _running;

    /// <summary>Drop everything still queued (e.g. the grid navigated to another folder).</summary>
    public static void NewBatch()
    {
        _generation++;
        Pending.Clear();
    }

    public static void Load(StackEntry entry, Action<BitmapSource> apply)
    {
        Pending.Enqueue((entry, apply, _generation));
        if (_running) return;
        _running = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, Next);
    }

    /// <summary>Load everything queued right now (snapshots only; it blocks).</summary>
    public static void Flush()
    {
        while (Pending.Count > 0)
        {
            var (entry, apply, gen) = Pending.Dequeue();
            if (gen == _generation) apply(entry.Image);
        }
    }

    private static void Next()
    {
        while (Pending.Count > 0)
        {
            var (entry, apply, gen) = Pending.Dequeue();
            if (gen != _generation) continue;
            apply(entry.Image);
            // Yield after each image so input and rendering stay smooth.
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, Next);
            return;
        }
        _running = false;
    }
}
