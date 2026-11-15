using System.Text.Json;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Markets;

internal sealed record StockQuote(string Symbol, double Price, double Change, double ChangePct, double Open, double High, double Low,
    double PrevClose, DateTime At, IReadOnlyList<double> Line)
{
    public PricePoint Point => new(Symbol, Price, ChangePct, PrevClose, Line);
}

/// <summary>
/// Stock quotes from Finnhub, shared by every stock tile: one request per symbol at most every
/// <see cref="Ttl"/>, however many tiles show it.
///
/// Finnhub's free plan includes quotes but not candles (the docs mark /stock/candle "Premium Access
/// Required"), so the day's line is built here from the quotes sampled while SnowDock runs, starting from the
/// day's open. It lives in memory only.
/// </summary>
internal static class Finnhub
{
    public const string Service = "finnhub";
    /// <summary>Under the tiles' 2-minute interval; Finnhub asks clients not to poll constantly.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(100);

    private sealed class Book
    {
        public StockQuote? Last;
        public DateTime At;
        public Task<StockQuote>? Pending;
        public DateTime Day;
        public long LastTradeTime;
        public readonly List<double> Samples = new();
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Book> Books = new();

    /// <summary>Upper-case letters, digits, dots and dashes only (e.g. "BRK.B").</summary>
    public static string Clean(string symbol) => new(symbol.Trim().ToUpperInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':').ToArray());

    public static Task<StockQuote> Get(string symbol, string key, CancellationToken ct)
    {
        Task<StockQuote> task;
        lock (Gate)
        {
            if (!Books.TryGetValue(symbol, out var b)) Books[symbol] = b = new Book();
            if (b.Last != null && DateTime.UtcNow - b.At < Ttl) return Task.FromResult(b.Last);
            task = b.Pending ??= Task.Run(() => Fetch(symbol, key));
        }
        return task.WaitAsync(ct);
    }

    private static async Task<StockQuote> Fetch(string symbol, string key)
    {
        try
        {
            // Quote endpoint: https://finnhub.io/docs/api/quote — GET /api/v1/quote?symbol=…; the key goes in the
            // "token" query parameter, as the official spec (https://finnhub.io/static/swagger.json) defines it.
            using var doc = await Web.GetJson($"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(key)}")
                .ConfigureAwait(false);
            var r = doc.RootElement;
            double c = Num(r, "c");
            if (c <= 0) throw new WebError($"No quote for {symbol}: check the symbol");
            long t = (long)Num(r, "t");
            double o = Num(r, "o"), pc = Num(r, "pc");
            var at = t > 0 ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime : DateTime.UtcNow;
            lock (Gate)
            {
                var b = Books[symbol];
                // US trading days fall within one UTC date, so the UTC date of the last trade identifies the session.
                if (b.Day != at.Date)
                {
                    b.Day = at.Date;
                    b.Samples.Clear();
                    if (o > 0) b.Samples.Add(o);
                    b.LastTradeTime = 0;
                }
                // A new trade time means a new price; without one, sample every fetch rather than never.
                if (t <= 0 || t != b.LastTradeTime) { b.Samples.Add(c); b.LastTradeTime = t; }
                if (b.Samples.Count > 400) b.Samples.RemoveAt(1); // keep the open, thin the oldest samples
                var q = new StockQuote(symbol, c, Num(r, "d"), Num(r, "dp"), o, Num(r, "h"), Num(r, "l"), pc, at.ToLocalTime(), b.Samples.ToList());
                b.Last = q;
                b.At = DateTime.UtcNow;
                b.Pending = null;
                return q;
            }
        }
        catch (Exception ex)
        {
            lock (Gate) Books[symbol].Pending = null;
            // 401 is a bad key; 403 also comes back for symbols the free plan doesn't cover (most non-US listings).
            if (ex is WebError { Status: System.Net.HttpStatusCode.Forbidden })
                throw new WebError($"Finnhub refused {symbol}: check the key, or it may need a paid plan");
            throw;
        }
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>Made-up quotes for the sample tiles: a gentle random walk from the open.</summary>
    public static StockQuote Sample(string symbol, double open, double prevClose, int seed)
    {
        var rnd = new Random(seed);
        var line = new List<double> { open };
        for (int i = 0; i < 40; i++) line.Add(line[^1] * (1 + (rnd.NextDouble() - 0.47) * 0.004));
        double c = line[^1];
        return new StockQuote(symbol, c, c - prevClose, (c - prevClose) / prevClose * 100, open, line.Max(), line.Min(), prevClose, DateTime.Now, line);
    }
}
