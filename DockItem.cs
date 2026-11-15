using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnowDock.Native;

namespace SnowDock;

internal enum DockItemKind { App, Stack, RecycleBin }
internal enum BounceKind { None, Launch, Attention }

internal sealed class DockItem
{
    public DockItemKind Kind { get; init; } = DockItemKind.App;
    public string Name { get; set; } = "";
    public PinnedItem? Pin { get; set; }
    public RunningApp? Running { get; set; }
    public Stacks.StackFolder? Stack { get; init; }

    private BitmapSource? _icon;
    private BitmapSource? _reflection;

    public BitmapSource Icon => _icon ?? IconLoader.Placeholder();
    public BitmapSource Reflection => _reflection ??= MakeReflection(Icon);

    public bool IsPinned => Pin != null;
    public bool IsRunning => Running is { Windows.Count: > 0 };

    // ---- per-frame layout (DIPs, window coordinates) ----
    public double Size;       // current drawn size
    public double CenterX;    // current center
    public double RestCenterX;
    /// <summary>Animated space opened before this item while another icon is dragged over it.</summary>
    public double ExtraBefore;
    public double TargetExtraBefore;

    // ---- bounce animation ----
    public BounceKind Bounce { get; private set; }
    private DateTime _bounceStart;
    private int _bounceCount;
    public DateTime LaunchDeadline { get; private set; }

    public void SetIcon(BitmapSource? icon)
    {
        _icon = icon;
        _reflection = null;
    }

    public void StartLaunchBounce()
    {
        Bounce = BounceKind.Launch;
        _bounceStart = DateTime.UtcNow;
        LaunchDeadline = DateTime.UtcNow.AddSeconds(12);
    }

    public void StartAttentionBounce()
    {
        if (Bounce == BounceKind.Launch) return;
        Bounce = BounceKind.Attention;
        _bounceStart = DateTime.UtcNow;
        _bounceCount = 3;
    }

    public void StopBounce() => Bounce = BounceKind.None;

    /// <summary>Upward offset in multiples of the rest icon size. Called every frame.</summary>
    public double BounceOffset()
    {
        if (Bounce == BounceKind.None) return 0;
        double t = (DateTime.UtcNow - _bounceStart).TotalSeconds;

        if (Bounce == BounceKind.Launch)
        {
            if (DateTime.UtcNow > LaunchDeadline && Phase(t, 0.62) < 0.05) { Bounce = BounceKind.None; return 0; }
            return 0.5 * Hop(Phase(t, 0.62));
        }

        // Attention: a few big hops, then stop until the app asks again.
        const double period = 0.7;
        if (t > period * _bounceCount) { Bounce = BounceKind.None; return 0; }
        return 0.9 * Hop(Phase(t, period));
    }

    /// <summary>Called once a launched app's first window appears: finish the current hop, then stop.</summary>
    public void LaunchCompleted()
    {
        if (Bounce == BounceKind.Launch) LaunchDeadline = DateTime.UtcNow;
    }

    public bool IsAnimating => Bounce != BounceKind.None;

    private static double Phase(double t, double period) => (t % period) / period;

    /// <summary>Ballistic hop: fast rise, hang, fall (parabola), 0..1..0.</summary>
    private static double Hop(double p) => 4 * p * (1 - p);

    public bool Matches(RunningApp app)
    {
        if (Pin == null) return false;
        if (Pin.Aumid != null && app.Aumid != null &&
            string.Equals(Pin.Aumid, app.Aumid, StringComparison.OrdinalIgnoreCase)) return true;
        if (Pin.TargetExe == null || app.ExePath == null) return false;

        string target = ShortcutResolver.NormalizePath(Pin.TargetExe);
        string running = ShortcutResolver.NormalizePath(app.ExePath);
        if (string.Equals(target, running, StringComparison.OrdinalIgnoreCase)) return true;

        // Launcher shortcuts, e.g. Discord: Update.exe --processStart Discord.exe, where the window belongs to
        // ...\Discord\app-1.0.x\Discord.exe. Match when the running exe lives under the launcher's folder and the
        // shortcut's arguments name it.
        var dir = Path.GetDirectoryName(target);
        var exeName = Path.GetFileName(running);
        return dir != null && Pin.ShortcutArguments != null
               && running.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Pin.ShortcutArguments.Contains(exeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pre-renders the flipped, fading copy that shows on the glass floor.</summary>
    private static BitmapSource MakeReflection(BitmapSource icon)
    {
        int w = icon.PixelWidth, h = icon.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var fade = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(150, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(40, 0, 0, 0), 0.35),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.6)
                }
            };
            dc.PushOpacityMask(fade);
            dc.PushTransform(new ScaleTransform(1, -1, 0, h / 2.0));
            dc.DrawImage(icon, new Rect(0, 0, w, h));
            dc.Pop();
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
