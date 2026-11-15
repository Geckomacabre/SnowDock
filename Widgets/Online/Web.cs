using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SnowDock.Widgets.Online;

/// <summary>A failed request, with a message fit to show on a tile or card.</summary>
internal sealed class WebError : Exception
{
    public HttpStatusCode? Status { get; }
    public WebError(string message, HttpStatusCode? status = null) : base(message) => Status = status;
}

/// <summary>
/// One shared HTTP client for every online widget. It sends a plain "SnowDock" user agent and nothing about
/// you; each widget talks only to its own service.
/// </summary>
internal static class Web
{
    public static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10), AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SnowDock", "1.0"));
        return c;
    }

    /// <summary>GET a URL and parse JSON. <paramref name="configure"/> adds headers such as Authorization.</summary>
    public static Task<JsonDocument> GetJson(string url, Action<HttpRequestMessage>? configure = null, CancellationToken ct = default) =>
        Send(HttpMethod.Get, url, null, configure, ct);

    /// <summary>POST a JSON body (string or object) and parse the JSON reply.</summary>
    public static Task<JsonDocument> PostJson(string url, object body, Action<HttpRequestMessage>? configure = null, CancellationToken ct = default) =>
        Send(HttpMethod.Post, url, body is string s ? s : JsonSerializer.Serialize(body), configure, ct);

    public static async Task<JsonDocument> Send(HttpMethod method, string url, string? jsonBody, Action<HttpRequestMessage>? configure, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        configure?.Invoke(req);
        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new WebError("Timed out"); }
        catch (HttpRequestException) { throw new WebError("Offline or unreachable"); }
        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new WebError(Describe(resp.StatusCode), resp.StatusCode);
            try { return JsonDocument.Parse(text); }
            catch (JsonException) { throw new WebError("Unexpected reply"); }
        }
    }

    /// <summary>Download raw bytes (images).</summary>
    public static async Task<byte[]> GetBytes(string url, CancellationToken ct = default)
    {
        try { return await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new WebError(ex.StatusCode is HttpStatusCode s ? Describe(s) : "Offline or unreachable", ex.StatusCode); }
    }

    public static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Key refused: check it in the card",
        HttpStatusCode.NotFound => "Not found: check the settings in the card",
        HttpStatusCode.TooManyRequests => "Rate limited: will retry",
        >= HttpStatusCode.InternalServerError => "Service is having trouble",
        _ => $"Request failed ({(int)status})"
    };

    /// <summary>A short message for any exception a fetch threw.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        WebError w => w.Message,
        AggregateException { InnerException: { } inner } => Friendly(inner),
        KeyNotFoundException or InvalidOperationException or FormatException => "Unexpected reply",
        _ => "Couldn't update"
    };

    public static void Bearer(HttpRequestMessage req, string token) =>
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static void Basic(HttpRequestMessage req, string user, string password) =>
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
}
