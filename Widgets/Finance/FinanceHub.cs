using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>One payment service: how to connect it, what its "net" means and how to read its revenue.</summary>
internal abstract class FinanceService
{
    /// <summary>Key-store id and widget type id ("stripe" → "stripe" and "stripe-chart").</summary>
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string KeyLabel { get; }
    public abstract string[] Steps { get; }
    public virtual string? DashboardUrl => null;
    public virtual AccountField[] Fields => Array.Empty<AccountField>();
    /// <summary>Fields that may be left empty.</summary>
    public virtual string[] OptionalFields => Array.Empty<string>();
    /// <summary>What "net" means for this service, short enough for the picker ("after fees and refunds").</summary>
    public abstract string NetShort { get; }
    /// <summary>What "net" means, in full, for the card.</summary>
    public abstract string NetLong { get; }
    public abstract RevenueData Sample { get; }

    /// <summary>Runs on the thread pool. Never called twice at once for the same credentials (see <see cref="FinanceHub"/>).</summary>
    public abstract Task<RevenueData> FetchAsync(string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct);

    public bool HasRequired(IReadOnlyDictionary<string, string> fields) =>
        HasRequired(id => fields.TryGetValue(id, out var v) ? v.Trim() : "");

    public bool HasRequired(Func<string, string> field) =>
        Fields.All(f => OptionalFields.Contains(f.Id) || field(f.Id).Length > 0);

    protected static string Field(IReadOnlyDictionary<string, string> fields, string id) =>
        fields.TryGetValue(id, out var v) ? v.Trim() : "";

    // ---- small JSON helpers: services disagree on numbers-as-strings and missing fields ----
    protected static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    protected static long Long(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble()
        : v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)d
        : 0;

    protected static double Decimal(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
        : e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    protected static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    protected static DateTimeOffset Time(JsonElement e, string name) =>
        DateTimeOffset.Parse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    protected static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// The one place finance data is fetched. A store's tile, its chart and the total all ask here, so a service is
/// called at most once per <see cref="Fresh"/> however many of its widgets are in the dock. The latest result
/// of each service is kept for the total. In memory only.
/// </summary>
internal static class FinanceHub
{
    /// <summary>Fixed order: the "first connected store" for the total's currency is the first one here.</summary>
    public static readonly FinanceService[] Services =
    {
        new StripeService(), new PolarService(), new DodoService(), new ShopifyService(),
        new LemonSqueezyService(), new PaddleService(), new GumroadService()
    };

    /// <summary>Calls to one service are at least this far apart.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(1);
    /// <summary>Widgets ask a little less often than <see cref="Fresh"/>, so each ask finds new data or starts it.</summary>
    public static readonly TimeSpan WidgetInterval = TimeSpan.FromMinutes(31);

    private sealed record Entry(string Print, DateTime Started, Task<RevenueData> Task);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Fetches = new();
    private static readonly Dictionary<string, (string Print, RevenueData Data)> Latest = new();
    private static readonly Dictionary<string, Dictionary<string, string>> KnownFields = new();
    private static int _version;

    /// <summary>Goes up whenever any service's data arrives.</summary>
    public static int Version => Volatile.Read(ref _version);

    /// <summary>
    /// The service's revenue: the fetch already running or fetched within the last 30 minutes for these exact
    /// credentials, or a new one. <paramref name="ct"/> only stops this caller waiting; the shared fetch goes on
    /// for the other widgets.
    /// </summary>
    public static Task<RevenueData> Get(FinanceService svc, string key, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        string print = Fingerprint(svc.Id, key, fields);
        Task<RevenueData> task;
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (!Fetches.TryGetValue(svc.Id, out var e) || e.Print != print || Stale(e, now))
            {
                var copy = new Dictionary<string, string>(fields);
                e = new Entry(print, now, Task.Run(() => svc.FetchAsync(key, copy, CancellationToken.None)));
                Fetches[svc.Id] = e;
                e.Task.ContinueWith(t =>
                {
                    lock (Gate) Latest[svc.Id] = (print, t.Result);
                    Interlocked.Increment(ref _version);
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
            task = e.Task;
        }
        return task.WaitAsync(ct);
    }

    private static bool Stale(Entry e, DateTime now) =>
        e.Task.IsCompleted && now - e.Started >= (e.Task.IsCompletedSuccessfully ? Fresh : RetryAfterError);

    /// <summary>A store widget tells the hub its non-secret settings (a shop domain), so the total can use them too.</summary>
    public static void Remember(string serviceId, IReadOnlyDictionary<string, string> fields)
    {
        lock (Gate) KnownFields[serviceId] = new Dictionary<string, string>(fields);
    }

    /// <summary>A key is saved and every required setting is known (cheap enough to ask every second).</summary>
    public static bool Ready(FinanceService svc)
    {
        if (!KeyStore.Has(svc.Id)) return false;
        lock (Gate) return KnownFields.ContainsKey(svc.Id) || svc.HasRequired(_ => "");
    }

    public static IReadOnlyDictionary<string, string> FieldsFor(FinanceService svc)
    {
        lock (Gate) return KnownFields.TryGetValue(svc.Id, out var f) ? new Dictionary<string, string>(f) : new Dictionary<string, string>();
    }

    /// <summary>The latest data for a service, if it was fetched with these credentials.</summary>
    public static RevenueData? LatestFor(FinanceService svc, string key, IReadOnlyDictionary<string, string> fields)
    {
        string print = Fingerprint(svc.Id, key, fields);
        lock (Gate) return Latest.TryGetValue(svc.Id, out var l) && l.Print == print ? l.Data : null;
    }

    /// <summary>Cheap to compute every second: changes when a key is saved or removed, or a shop domain becomes known.</summary>
    public static string ConnectionSignature()
    {
        var sb = new StringBuilder();
        lock (Gate)
            foreach (var s in Services)
                sb.Append(KeyStore.Has(s.Id) ? '1' : '0').Append(KnownFields.ContainsKey(s.Id) ? '1' : '0');
        return sb.ToString();
    }

    /// <summary>Identifies credentials without keeping the key itself around as a dictionary key.</summary>
    private static string Fingerprint(string id, string key, IReadOnlyDictionary<string, string> fields)
    {
        // Empty settings count as absent, so a widget's "store = (empty)" matches the total's "no store".
        var set = fields.Where(f => f.Value.Trim().Length > 0).OrderBy(f => f.Key, StringComparer.Ordinal);
        var text = id + "\n" + key + "\n" + string.Join("\n", set.Select(f => f.Key + "=" + f.Value.Trim()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 12);
    }
}
