using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using SnowDock.Widgets.Media;

namespace SnowDock.Widgets.SystemPlus;

/// <summary>
/// Do Not Disturb. Windows offers apps no way to switch it, so a click opens the notification center, where its
/// switch lives. The tile shows whether it's on by reading the shell's quiet-hours state (read only).
/// </summary>
internal sealed class DoNotDisturbWidget : DockWidget
{
    // The shell publishes its quiet-hours profile here: 0 off, 1 priority only, 2 alarms only.
    private const ulong QuietHoursProfile = 0x0D83063EA3BF1C75;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope,
        out uint changeStamp, out uint buffer, ref uint bufferSize);

    private int? _state = -1;

    /// <summary>The quiet-hours profile, or null if Windows didn't say.</summary>
    private static int? Read()
    {
        try
        {
            ulong name = QuietHoursProfile;
            uint size = 4;
            return NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out uint value, ref size) == 0 && size == 4
                ? (int)value : null;
        }
        catch { return null; }
    }

    private bool On => _state > 0;

    public override string Title => _state switch
    {
        null => "Do Not Disturb (click for the notification center)",
        0 => "Do Not Disturb is off (switch it in the notification center)",
        2 => "Do Not Disturb: alarms only (switch it in the notification center)",
        _ => "Do Not Disturb is on (switch it in the notification center)"
    };

    public override bool Tick(DateTime now)
    {
        var s = Read();
        if (s == _state) return false;
        _state = s;
        return true;
    }

    // Win+N: the notification center, whose bell button turns Do Not Disturb on and off.
    public override void OnClick() => Keys.Press(Keys.Win, 0x4E);

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height * 0.44);
        double h = r.Height;
        if (On)
        {
            dc.DrawEllipse(p.Accent, null, c, h * 0.27, h * 0.27);
            Glyphs.Moon(dc, c, h * 0.17, Brushes.White, null);
        }
        else Glyphs.Moon(dc, c, h * 0.2, null, Glyphs.Stroke(p.Ink, Math.Max(1.4, h * 0.035)));
        Centered(dc, _state == null ? "DND" : On ? "ON" : "OFF", new Point(r.X + r.Width / 2, r.Bottom - h * 0.13), h * 0.11,
            On ? p.Accent : p.SubInk, p, r.Width - 6, FontWeights.Bold);
    }
}

