using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnowDock.Expose;
using SnowDock.Native;

namespace SnowDock;

/// <summary>Dock Exposé: click and hold a running app's icon to see all of its windows.</summary>
internal sealed partial class DockWindow
{
    private void ShowExpose(DockItem item)
    {
        var app = item.Running;
        if (app == null || app.Windows.Count == 0 || _expose != null) return;
        _surface.IsHovering = false;
        _surface.Hovered = null;
        EnsureAnimating();

        var ex = new ExposeWindow(app.Windows.ToList(), item.Icon, TargetMonitor, DpiScale, ToPx(_surface.ReservedHeight));
        ex.Closed += (_, _) => { if (ReferenceEquals(_expose, ex)) _expose = null; };
        _expose = ex;
        ex.Show();
    }

    /// <summary>The running app with the most windows (for the Exposé debug commands).</summary>
    private DockItem? BusiestApp() =>
        _surface.Items.Where(i => i.IsRunning).OrderByDescending(i => i.Running!.Windows.Count).FirstOrDefault();

    /// <summary>Debug: Exposé's layout for the busiest app, drawn with placeholders instead of live thumbnails.</summary>
    public void SaveExposePreview(string path)
    {
        var item = BusiestApp();
        if (item == null) return;
        var mon = TargetMonitor.Bounds;
        double scale = DpiScale;
        var surface = new ExposeSurface
        {
            Backdrop = Dwm.CaptureScreen(mon), AppIcon = item.Icon, Scale = scale, Progress = 1, Hover = 0
        };
        var sizes = new List<Size>();
        foreach (var w in item.Running!.Windows)
        {
            var b = Dwm.VisibleBounds(w);
            sizes.Add(NativeMethods.IsIconic(w) || b.Width <= 0 ? new Size(1280, 800) : new Size(b.Width, b.Height));
            surface.Tiles.Add(new ExposeTile { Hwnd = w, Title = WindowTracker.GetTitle(w) });
        }
        var targets = ExposeLayout.Arrange(sizes, new Rect(60, 60, mon.Width - 120, mon.Height - 60 - ToPx(_surface.ReservedHeight) - 40));
        for (int i = 0; i < targets.Length; i++) surface.Tiles[i].Current = surface.Tiles[i].Target = targets[i];

        var size = new Size(mon.Width / scale, mon.Height / scale);
        surface.Measure(size);
        surface.Arrange(new Rect(size));
        surface.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(surface);
        Save(rtb, path);
        AppPaths.Log($"Exposé preview: {item.Name}, {targets.Length} windows");
    }

    /// <summary>Debug: open real Exposé for the busiest app, log the thumbnail results, close after a moment.</summary>
    public async Task RunExposeLive(int milliseconds, string? capturePath = null)
    {
        var item = BusiestApp();
        if (item == null) return;
        ShowExpose(item);
        await Task.Delay(milliseconds);
        if (_expose is ExposeWindow ex)
        {
            // Grab the screen while Exposé is fully open (the desktop capture includes DWM's live thumbnails).
            if (capturePath != null && Dwm.CaptureScreen(TargetMonitor.Bounds) is BitmapSource shot) Save(shot, capturePath);
            File.WriteAllLines(Path.Combine(AppPaths.DataDir, "expose-diagnostics.txt"), ex.Diagnostics);
            ex.Dismiss(IntPtr.Zero);
            await Task.Delay(600);
        }
    }
}
