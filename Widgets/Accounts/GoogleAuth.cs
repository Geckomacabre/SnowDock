using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

/// <summary>
/// Google sign-in for the Gmail and Search Console widgets, using the person's own OAuth client (type "Desktop app")
/// from Google Cloud Console. It's the installed-app flow: the default browser opens Google's consent page, Google
/// redirects to a one-shot listener on 127.0.0.1, and the code is exchanged (with PKCE) for a refresh token that is
/// kept, encrypted, in <see cref="KeyStore"/>. https://developers.google.com/identity/protocols/oauth2/native-app
/// </summary>
internal static class GoogleAuth
{
    private const string RefreshKey = "google-oauth-refresh", ClientIdKey = "google-oauth-client-id",
        SecretKey = "google-oauth-client-secret", ScopesKey = "google-oauth-scopes";
    private const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    public const string GmailScope = "https://www.googleapis.com/auth/gmail.readonly";
    public const string SearchConsoleScope = "https://www.googleapis.com/auth/webmasters.readonly";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _access;
    private static DateTime _accessExpires;
    private static CancellationTokenSource? _flow;

    public static string ClientId => KeyStore.Get(ClientIdKey) ?? "";
    public static bool HasSecret => KeyStore.Has(SecretKey);
    public static bool Connected => KeyStore.Has(RefreshKey) && KeyStore.Has(ClientIdKey);
    public static bool Connecting => _flow != null;
    /// <summary>Whether the person allowed this scope (Google lets them untick one on the consent page).</summary>
    public static bool Granted(string scope) => (_scopes ??= KeyStore.Get(ScopesKey) ?? "").Split(' ').Contains(scope);
    private static string? _scopes; // decrypted once; tiles ask every second

    /// <summary>Save the OAuth client. A blank secret keeps the saved one; a changed client ID signs out.</summary>
    public static void SaveClient(string clientId, string secret)
    {
        clientId = clientId.Trim();
        if (clientId != ClientId) Forget();
        KeyStore.Set(ClientIdKey, clientId.Length > 0 ? clientId : null);
        if (secret.Trim().Length > 0) KeyStore.Set(SecretKey, secret);
    }

