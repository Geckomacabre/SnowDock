using System.Globalization;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Finance;

/// <summary>One store in the total: its data, or why it has none.</summary>
internal sealed record StoreLine(FinanceService Service, RevenueData? Data, string? Status);

/// <summary>
/// Every connected store added up. Amounts are never added across currencies: the total is in the currency of the
/// first connected store (in <see cref="FinanceHub.Services"/> order) and stores in other currencies are listed apart.
/// </summary>
internal sealed class TotalData
{
    public required IReadOnlyList<StoreLine> Stores { get; init; }
    public required string Currency { get; init; }
    public required DateTime Today { get; init; }

    public IEnumerable<StoreLine> Included => Stores.Where(s => s.Data?.Currency == Currency);
    public IEnumerable<StoreLine> OtherCurrency => Stores.Where(s => s.Data != null && s.Data.Currency != Currency);
    public bool Mixed => OtherCurrency.Any();

    public double Last30 => Included.Sum(s => s.Data!.Last30);
    public double Prev30 => Included.Sum(s => s.Data!.Prev30);
    public bool HasPrev30 => Included.All(s => s.Data!.HasPrev30);
    public double MonthToDate => Included.Sum(s => s.Data!.MonthToDate);

    /// <summary>Month to date plus the last 30 days' daily average for each day left in the month.</summary>
    public double ProjectedMonth => MonthToDate + Last30 / 30 * (DateTime.DaysInMonth(Today.Year, Today.Month) - Today.Day);

    public static TotalData From(IReadOnlyList<StoreLine> stores) => new()
    {
        Stores = stores,
        Currency = stores.FirstOrDefault(s => s.Data != null)?.Data!.Currency ?? "USD",
        Today = DateTime.Today
    };

    public static TotalData Sample { get; } = From(new[] { "stripe", "shopify", "gumroad" }
        .Select(id => FinanceHub.Services.First(s => s.Id == id))
        .Select(s => new StoreLine(s, s.Sample, null)).ToList());
}

/// <summary>Gathers the stores for the total, fetching through the hub (so nothing is called twice).</summary>
internal static class TotalGatherer
{
    /// <summary>Services with a saved key and every setting they need (a shop domain comes from that store's own widget).</summary>
    public static bool AnyReady() => FinanceHub.Services.Any(FinanceHub.Ready);

    /// <summary>Fetch (or reuse) every connected store. Runs on the thread pool.</summary>
    public static async Task<TotalData> FetchAsync(CancellationToken ct)
    {
        var pending = new List<(FinanceService Svc, Task<RevenueData>? Task, string? Status)>();
        foreach (var svc in FinanceHub.Services)
        {
            if (!KeyStore.Has(svc.Id)) continue;
            var fields = FinanceHub.FieldsFor(svc);
            pending.Add(svc.HasRequired(fields)
                ? (svc, FinanceHub.Get(svc, KeyStore.Get(svc.Id) ?? "", fields, ct), null)
                : (svc, null, $"add the {svc.Name} widget to set up the store"));
        }
        var lines = new List<StoreLine>();
        Exception? first = null;
        foreach (var (svc, task, status) in pending)
        {
            if (task == null) { lines.Add(new StoreLine(svc, null, status)); continue; }
            try { lines.Add(new StoreLine(svc, await task.ConfigureAwait(false), null)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                first ??= ex;
                lines.Add(new StoreLine(svc, null, Web.Friendly(ex)));
            }
        }
        if (!lines.Any(l => l.Data != null)) throw first ?? new WebError("No store connected");
        return TotalData.From(lines);
    }

    /// <summary>
    /// The total from what the hub already has, without fetching: used when a store's own widget brings new data
    /// between the total's fetches. Stores without data keep the status from <paramref name="previous"/>.
    /// </summary>
    public static TotalData? FromHub(TotalData? previous)
    {
        var lines = new List<StoreLine>();
        foreach (var svc in FinanceHub.Services)
        {
            if (!KeyStore.Has(svc.Id)) continue;
            var fields = FinanceHub.FieldsFor(svc);
            var data = svc.HasRequired(fields) ? FinanceHub.LatestFor(svc, KeyStore.Get(svc.Id) ?? "", fields) : null;
            var old = previous?.Stores.FirstOrDefault(s => s.Service == svc);
            lines.Add(data != null ? new StoreLine(svc, data, null) : new StoreLine(svc, null, old?.Status ?? "updating…"));
        }
        return lines.Any(l => l.Data != null) ? TotalData.From(lines) : null;
    }

    /// <summary>"15000", "$15,000", "15k" → 15000.</summary>
    public static double? ParseGoal(string text)
    {
        var s = new string(text.Where(c => char.IsDigit(c) || c is '.' or ',' or 'k' or 'K').ToArray());
        double mult = s.EndsWith('k') || s.EndsWith('K') ? 1000 : 1;
        s = s.TrimEnd('k', 'K');
        if (s.Contains(',') && !s.Contains('.') && s.LastIndexOf(',') == s.Length - 3) s = s.Replace(',', '.'); // "1500,50"
        s = s.Replace(",", "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v * mult : null;
    }
}
