using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using static SnowDock.Native.NativeMethods;

namespace SnowDock.Widgets;

internal static class ActionWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.System;
        add("start", "Start", c, "Opens the Start menu.", () => new ActionTile("Start", Glyph.Apps, () => Keys.Press(Keys.Win)));
        add("show-desktop", "Show Desktop", c, "Hides every window; click again to bring them back.",
            () => new ActionTile("Show Desktop", Glyph.Desktop, () => Keys.Press(Keys.Win, 0x44)));
        add("notifications", "Notifications", c, "Opens the notification center and calendar.",
            () => new ActionTile("Notifications", Glyph.Bell, () => Keys.Press(Keys.Win, 0x4E)));
        add("quick-settings", "Quick Settings", c, "Opens Wi-Fi, Bluetooth, volume and the other quick settings.",
            () => new ActionTile("Quick Settings", Glyph.Sliders, () => Keys.Press(Keys.Win, 0x41)));
        add("keyboard", "On-Screen Keyboard", c, "Opens the on-screen keyboard.",
            () => new ActionTile("On-Screen Keyboard", Glyph.Keyboard, () => DockWindow.ShellOpen("osk.exe")));
        add("screenshot", "Screenshot", c, "Capture the screen, a window or a region.",
            () => new ActionTile("Screenshot", Glyph.Capture, () => DockWindow.ShellOpen("ms-screenclip:")));
        add("theme-toggle", "Light / Dark", c, "Switches Windows between light and dark mode.", () => new ThemeToggle());
        add("language", "Keyboard Language", c, "The current input language; click to switch to the next one.", () => new LanguageWidget());
    }
}

internal static class Keys
{
    public const byte Win = 0x5B;
    private const uint KeyUp = 2;

    /// <summary>Press a key combination (modifier first), then release in reverse.</summary>
    public static void Press(params byte[] keys)
    {
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        foreach (var k in keys.Reverse()) keybd_event(k, 0, KeyUp, UIntPtr.Zero);
    }
}

internal enum Glyph { Apps, Desktop, Bell, Sliders, Keyboard, Capture }

/// <summary>A square tile with a drawn glyph that does one thing when clicked.</summary>
internal sealed class ActionTile : DockWidget
{
    private readonly string _title;
    private readonly Glyph _glyph;
    private readonly Action _act;

    public ActionTile(string title, Glyph glyph, Action act) => (_title, _glyph, _act) = (title, glyph, act);
    public override string Title => _title;
    public override void OnClick() => _act();

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double u = r.Height * 0.09; // glyph unit
        var pen = new Pen(p.Ink, Math.Max(1.4, u * 0.38)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (_glyph)
        {
            case Glyph.Apps:
                for (int i = 0; i < 9; i++)
                    dc.DrawRoundedRectangle(p.Ink, null, new Rect(c.X + (i % 3 - 1.5) * u * 1.6 + u * 0.15, c.Y + (i / 3 - 1.5) * u * 1.6 + u * 0.15, u * 1.1, u * 1.1), u * 0.3, u * 0.3);
                break;
            case Glyph.Desktop:
                dc.DrawRoundedRectangle(null, pen, new Rect(c.X - u * 3, c.Y - u * 2.2, u * 6, u * 3.8), u * 0.5, u * 0.5);
                dc.DrawLine(pen, new Point(c.X, c.Y + u * 1.6), new Point(c.X, c.Y + u * 2.6));
                dc.DrawLine(pen, new Point(c.X - u * 1.5, c.Y + u * 2.7), new Point(c.X + u * 1.5, c.Y + u * 2.7));
                break;
            case Glyph.Bell:
                var bell = Geometry.Parse($"M {c.X - u * 2.2},{c.Y + u * 1.4} C {c.X - u * 1.6},{c.Y + u * 0.6} {c.X - u * 2},{c.Y - u * 2.6} {c.X},{c.Y - u * 2.6} " +
                                          $"C {c.X + u * 2},{c.Y - u * 2.6} {c.X + u * 1.6},{c.Y + u * 0.6} {c.X + u * 2.2},{c.Y + u * 1.4} Z");
                dc.DrawGeometry(null, pen, bell);
                dc.DrawEllipse(p.Ink, null, new Point(c.X, c.Y + u * 2.2), u * 0.55, u * 0.55);
                break;
            case Glyph.Sliders:
                for (int i = -1; i <= 1; i++)
                {
                    double y = c.Y + i * u * 1.6, knob = c.X + (i == 0 ? u : -u);
                    dc.DrawLine(pen, new Point(c.X - u * 2.6, y), new Point(c.X + u * 2.6, y));
                    dc.DrawEllipse(p.Tile, pen, new Point(knob, y), u * 0.65, u * 0.65);
                }
                break;
            case Glyph.Keyboard:
                dc.DrawRoundedRectangle(null, pen, new Rect(c.X - u * 3.2, c.Y - u * 2, u * 6.4, u * 4), u * 0.6, u * 0.6);
                for (int row = 0; row < 2; row++)
                    for (int k = 0; k < 5; k++)
                        dc.DrawEllipse(p.Ink, null, new Point(c.X - u * 2.2 + k * u * 1.1, c.Y - u * 0.9 + row * u * 1.1), u * 0.28, u * 0.28);
                dc.DrawLine(pen, new Point(c.X - u * 1.4, c.Y + u * 1.2), new Point(c.X + u * 1.4, c.Y + u * 1.2));
                break;
            case Glyph.Capture:
                double a = u * 2.8, l = u * 1.2;
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    var corner = new Point(c.X + sx * a, c.Y + sy * a * 0.8);
                    dc.DrawLine(pen, corner, new Point(corner.X - sx * l, corner.Y));
                    dc.DrawLine(pen, corner, new Point(corner.X, corner.Y - sy * l));
                }
                dc.DrawEllipse(null, pen, c, u * 0.9, u * 0.9);
                break;
        }
    }
}

internal sealed class ThemeToggle : DockWidget
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private bool? _last;
    public override string Title => IsLight ? "Switch to dark mode" : "Switch to light mode";

    private static bool IsLight
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
    }

    public override bool Tick(DateTime now)
    {
        bool light = IsLight;
        if (_last == light) return false;
        _last = light;
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

    public override void OnClick()
    {
        int next = IsLight ? 0 : 1;
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            k.SetValue("AppsUseLightTheme", next, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", next, RegistryValueKind.DWord);
        }
        // Tell running apps the colour set changed (as the Settings app does).
        SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
        Host?.Redraw();
    }

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double rad = r.Height * 0.24;
        dc.DrawEllipse(null, new Pen(p.Ink, 1.8), c, rad, rad);
        // The filled half shows which way it'll switch.
        var half = new StreamGeometry();
        using (var g = half.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - rad), true, true);
            g.ArcTo(new Point(c.X, c.Y + rad), new Size(rad, rad), 0, false, IsLight ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, true);
        }
        dc.DrawGeometry(p.Ink, null, half);
    }
}

internal sealed class LanguageWidget : DockWidget
{
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    private string _last = "";

    private static string Current()
    {
        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        int lang = (int)GetKeyboardLayout(thread).ToInt64() & 0xFFFF;
        try { return CultureInfo.GetCultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { return "??"; }
    }

    public override string Title => $"Input language: {Current()} (click to switch)";

    public override bool Tick(DateTime now)
    {
        var cur = Current();
        if (cur == _last) return false;
        _last = cur;
        return true;
    }

    public override void OnClick() => Keys.Press(Keys.Win, 0x20); // Win+Space: next input language

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        Centered(dc, Current(), new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Height * 0.3, p.Ink, p);
    }
}
