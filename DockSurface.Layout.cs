using System.Windows;

namespace SnowDock;

/// <summary>
/// Draws the dock and computes its per-frame layout. Everything is drawn in code (OnRender), so the
/// magnification wave, perspective shelf and bounces stay in one place instead of fighting templates.
/// </summary>
internal sealed partial class DockSurface : FrameworkElement
{
    public List<DockItem> Items { get; } = new();
    /// <summary>Index of the first item after the separator (the Recycle Bin side), or -1.</summary>
    public int SeparatorIndex { get; set; } = -1;

    public double IconSize { get; set; } = 52;
    public double MagnifiedSize { get; set; } = 96;
    public bool MagnificationEnabled { get; set; } = true;

    /// <summary>Mouse X while hovering; kept after leaving so the wave can fade out in place.</summary>
    public double LastMouseX { get; set; }
    public bool IsHovering { get; set; }
    public DockItem? Hovered { get; set; }
    /// <summary>No hover label for this item (its stack is fanned out above it).</summary>
    public DockItem? LabelSuppressedFor { get; set; }
    public DockItem? Pressed { get; set; }
    /// <summary>Item that would receive the files being dragged over the dock (drawn darkened, like a press).</summary>
    public DockItem? DropTarget { get; set; }

    private double _magAmount; // 0..1, eased

    /// <summary>Space after the last item (drop gap at the very end when there's no Recycle Bin).</summary>
    public double TrailingExtra;
    public double TargetTrailingExtra;
    /// <summary>Item whose slot is laid out but not drawn (the drag ghost is flying into it).</summary>
    public DockItem? HiddenItem { get; set; }
    /// <summary>Keeps the frame loop alive while a drag is in progress.</summary>
    public bool KeepAnimating { get; set; }

    /// <summary>Auto-hide: 0 = fully shown, 1 = slid off the screen edge. Eases toward <see cref="HideTarget"/>.</summary>
    public double HideAmount { get; set; }
    public double HideTarget { get; set; }

    public double SlotWidth => IconSize + Gap;

    /// <summary>Close every drop gap immediately (after a drop, the item itself fills the gap).</summary>
    public void ResetGaps()
    {
        foreach (var i in Items) i.ExtraBefore = i.TargetExtraBefore = 0;
        TrailingExtra = TargetTrailingExtra = 0;
    }

    // ---- geometry (DIPs) ----
    public double FrontLip => 4;
    public double FloorDepth => Math.Round(IconSize * 0.48);
    public double Gap => Math.Round(IconSize * 0.12);
    public double SeparatorWidth => Math.Round(IconSize * 0.5);
    public double ShelfPadding => Math.Round(IconSize * 0.42);
    public double YFront => CrossLength - FrontLip;
    public double YBack => YFront - FloorDepth;
    public double Baseline => YFront - FloorDepth * 0.42;

    /// <summary>Height of the window the dock needs (magnified icons, bounces, labels).</summary>
    public double RequiredHeight =>
        FrontLip + FloorDepth * 0.42 + Math.Max(MagnifiedSize, IconSize * 1.95) +
        // Side docks put labels beside the icons, so they need room for a whole name across.
        (IsSide ? 280 : 40);

    /// <summary>Strip to reserve at the bottom of the screen so maximized windows stop above the dock.</summary>
    public double ReservedHeight => FrontLip + FloorDepth * 0.42 + IconSize + 6;

    // Shelf extents for the current frame.
    public double ShelfLeft { get; private set; }
    public double ShelfRight { get; private set; }
    public double SeparatorX { get; private set; } = double.NaN;

    /// <summary>Advance animations. Returns true while anything is still moving.</summary>
    public bool Tick(double dt)
    {
        double target = IsHovering && MagnificationEnabled ? 1 : 0;
        double speed = dt / 0.16;
        _magAmount = target > _magAmount ? Math.Min(target, _magAmount + speed) : Math.Max(target, _magAmount - speed);

        // Drop gaps ease open and closed.
        double k = Math.Min(1, dt * 16);
        bool gapsMoving = false;
        foreach (var i in Items)
        {
            i.ExtraBefore += (i.TargetExtraBefore - i.ExtraBefore) * k;
            if (Math.Abs(i.TargetExtraBefore - i.ExtraBefore) < 0.3) i.ExtraBefore = i.TargetExtraBefore;
            else gapsMoving = true;
        }
        TrailingExtra += (TargetTrailingExtra - TrailingExtra) * k;
        if (Math.Abs(TargetTrailingExtra - TrailingExtra) < 0.3) TrailingExtra = TargetTrailingExtra;
        else gapsMoving = true;

        // Auto-hide slide: about a quarter of a second each way.
        double hideStep = dt / 0.25;
        HideAmount = HideTarget > HideAmount ? Math.Min(HideTarget, HideAmount + hideStep) : Math.Max(HideTarget, HideAmount - hideStep);
        bool sliding = Math.Abs(HideAmount - HideTarget) > 0.001;

        return KeepAnimating || gapsMoving || sliding || Math.Abs(_magAmount - target) > 0.001 || Items.Any(i => i.IsAnimating);
    }

