using System.Windows;

namespace SnowDock.Expose;

internal static class ExposeLayout
{
    /// <summary>
    /// Arrange windows of the given sizes in a grid inside <paramref name="area"/>, choosing the column
    /// count that shows the most window area. Windows keep their aspect ratio and are never enlarged.
    /// Order is kept: the first window goes top-left. Everything is in physical pixels.
    /// </summary>
    public static Rect[] Arrange(IReadOnlyList<Size> sizes, Rect area, double gap = 48)
    {
        int n = sizes.Count;
        var result = new Rect[n];
        if (n == 0 || area.Width <= 0 || area.Height <= 0) return result;

        int bestCols = 1;
        double bestScore = -1;
        for (int cols = 1; cols <= n; cols++)
        {
            int rows = (int)Math.Ceiling(n / (double)cols);
            double cellW = (area.Width - gap * (cols - 1)) / cols, cellH = (area.Height - gap * (rows - 1)) / rows;
            if (cellW <= 0 || cellH <= 0) continue;
            double score = sizes.Sum(s =>
            {
                double k = Scale(s, cellW, cellH);
                return s.Width * k * s.Height * k;
            });
            if (score > bestScore) { bestScore = score; bestCols = cols; }
        }

        int c = bestCols, r = (int)Math.Ceiling(n / (double)c);
        double cw = (area.Width - gap * (c - 1)) / c, ch = (area.Height - gap * (r - 1)) / r;
        double top = area.Y + (area.Height - (r * ch + (r - 1) * gap)) / 2;
        for (int i = 0; i < n; i++)
        {
            int row = i / c, col = i % c;
            int inRow = Math.Min(c, n - row * c); // the last row may be short; center it
            double rowLeft = area.X + (area.Width - (inRow * cw + (inRow - 1) * gap)) / 2;
            double k = Scale(sizes[i], cw, ch) * 0.94;
            double w = sizes[i].Width * k, h = sizes[i].Height * k;
            double cx = rowLeft + col * (cw + gap) + cw / 2, cy = top + row * (ch + gap) + ch / 2;
            result[i] = new Rect(cx - w / 2, cy - h / 2, w, h);
        }
        return result;
    }

    private static double Scale(Size s, double w, double h) =>
        s.Width <= 0 || s.Height <= 0 ? 1 : Math.Min(1, Math.Min(w / s.Width, h / s.Height));
}
