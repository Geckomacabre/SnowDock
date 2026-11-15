namespace SnowDock.Widgets.SystemPlus;

/// <summary>
/// WMI through the scripting COM object, late-bound (no System.Management package). Every call is slow-ish
/// and can block for a while, so callers run it on the thread pool and throttle it.
/// </summary>
internal static class Wmi
{
    private static dynamic Connect(string ns)
    {
        var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new NotSupportedException("WMI scripting is unavailable");
        dynamic locator = Activator.CreateInstance(type)!;
        return locator.ConnectServer(".", ns);
    }

    /// <summary>Run a WQL query and map each row with <paramref name="pick"/>.</summary>
    public static List<T> Query<T>(string ns, string wql, Func<dynamic, T> pick)
    {
        dynamic services = Connect(ns);
        dynamic set = services.ExecQuery(wql);
        var list = new List<T>();
        int count = set.Count;
        for (int i = 0; i < count; i++) list.Add(pick(set.ItemIndex(i)));
        return list;
    }

    /// <summary>The objects of a query, for calling their methods.</summary>
    public static List<dynamic> Objects(string ns, string wql) => Query<dynamic>(ns, wql, o => o);
}
