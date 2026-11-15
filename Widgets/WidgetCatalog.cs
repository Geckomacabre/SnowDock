using System.Reflection;

namespace SnowDock.Widgets;

internal sealed record WidgetInfo(string Type, string Name, string Category, string Description, Func<DockWidget> Create);

/// <summary>Marks a static class whose <c>Register(add)</c> method adds a family of widgets to the catalog.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WidgetFamilyAttribute : Attribute
{
    /// <summary>Lower comes first in the picker.</summary>
    public int Order { get; }
    public WidgetFamilyAttribute(int order = 100) => Order = order;
}

/// <summary>Every widget the picker offers, grouped by category.</summary>
internal static class WidgetCatalog
{
    public const string Clocks = "Clocks", Productivity = "Productivity", System = "System", Apps = "Apps & Files",
        Media = "Media & Audio", Weather = "Weather", Finance = "Finance", Analytics = "Analytics",
        Developer = "Developer", Online = "Online & Mail", Social = "Social", Stocks = "Stocks";

    /// <summary>Categories in picker order; empty ones are hidden.</summary>
    public static IReadOnlyList<string> Categories =>
        new[] { Clocks, Productivity, System, Media, Apps, Weather, Stocks, Finance, Analytics, Developer, Online, Social }
            .Where(c => All.Any(w => w.Category == c)).ToList();

    public static IReadOnlyList<WidgetInfo> All { get; } = Build();

    /// <summary>
    /// Finds every [WidgetFamily] class and calls its Register method, so adding a family never means editing
    /// this file.
    /// </summary>
    private static List<WidgetInfo> Build()
    {
        var list = new List<WidgetInfo>();
        void Add(string type, string name, string category, string description, Func<DockWidget> create)
        {
            if (list.Any(w => w.Type == type)) { AppPaths.Log($"Duplicate widget type '{type}' ignored"); return; }
            list.Add(new WidgetInfo(type, name, category, description, create));
        }
        Action<string, string, string, string, Func<DockWidget>> add = Add;

        var families = typeof(WidgetCatalog).Assembly.GetTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<WidgetFamilyAttribute>()))
            .Where(x => x.Attr != null)
            .OrderBy(x => x.Attr!.Order).ThenBy(x => x.Type.Name);
        foreach (var (type, _) in families)
        {
            var register = type.GetMethod("Register", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            try { register?.Invoke(null, new object[] { add }); }
            catch (Exception ex) { AppPaths.Log($"Widget family {type.Name} failed to register: {ex.InnerException?.Message ?? ex.Message}"); }
        }
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
