namespace SnowDock.Widgets.SystemPlus;

[WidgetFamily(32)]
internal static class ControlsFamily
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.System;
        add("quick-toggles", "Quick Toggles", c,
            "Wi-Fi, Bluetooth and mobile hotspot switches in one tile, plus a shortcut to airplane mode (which apps can't switch).",
            () => new QuickTogglesWidget());
        add("brightness", "Brightness", c,
            "Screen brightness with a slider for each screen, for laptop panels and monitors that accept DDC/CI.",
            () => new BrightnessWidget());
        add("do-not-disturb", "Do Not Disturb", c,
            "Opens the notification center, where Do Not Disturb's switch is (Windows lets no app flip it). The on/off it shows is a best guess.",
            () => new DoNotDisturbWidget());
    }
}
