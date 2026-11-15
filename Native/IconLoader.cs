using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Native;

/// <summary>
/// Loads large icons through IShellItemImageFactory. One code path covers .exe files, .lnk shortcuts,
/// folders, shell:AppsFolder\{AUMID} (Store apps) and shell namespace items like the Recycle Bin.
/// </summary>
internal static class IconLoader
{
    public const string RecycleBinPath = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const int Size = 256;
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? ForPath(string path, bool bypassCache = false)
    {
        if (!bypassCache && Cache.TryGetValue(path, out var cached)) return cached;
        var img = Load(path);
        Cache[path] = img;
        return img;
    }

    public static BitmapSource? ForAumid(string aumid) => ForPath(@"shell:AppsFolder\" + aumid);

    /// <summary>
    /// A file's look in a stack: a thumbnail (photos, PDFs, videos) when Windows has one, otherwise its icon.
    /// Thumbnails keep their aspect ratio, so draw them with <see cref="FitSquare"/>.
    /// </summary>
    public static BitmapSource? ForFile(string path)
    {
        string key = "thumb:" + path;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var img = Load(path, allowThumbnail: true);
        if (Cache.Count > 600) Cache.Clear(); // stacks churn through files; keep the cache bounded
        Cache[key] = img;
        return img;
    }

    /// <summary>Forget cached images for files that changed (a re-downloaded file keeps its path).</summary>
    public static void Forget(string path) => Cache.Remove("thumb:" + path);

    /// <summary>The largest rect with the image's aspect ratio that fits, centered, in a square.</summary>
    public static Rect FitSquare(BitmapSource img, Rect square)
    {
        double w = img.PixelWidth, h = img.PixelHeight;
        if (w <= 0 || h <= 0 || Math.Abs(w - h) < 1) return square;
        double scale = Math.Min(square.Width / w, square.Height / h);
        double dw = w * scale, dh = h * scale;
        return new Rect(square.X + (square.Width - dw) / 2, square.Y + (square.Height - dh) / 2, dw, dh);
    }

    private static BitmapSource? Load(string parsingName, bool allowThumbnail = false)
    {
        object? obj = null;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, IID_IShellItemImageFactory, out obj);
            if (obj is not IShellItemImageFactory factory) return null;

            var big = GetImage(factory, Size, allowThumbnail);
            if (big == null) return null;

            // Icons with no large version come back drawn small inside a thin square frame.
            // Remove the frame, then crop to the icon itself.
            if (HasFrame(big)) big = ClearBorder(big, Math.Max(4, big.PixelWidth / 32));
            var result = TrimSmallIcon(big);
            result.Freeze();
            return result;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Icon load failed for {parsingName}: {ex.Message}");
            return null;
        }
        finally
        {
            if (obj != null) Marshal.ReleaseComObject(obj);
        }
    }

    private static BitmapSource? GetImage(IShellItemImageFactory factory, int size, bool allowThumbnail = false)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var sz = new SIZE { cx = size, cy = size };
            if (allowThumbnail)
            {
                // A real thumbnail (photo, video frame, PDF page) if Windows has one; otherwise the icon below.
                int th = factory.GetImage(sz, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk, out hbm);
                if (th == 0 && hbm != IntPtr.Zero) return FromHBitmap(hbm, thumbnail: true);
                if (hbm != IntPtr.Zero) { DeleteObject(hbm); hbm = IntPtr.Zero; }
            }
            int hr = factory.GetImage(sz, SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero)
                hr = factory.GetImage(sz, SIIGBF.BiggerSizeOk, out hbm);
            return hr == 0 && hbm != IntPtr.Zero ? FromHBitmap(hbm) : null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    /// <summary>
    /// True for the shell's "small icon in a box" rendering: a thin line along every edge with empty space
    /// just inside it. Real icons have either empty padding at the edges or solid art running inward.
    /// </summary>
    private static bool HasFrame(BitmapSource bmp)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        if (w < 32 || h < 32) return false;
        int inner = Math.Max(6, w / 24), innerEnd = Math.Max(inner + 4, w / 8);

        // (x, y, dx, dy): start at each edge midpoint and walk inward.
        foreach (var (x0, y0, dx, dy) in new[] { (w / 2, 0, 0, 1), (w / 2, h - 1, 0, -1), (0, h / 2, 1, 0), (w - 1, h / 2, -1, 0) })
        {
            int edge = 0, inside = 0;
            for (int s = 0; s < innerEnd; s++)
            {
                int a = Alpha(bmp, x0 + dx * s, y0 + dy * s);
                if (s < 4) edge = Math.Max(edge, a);
                else if (s >= inner) inside = Math.Max(inside, a);
            }
            if (edge < 30 || inside > 10) return false;
        }
        return true;
    }

    private static int Alpha(BitmapSource bmp, int x, int y)
    {
        var px = new byte[4];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return px[3];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint c0, c1, c2; // room for bitfield masks
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>
    /// Copies the bitmap's 32-bit pixels so per-pixel alpha survives (Imaging.CreateBitmapSourceFromHBitmap
    /// drops it). GetDIBits is asked for top-down rows, so GDI deals with however the bitmap is stored
    /// (icons and thumbnails come back stored differently).
    /// </summary>
    private static BitmapSource? FromHBitmap(IntPtr hbm, bool thumbnail = false)
    {
        if (GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0) return null;
        var bm = ds.dsBm;
        if (bm.bmBitsPixel != 32 || bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;

        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight), stride = w * 4;
        var pixels = new byte[stride * h];
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, // negative: top-down
                biPlanes = 1, biBitCount = 32, biCompression = 0 /* BI_RGB */
            }
        };
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, pixels, ref bmi, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha)
        {
            if (thumbnail)
                // Photos and video frames have no alpha at all: fully opaque.
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            else
                // Legacy 24-bit icons: make drawn pixels opaque, leave the empty (black) canvas transparent.
                for (int i = 0; i < pixels.Length; i += 4)
                    pixels[i + 3] = (byte)(pixels[i] | pixels[i + 1] | pixels[i + 2]) == 0 ? (byte)0 : (byte)255;
        }

        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        src.Freeze();
        return src;
    }

    private static BitmapSource ClearBorder(BitmapSource src, int thickness)
    {
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x < thickness || y < thickness || x >= w - thickness || y >= h - thickness)
                    Array.Clear(px, y * stride + x * 4, 4);
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Apps that only ship small icons come back as a little icon in the middle of a 256 px canvas.
    /// Crop to the content so it fills its dock tile like everything else.
    /// </summary>
    private static BitmapSource TrimSmallIcon(BitmapSource src)
    {
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * stride + x * 4 + 3] > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < 0) return src;

        int side = Math.Max(maxX - minX + 1, maxY - minY + 1);
        if (side >= w * 0.6) return src;

        // Square crop around the content, with a little breathing room, kept inside the canvas.
        side = Math.Min(Math.Min(w, h), (int)(side * 1.12) + 2); // thumbnails needn't be square
        int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        int x0 = Math.Clamp(cx - side / 2, 0, w - side), y0 = Math.Clamp(cy - side / 2, 0, h - side);
        return new CroppedBitmap(src, new Int32Rect(x0, y0, side, side));
    }

    /// <summary>Generic placeholder for apps whose icon can't be loaded.</summary>
    public static BitmapSource Placeholder()
    {
        if (Cache.TryGetValue("<placeholder>", out var p) && p != null) return p;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var brush = new LinearGradientBrush(Color.FromRgb(0xD8, 0xDC, 0xE4), Color.FromRgb(0x8A, 0x93, 0xA3), 90);
            dc.DrawRoundedRectangle(brush, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 4),
                new Rect(24, 24, 208, 208), 40, 40);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), null, new Point(128, 128), 50, 50);
        }
        var rtb = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        Cache["<placeholder>"] = rtb;
        return rtb;
    }
}
