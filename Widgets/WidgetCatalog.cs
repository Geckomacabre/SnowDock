namespace SnowDock.Widgets;

internal sealed record WidgetInfo(string Type, string Name, string Category, string Description, Func<DockWidget> Create);

/// <summary>Every widget the picker offers, grouped by category.</summary>
internal static class WidgetCatalog
{
    public const string Clocks = "Clocks", Productivity = "Productivity", System = "System", Apps = "Apps & Files";

    public static IReadOnlyList<string> Categories { get; } = new[] { Clocks, Productivity, System, Apps };

    public static IReadOnlyList<WidgetInfo> All { get; } = Build();

    private static List<WidgetInfo> Build()
    {
        var list = new List<WidgetInfo>();
        void Add(string type, string name, string category, string description, Func<DockWidget> create) =>
            list.Add(new WidgetInfo(type, name, category, description, create));

        ClockWidgets.Register(Add);
        ProgressWidgets.Register(Add);
        SystemWidgets.Register(Add);
        ActionWidgets.Register(Add);
        ProductivityWidgets.Register(Add);
        LauncherWidgets.Register(Add);
        return list;
    }

    public static WidgetInfo? Find(string type) => All.FirstOrDefault(w => w.Type == type);

    /// <summary>A live widget for a saved entry, or null if its type no longer exists.</summary>
    public static DockWidget? Create(WidgetConfig config)
    {
        var info = Find(config.Type);
        if (info == null) return null;
        var widget = info.Create();
        widget.Config = config;
        return widget;
    }
}
