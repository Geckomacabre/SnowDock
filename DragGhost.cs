using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static SnowDock.Native.NativeMethods;

namespace SnowDock;

/// <summary>
/// The icon that follows the cursor while dragging a dock item. It is its own small topmost window,
/// so it can leave the dock's strip, and it is click-through so it never gets in the way of hit-testing.
/// It also plays the fly-into-place and "poof" animations.
/// </summary>
internal sealed class DragGhost : Window
{
    private readonly GhostSurface _surface = new();
    private readonly double _boxDip;
    private IntPtr _hwnd;
    private double _scale = 1;
    private Point _centerPx;

    private enum Anim { None, Fly, Poof }
    private Anim _anim;
    private DateTime _animStart;
    private double _animDuration;
    private Point _flyFrom, _flyTo;
    private double _sizeFrom, _sizeTo;
    private Action? _animDone;

    public DragGhost(double boxDip)
    {
        _boxDip = boxDip;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; Width = boxDip; Height = boxDip;
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.HighQuality);
        Content = _surface;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public IntPtr Handle => _hwnd;

    /// <summary>Start showing <paramref name="icon"/> centered on a point in physical screen pixels.</summary>
    public void Begin(BitmapSource icon, double iconSizeDip, double dpiScale, Point centerPx)
    {
        StopAnimation();
        _scale = dpiScale;
        _surface.Icon = icon;
        _surface.IconSize = iconSizeDip;
        _surface.Mode = GhostMode.Icon;
        _surface.Dim = false;
        if (!IsVisible) Show();
        MoveTo(centerPx);
        _surface.InvalidateVisual();
    }

    public void MoveTo(Point centerPx)
    {
        _centerPx = centerPx;
        if (_hwnd == IntPtr.Zero) return;
        int box = (int)Math.Ceiling(_boxDip * _scale);
        SetWindowPos(_hwnd, HWND_TOPMOST, (int)Math.Round(centerPx.X - box / 2.0), (int)Math.Round(centerPx.Y - box / 2.0),
            box, box, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Faded while the icon would be removed if dropped here.</summary>
    public void SetRemoving(bool removing)
    {
        if (_surface.Dim == removing) return;
        _surface.Dim = removing;
        _surface.InvalidateVisual();
    }

    /// <summary>Glide into a dock slot (physical pixels), resizing to the slot's icon size, then hide.</summary>
    public void FlyTo(Point targetPx, double targetSizeDip, Action done)
    {
        _flyFrom = _centerPx;
        _flyTo = targetPx;
        _sizeFrom = _surface.IconSize;
        _sizeTo = targetSizeDip;
        _surface.Dim = false;
        StartAnimation(Anim.Fly, 0.18, done);
    }

    /// <summary>The puff of smoke for an icon dragged off the dock.</summary>
    public void Poof(Action? done = null)
    {
        _surface.Mode = GhostMode.Poof;
        _surface.PoofT = 0;
        StartAnimation(Anim.Poof, 0.42, done);
    }

    private void StartAnimation(Anim anim, double duration, Action? done)
    {
        StopAnimation();
        _anim = anim;
        _animDuration = duration;
        _animDone = done;
        _animStart = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>Stops any animation. A cut-short animation still runs its completion (e.g. un-hiding the dock icon).</summary>
    private void StopAnimation()
    {
        if (_anim == Anim.None) return;
        _anim = Anim.None;
        CompositionTarget.Rendering -= OnFrame;
        var done = _animDone;
        _animDone = null;
        done?.Invoke();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double t = Math.Min(1, (DateTime.UtcNow - _animStart).TotalSeconds / _animDuration);
        if (_anim == Anim.Fly)
        {
            double ease = 1 - Math.Pow(1 - t, 3);
            MoveTo(new Point(_flyFrom.X + (_flyTo.X - _flyFrom.X) * ease, _flyFrom.Y + (_flyTo.Y - _flyFrom.Y) * ease));
            _surface.IconSize = _sizeFrom + (_sizeTo - _sizeFrom) * ease;
        }
        else if (_anim == Anim.Poof)
        {
            _surface.PoofT = t;
        }
        _surface.InvalidateVisual();

        if (t < 1) return;
        var done = _animDone;
        _animDone = null;
        StopAnimation();
        Hide();
        done?.Invoke();
    }
}

internal enum GhostMode { Icon, Poof }

internal static class PoofPreview
{
    /// <summary>Debug: six poof frames side by side on a dark backdrop.</summary>
    public static void Save(string path, double iconSize)
    {
        const int frames = 6;
        int cell = (int)(iconSize * 2.4);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x6A)), null, new Rect(0, 0, cell * frames, cell));
            for (int f = 0; f < frames; f++)
            {
                var g = new GhostSurface { Mode = GhostMode.Poof, IconSize = iconSize, PoofT = f / (double)frames };
                g.Measure(new Size(cell, cell));
                g.Arrange(new Rect(0, 0, cell, cell));
                dc.PushTransform(new TranslateTransform(f * cell, 0));
                dc.DrawRectangle(new VisualBrush(g), null, new Rect(0, 0, cell, cell));
                dc.Pop();
            }
        }
        var rtb = new RenderTargetBitmap(cell * frames, cell, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }
}

internal sealed class GhostSurface : FrameworkElement
{
    public BitmapSource? Icon { get; set; }
    public double IconSize { get; set; } = 52;
    public bool Dim { get; set; }
    public GhostMode Mode { get; set; }
    public double PoofT { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        if (Mode == GhostMode.Icon)
        {
            if (Icon == null) return;
            if (Dim) dc.PushOpacity(0.55);
            dc.DrawImage(Icon, new Rect(c.X - IconSize / 2, c.Y - IconSize / 2, IconSize, IconSize));
            if (Dim) dc.Pop();
            return;
        }
        DrawPoof(dc, c);
    }

    /// <summary>A procedural smoke puff: soft white blobs drifting outward and fading.</summary>
    private void DrawPoof(DrawingContext dc, Point c)
    {
        double t = PoofT, s = IconSize;
        double ease = 1 - (1 - t) * (1 - t);
        double alpha = Math.Pow(1 - t, 1.4);
        double ring = s * (0.10 + 0.40 * ease);
        double r = s * (0.17 + 0.15 * ease);

        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(245 * alpha), 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb((byte)(215 * alpha), 228, 229, 233), 0.6),
                new GradientStop(Color.FromArgb((byte)(120 * alpha), 170, 172, 180), 0.85),
                new GradientStop(Color.FromArgb(0, 170, 172, 180), 1.0)
            }
        };
        brush.Freeze();

        const int puffs = 8;
        for (int i = 0; i < puffs; i++)
        {
            double a = i * Math.PI * 2 / puffs + t * 0.7;
            double wobble = 1 + 0.18 * Math.Sin(i * 2.3);
            dc.DrawEllipse(brush, null, new Point(c.X + Math.Cos(a) * ring * wobble, c.Y + Math.Sin(a) * ring * wobble * 0.85),
                r * wobble, r * wobble);
        }
        dc.DrawEllipse(brush, null, c, r * (1.3 - 0.5 * t), r * (1.3 - 0.5 * t));
    }
}
