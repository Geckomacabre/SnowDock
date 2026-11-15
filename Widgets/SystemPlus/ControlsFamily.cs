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
            "Shows whether Do Not Disturb is on; Windows lets no app switch it, so a click opens the notification center where its switch is.",
            () => new DoNotDisturbWidget());
    }
}