    public void LayoutItems()
    {
        int n = Items.Count;
        if (n == 0) { ShelfLeft = ShelfRight = MainLength / 2; return; }

        double s = IconSize, gap = Gap;
        double sepExtra = SeparatorIndex > 0 ? SeparatorWidth : 0;

        // 1. Rest layout, centered.
        double restWidth = n * s + (n - 1) * gap + sepExtra;
        double x = (MainLength - restWidth) / 2;
        var restLeft = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i == SeparatorIndex) x += sepExtra;
            restLeft[i] = x;
            Items[i].RestCenterX = x + s / 2;
            x += s + gap;
        }

        // 2. Magnified sizes: a cosine wave around the mouse, about 2.6 icons wide on each side.
        double range = s * 2.6;
        for (int i = 0; i < n; i++)
        {
            double d = Math.Abs(LastMouseX - Items[i].RestCenterX) / range;
            double wave = d >= 1 ? 0 : (Math.Cos(Math.PI * d) + 1) / 2;
            Items[i].Size = s + (MagnifiedSize - s) * _magAmount * wave;
        }

        // 3. Lay out with new sizes and any open drop gaps.
        var newLeft = new double[n];
        x = 0;
        for (int i = 0; i < n; i++)
        {
            x += Items[i].ExtraBefore;
            if (i == SeparatorIndex) x += sepExtra;
            newLeft[i] = x;
            x += Items[i].Size + gap;
        }
        double newWidth = x - gap + TrailingExtra;

        // 4. Shift so the spot under the mouse stays under the mouse (or stay centered at rest).
        double shift;
        if (_magAmount <= 0.0001)
        {
            shift = (MainLength - newWidth) / 2;
        }
        else
        {
            int k = 0;
            for (int i = 0; i < n; i++) if (LastMouseX >= restLeft[i] - gap / 2) k = i;
            double slotRest = s + gap;
            double f = Math.Clamp((LastMouseX - (restLeft[k] - gap / 2)) / slotRest, 0, 1);
            double newPos = newLeft[k] - gap / 2 + f * (Items[k].Size + gap);
            shift = LastMouseX - newPos;
        }

        for (int i = 0; i < n; i++) Items[i].CenterX = newLeft[i] + shift + Items[i].Size / 2;

        ShelfLeft = Items[0].CenterX - Items[0].Size / 2 - Items[0].ExtraBefore - ShelfPadding;
        ShelfRight = Items[n - 1].CenterX + Items[n - 1].Size / 2 + TrailingExtra + ShelfPadding;
        SeparatorX = SeparatorIndex > 0
            ? (Items[SeparatorIndex - 1].CenterX + Items[SeparatorIndex - 1].Size / 2 +
               Items[SeparatorIndex].CenterX - Items[SeparatorIndex].Size / 2) / 2
            : double.NaN;
    }

    /// <summary>Item under a point (window DIPs), using the current magnified layout.</summary>
    public DockItem? HitItem(Point p)
    {
        foreach (var item in Items)
        {
            double half = (item.Size + Gap) / 2;
            double top = Baseline - item.Size - item.BounceOffset() * IconSize;
            if (p.X >= item.CenterX - half && p.X < item.CenterX + half && p.Y >= top - 4 && p.Y <= CrossLength)
                return item;
        }
        return null;
    }

    /// <summary>True when the point is on the shelf area (used to keep hover while the mouse is between icons).</summary>
    public bool IsOverDock(Point p)
    {
        double maxSize = Items.Count == 0 ? IconSize : Items.Max(i => i.Size);
        return p.X >= ShelfLeft && p.X <= ShelfRight && p.Y >= Baseline - maxSize - 4;
    }
}
