using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

/// <summary>DWM live thumbnails (what the taskbar's window previews use) and a plain screen grab.</summary>
internal static class Dwm
{
    [StructLayout(LayoutKind.Sequential)]
    public struct THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    public const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8,
                      DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);

    public const uint DWM_TNP_RECTSOURCE = 0x2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    /// <summary>
    /// The part of a window's thumbnail that's actually visible: Windows 10/11 windows have invisible resize
    /// borders that would otherwise show as an empty frame. Null when unknown (e.g. minimized).
    /// </summary>
    public static RECT? VisibleSourceRect(IntPtr hwnd)
    {
        if (IsIconic(hwnd) || !GetWindowRect(hwnd, out var wr)) return null;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var fb, Marshal.SizeOf<RECT>()) != 0) return null;
        var r = new RECT { Left = fb.Left - wr.Left, Top = fb.Top - wr.Top, Right = fb.Right - wr.Left, Bottom = fb.Bottom - wr.Top };
        return r.Width > 0 && r.Height > 0 ? r : null;
    }

    /// <summary>The window's visible bounds on screen (no invisible borders), or its plain rect.</summary>
    public static RECT VisibleBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var fb, Marshal.SizeOf<RECT>()) == 0 && fb.Width > 0)
            return fb;
        GetWindowRect(hwnd, out var wr);
        return wr;
    }

    /// <summary>Place a thumbnail (destination in physical pixels, relative to the host window's client area).</summary>
    public static void Show(IntPtr thumb, RECT dest, RECT? source = null, byte opacity = 255)
    {
        var p = new THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = dest,
            opacity = opacity,
            fVisible = true,
            fSourceClientAreaOnly = false
        };
        if (source is RECT s)
        {
            p.dwFlags |= DWM_TNP_RECTSOURCE;
            p.rcSource = s;
        }
        DwmUpdateThumbnailProperties(thumb, ref p);
    }

    // ---- screen capture (GDI) ----
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    private const uint SRCCOPY = 0x00CC0020;

    /// <summary>A copy of what's on screen inside <paramref name="area"/> (physical pixels).</summary>
    public static BitmapSource? CaptureScreen(RECT area)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = IntPtr.Zero, bmp = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, area.Width, area.Height);
            var old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, SRCCOPY);
            SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Screen capture failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