    /// <summary>A current access token (cached in memory until shortly before it expires). Thread-pool safe.</summary>
    public static async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_access != null && DateTime.UtcNow < _accessExpires) return _access;
            string refresh = KeyStore.Get(RefreshKey) ?? throw new WebError("Not connected to Google");
            // Refreshing an access token: https://developers.google.com/identity/protocols/oauth2/native-app#offline
            using var doc = await PostForm(TokenUrl, WithSecret(new()
            {
                ["client_id"] = ClientId, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token"
            }), ct).ConfigureAwait(false);
            Remember(doc.RootElement);
            return _access ?? throw new WebError("Unexpected reply");
        }
        finally { Gate.Release(); }
    }

    private static Dictionary<string, string> WithSecret(Dictionary<string, string> form)
    {
        if (KeyStore.Get(SecretKey) is { Length: > 0 } secret) form["client_secret"] = secret;
        return form;
    }

    private static void Remember(JsonElement token)
    {
        _access = token.Str("access_token");
        if (_access.Length == 0) _access = null;
        _accessExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, token.Num("expires_in")) - 60);
    }

    /// <summary>
    /// Run the browser sign-in. Call on the UI thread (it saves the result there). Returns null when connected,
    /// otherwise a short reason. Gives up after five minutes.
    /// </summary>
    public static async Task<string?> ConnectAsync()
    {
        if (ClientId.Length == 0) return "Paste your OAuth client ID first.";
        _flow?.Cancel();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _flow = cts;
        var listener = new TcpListener(IPAddress.Loopback, 0); // any free port; Google accepts any port on a loopback redirect
        try
        {
            listener.Start();
            string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = Base64Url(RandomNumberGenerator.GetBytes(16));
            // Step 2 of the installed-app flow: send the person to Google's OAuth 2.0 server with PKCE (S256) and state.
            var query = new Dictionary<string, string>
            {
                ["client_id"] = ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
                ["scope"] = $"{GmailScope} {SearchConsoleScope}", ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256", ["state"] = state
            };
            DockWindow.ShellOpen(AuthUrl + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")));

            var (code, error) = await WaitForRedirect(listener, state, cts.Token);
            if (code == null) return error;

            // Step 5: exchange the code for a refresh and access token.
            using var doc = await PostForm(TokenUrl, WithSecret(new()
            {
                ["code"] = code, ["client_id"] = ClientId, ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verifier
            }), cts.Token);
            string refresh = doc.RootElement.Str("refresh_token");
            if (refresh.Length == 0) return "Google didn't send a sign-in token. Try connecting again.";
            KeyStore.Set(RefreshKey, refresh);
            KeyStore.Set(ScopesKey, _scopes = doc.RootElement.Str("scope"));
            await Gate.WaitAsync();
            try { Remember(doc.RootElement); } finally { Gate.Release(); }
            return null;
        }
        catch (OperationCanceledException) { return cts.IsCancellationRequested && _flow != cts ? "Cancelled." : "Timed out waiting for the Google sign-in."; }
        catch (WebError e) { return e.Message; }
        catch (Exception e) when (e is SocketException or IOException) { return "Couldn't listen for Google's reply on this PC."; }
        finally
        {
            listener.Stop();
            if (_flow == cts) _flow = null;
            cts.Dispose();
        }
    }

    /// <summary>Sign out: forget the refresh token here and ask Google to revoke it (best effort, in the background).</summary>
    public static void Forget()
    {
        if (KeyStore.Get(RefreshKey) is { Length: > 0 } refresh)
            // Revoking a token: https://developers.google.com/identity/protocols/oauth2/native-app#tokenrevoke
            _ = Task.Run(async () => { try { using var _ = await PostForm(RevokeUrl, new() { ["token"] = refresh }, default); } catch { } });
        KeyStore.Set(RefreshKey, null);
        KeyStore.Set(ScopesKey, null);
        _scopes = null;
        _access = null;
    }

    /// <summary>Accept connections on the loopback listener until Google's redirect (with our state) arrives.</summary>
    private static async Task<(string? Code, string? Error)> WaitForRedirect(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            string target = await ReadTarget(stream, ct).ConfigureAwait(false);
            int qi = target.IndexOf('?');
            var q = (qi >= 0 ? target[(qi + 1)..] : "").Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(kv => kv.Split('=', 2))
                .GroupBy(kv => Uri.UnescapeDataString(kv[0]))
                .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First().ElementAtOrDefault(1)?.Replace('+', ' ') ?? ""));
            if (!q.ContainsKey("code") && !q.ContainsKey("error")) { await Reply(stream, "404 Not Found", "Nothing here.", ct); continue; }
            if (q.GetValueOrDefault("state") != state)
            {
                await Reply(stream, "400 Bad Request", "This sign-in doesn't match the one SnowDock started. Start again from the widget's card.", ct);
                return (null, "The sign-in reply didn't match. Try again.");
            }
            if (q.TryGetValue("error", out var err))
            {
                await Reply(stream, "200 OK", "Sign-in cancelled. You can close this tab.", ct);
                return (null, err == "access_denied" ? "Google access was declined." : "Google sign-in failed.");
            }
            await Reply(stream, "200 OK", "SnowDock is connected to Google. You can close this tab.", ct);
            return (q["code"], null);
        }
    }

    /// <summary>The request target from "GET /?code=… HTTP/1.1" (reads at most 16 KB of headers).</summary>
    private static async Task<string> ReadTarget(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[16384];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
            if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n")) break;
        }
        var first = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0].Split(' ');
        return first.Length >= 2 ? first[1] : "";
    }

    private static async Task Reply(Stream stream, string status, string message, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>SnowDock</title>"
            + $"<body style=\"font:16px Segoe UI,sans-serif;margin:3em\">{WebUtility.HtmlEncode(message)}</body>");
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    /// <summary>POST a form to Google's token endpoints. Error replies become fixed messages (never the reply body).</summary>
    private static async Task<JsonDocument> PostForm(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        HttpResponseMessage resp;
        try { resp = await Web.Http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new WebError("Timed out"); }
        catch (HttpRequestException) { throw new WebError("Offline or unreachable"); }
        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(text); } catch (JsonException) { }
            if (resp.IsSuccessStatusCode && doc != null) return doc;
            string error = doc?.RootElement.Str("error") ?? "";
            doc?.Dispose();
            throw error switch
            {
                "invalid_grant" => new WebError("Google sign-in expired: connect again in the card", resp.StatusCode),
                "invalid_client" or "unauthorized_client" => new WebError("Google refused the client ID or secret: check the card", resp.StatusCode),
                _ => new WebError(doc == null ? "Unexpected reply" : Web.Describe(resp.StatusCode), resp.StatusCode)
            };
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
