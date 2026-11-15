namespace SnowDock.Widgets.Finance;

/// <summary>
/// One store's net revenue for each of the last <see cref="Days"/> local days (oldest first, today last), in one
/// currency. Every figure the finance tiles show (30 days, the 14-day line, this week, month to date) comes from it.
/// </summary>
internal sealed class RevenueData
{
    public const int Days = 60;

    public required string Currency { get; init; }
    /// <summary>Net amount per day in major units; index 0 is 59 days ago, the last index is <see cref="Today"/>.</summary>
    public required double[] Daily { get; init; }
    public DateTime Today { get; init; } = DateTime.Today;
    /// <summary>How many recent days are complete. Below 60 when paging stopped early (a very busy store).</summary>
    public int CoveredDays { get; init; } = Days;
    /// <summary>Currencies that were left out because they differ from <see cref="Currency"/>.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = Array.Empty<string>();

    /// <summary>Sum of <paramref name="count"/> days ending <paramref name="daysAgo"/> days before today.</summary>
    public double Sum(int daysAgo, int count)
    {
        double s = 0;
        for (int d = daysAgo; d < daysAgo + count && d < Days; d++) s += Daily[Days - 1 - d];
        return s;
    }

    public double Last30 => Sum(0, 30);
    public double Prev30 => Sum(30, 30);
    public double Week => Sum(0, 7);
    public double PrevWeek => Sum(7, 7);
    public bool HasPrev30 => CoveredDays >= 60;
    public bool HasPrevWeek => CoveredDays >= 14;

    /// <summary>The last <paramref name="days"/> days, oldest first.</summary>
    public double[] Recent(int days) => Daily[(Days - days)..];

    /// <summary>Revenue from the 1st of this month through today.</summary>
    public double MonthToDate => Sum(0, Today.Day);

    /// <summary>Made-up but plausible days: a gentle upward trend, quieter weekends and some noise.</summary>
    public static RevenueData Sample(int seed, double perDay, string currency)
    {
        var rng = new Random(seed);
        var today = DateTime.Today;
        var daily = new double[Days];
        for (int i = 0; i < Days; i++)
        {
            var day = today.AddDays(i - (Days - 1));
            double weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.7 : 1.05;
            double trend = 0.82 + 0.3 * i / Days;
            daily[i] = Math.Round(perDay * weekend * trend * (0.65 + rng.NextDouble() * 0.7), 2);
        }
        return new RevenueData { Currency = currency, Daily = daily, Today = today };
    }
}

/// <summary>Collects dated amounts (in any currency) into a <see cref="RevenueData"/>.</summary>
internal sealed class RevenueBuilder
{
    private readonly Dictionary<string, double[]> _byCurrency = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _today = DateTime.Today;
    private int _covered = RevenueData.Days;

    /// <summary>Local midnight at the start of the window (59 days ago).</summary>
    public DateTime Start => _today.AddDays(-(RevenueData.Days - 1));
    public DateTimeOffset StartUtc => new DateTimeOffset(Start).ToUniversalTime();

    /// <summary>Days ago for a moment, or -1 if it falls outside the window.</summary>
    public int DaysAgo(DateTimeOffset when)
    {
        int ago = (_today - when.LocalDateTime.Date).Days;
        return ago is >= 0 and < RevenueData.Days ? ago : -1;
    }

    public void Add(DateTimeOffset when, string currency, double major) => AddDay(DaysAgo(when), currency, major);

    /// <summary>Add a smallest-unit amount (cents, pence, yen…).</summary>
    public void AddMinor(DateTimeOffset when, string currency, long minor) => Add(when, currency, minor / FinanceMoney.Divisor(currency));

    public void AddDay(int daysAgo, string currency, double major)
    {
        if (daysAgo < 0 || daysAgo >= RevenueData.Days || string.IsNullOrWhiteSpace(currency)) return;
        currency = currency.ToUpperInvariant();
        if (!_byCurrency.TryGetValue(currency, out var days)) _byCurrency[currency] = days = new double[RevenueData.Days];
        days[RevenueData.Days - 1 - daysAgo] += major;
    }

    /// <summary>Paging stopped before the start of the window; only days newer than <paramref name="oldest"/> are whole.</summary>
    public void Truncated(DateTimeOffset oldest)
    {
        int ago = (_today - oldest.LocalDateTime.Date).Days;
        _covered = Math.Clamp(ago, 0, _covered);
    }

    /// <summary>The store's currency is the one with the most money; any others are left out (no exchange rates here).</summary>
    public RevenueData Build(string fallbackCurrency)
    {
        var main = _byCurrency.OrderByDescending(kv => kv.Value.Sum(Math.Abs)).Select(kv => kv.Key).FirstOrDefault()
            ?? fallbackCurrency.ToUpperInvariant();
        return new RevenueData
        {
            Currency = main,
            Daily = _byCurrency.TryGetValue(main, out var d) ? d : new double[RevenueData.Days],
            Today = _today,
            CoveredDays = _covered,
            LeftOut = _byCurrency.Where(kv => kv.Key != main && kv.Value.Any(v => v != 0)).Select(kv => kv.Key).ToList()
        };
    }
}

internal static class FinanceMoney
{
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
        { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };
    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase) { "BHD", "JOD", "KWD", "OMR", "TND" };

    /// <summary>How many smallest units make one unit of <paramref name="currency"/> (100 for USD, 1 for JPY).</summary>
    public static double Divisor(string currency) =>
        ZeroDecimal.Contains(currency) ? 1 : ThreeDecimal.Contains(currency) ? 1000 : 100;

    /// <summary>A change with an arrow, e.g. "▲ 12%" or "▲ new".</summary>
    public static string Change(double now, double before) =>
        (now >= before ? "▲ " : "▼ ") + Online.Charts.Delta(now, before).TrimStart('+', '-');

    /// <summary>Full amount for cards: "$12,345" or "$950.20".</summary>
    public static string Full(double amount, string currency)
    {
        string sym = Online.Charts.Money(0, currency)[..^1]; // "$0" → "$", "SEK 0" → "SEK "
        double a = Math.Abs(amount);
        string fmt = a >= 1000 || Divisor(currency) == 1 ? "#,0" : "#,0.00";
        return (amount < 0 ? "-" : "") + sym + a.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
    }
}
