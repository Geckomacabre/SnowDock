using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnowDock.Native;

namespace SnowDock.Stacks;

internal sealed class StackEntry
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool IsFolder { get; init; }

    private BitmapSource? _image;
    /// <summary>
    /// Files: a thumbnail when Windows has one (photos, PDFs, videos), otherwise the file's icon.
    /// Folders: the plain folder icon (Windows' folder "thumbnails" are white tiles that look wrong here).
    /// </summary>
    public BitmapSource Image => _image ??=
        (IsFolder ? IconLoader.ForPath(Path) : IconLoader.ForFile(Path)) ?? IconLoader.Placeholder();
}

/// <summary>
/// One stack: a watched folder, its items in the chosen order, and the dock icon (a little pile of
/// the newest items, or the folder's own icon).
/// </summary>
internal sealed class StackFolder : IDisposable
{
    private const int MaxEntries = 40;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;

    public StackConfig Config { get; }
    public IReadOnlyList<StackEntry> Entries { get; private set; } = Array.Empty<StackEntry>();
    public int TotalCount { get; private set; }
    public BitmapSource Icon { get; private set; } = IconLoader.Placeholder();
    public bool Exists => Directory.Exists(Config.Path);
    public event Action? Changed;

    public StackFolder(StackConfig config)
    {
        Config = config;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
        Refresh();
        Watch();
    }

    public void Refresh()
    {
        (Entries, TotalCount) = Enumerate(Config.Path, Config.Sort, MaxEntries);
        Icon = BuildIcon();
        Changed?.Invoke();
    }

    /// <summary>
    /// A folder's visible items in stack order (hidden and system files skipped), at most
    /// <paramref name="max"/> of them, plus the total count. Also used by the grid and list views.
    /// </summary>
    public static (List<StackEntry> Entries, int Total) Enumerate(string path, StackSort sort, int max = int.MaxValue)
    {
        List<FileSystemInfo> infos;
        try
        {
            infos = new DirectoryInfo(path).EnumerateFileSystemInfos()
                .Where(i => (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
                            && !i.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Folder '{path}' unreadable: {ex.Message}");
            infos = new List<FileSystemInfo>();
        }

        var entries = Sorted(infos, sort).Take(max).Select(i => new StackEntry
        {
            Path = i.FullName,
            IsFolder = i is DirectoryInfo,
            // Explorer hides known extensions by default; so do we.
            Name = i is DirectoryInfo || System.IO.Path.GetFileNameWithoutExtension(i.Name).Length == 0
                ? i.Name
                : System.IO.Path.GetFileNameWithoutExtension(i.Name)
        }).ToList();
        return (entries, infos.Count);
    }

    /// <summary>Newest first for the date sorts, so the most recent item sits closest to the dock.</summary>
    private static IEnumerable<FileSystemInfo> Sorted(List<FileSystemInfo> items, StackSort sort) => sort switch
    {
        // Windows doesn't record "date added"; a file's creation time is when it arrived in this folder.
        StackSort.DateAdded => items.OrderByDescending(i => i.CreationTimeUtc),
        StackSort.DateModified => items.OrderByDescending(i => i.LastWriteTimeUtc),
        StackSort.Kind => items.OrderBy(i => i is DirectoryInfo ? "" : i.Extension.ToLowerInvariant())
                               .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
        _ => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
    };

    private BitmapSource BuildIcon()
    {
        if (Config.Display == StackDisplay.Folder || Entries.Count == 0)
            return IconLoader.ForPath(Config.Path) ?? IconLoader.Placeholder();
        return BuildPile(Entries.Take(3).Reverse().Select(e => e.Image).ToList());
    }

    /// <summary>The "stack" look: the newest items piled up, each turned a little, newest on top.</summary>
    private static BitmapSource BuildPile(IReadOnlyList<BitmapSource> images)
    {
        const int size = 256;
        // Bottom to top. The top (newest) item sits almost straight.
        double[] angles = { -9, 7, -2 };
        double[] shiftX = { -14, 12, 0 };
        double[] shiftY = { 6, 2, -4 };
        int offset = 3 - images.Count;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int i = 0; i < images.Count; i++)
            {
                int k = i + offset;
                double item = 196;
                var square = new Rect((size - item) / 2 + shiftX[k], size - item - 18 + shiftY[k], item, item);
                var rect = IconLoader.FitSquare(images[i], square);
                // Sit flat images on the bottom of their square, like things resting on a pile.
                rect.Y = square.Bottom - rect.Height;
                var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

                dc.PushTransform(new RotateTransform(angles[k], center.X, center.Y));
                bool photo = Math.Abs(images[i].PixelWidth - images[i].PixelHeight) > 2;
                if (photo)
                {
                    // Thumbnails get a thin white border and a soft edge so they read as separate sheets.
                    var border = rect;
                    border.Inflate(5, 5);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null,
                        new Rect(border.X + 2, border.Y + 4, border.Width, border.Height));
                    dc.DrawRectangle(Brushes.White, null, border);
                }
                dc.DrawImage(images[i], rect);
                dc.Pop();
            }
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void Watch()
    {
        if (!Exists) return;
        try
        {
            _watcher = new FileSystemWatcher(Config.Path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                               NotifyFilters.CreationTime | NotifyFilters.Attributes,
                IncludeSubdirectories = false
            };
            // Watcher events arrive on a worker thread; the icon cache and timer belong to the UI thread.
            FileSystemEventHandler onChange = (_, e) => _dispatcher.BeginInvoke(() =>
            {
                IconLoader.Forget(e.FullPath);
                _debounce.Stop();
                _debounce.Start();
            });
            _watcher.Created += onChange;
            _watcher.Deleted += onChange;
            _watcher.Changed += onChange;
            _watcher.Renamed += (s, e) => onChange(s, e);
            _watcher.Error += (_, e) => AppPaths.Log($"Stack watcher error for {Config.Path}: {e.GetException().Message}");
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Can't watch {Config.Path}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
