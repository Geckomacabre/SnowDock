using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SnowDock.Widgets.Online;

/// <summary>
/// API keys and tokens you paste into widgets. Each is encrypted with Windows' per-user data protection
/// (DPAPI), so only your Windows account on this PC can read it, and stored in keys.json next to the
/// settings. A key is only ever sent to the service it belongs to.
/// </summary>
internal static class KeyStore
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB data, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DATA_BLOB result);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB data, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DATA_BLOB result);

    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;
    private static string FilePath => WidgetEnv.DataFile("keys.json");
    private static Dictionary<string, string>? _cache;

    private static Dictionary<string, string> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { _cache = new(); }
        return _cache;
    }

    public static bool Has(string service) => Load().ContainsKey(service);

    /// <summary>The decrypted key for a service, or null.</summary>
    public static string? Get(string service)
    {
        if (!Load().TryGetValue(service, out var b64)) return null;
        try { return Unprotect(Convert.FromBase64String(b64)); }
        catch { return null; }
    }

    /// <summary>Save (or, with null/empty, forget) a service's key.</summary>
    public static void Set(string service, string? value)
    {
        var all = Load();
        if (string.IsNullOrWhiteSpace(value)) all.Remove(service);
        else all[service] = Convert.ToBase64String(Protect(value.Trim()));
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { AppPaths.Log($"Saving keys failed: {ex.Message}"); }
    }

    private static byte[] Protect(string text)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        var handle = GCHandle.Alloc(plain, GCHandleType.Pinned);
        try
        {
            var input = new DATA_BLOB { cbData = plain.Length, pbData = handle.AddrOfPinnedObject() };
            if (!CryptProtectData(ref input, "SnowDock widget key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var output))
                throw new InvalidOperationException($"DPAPI protect failed ({Marshal.GetLastWin32Error()})");
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally { LocalFree(output.pbData); }
        }
        finally
        {
            Array.Clear(plain);
            handle.Free();
        }
    }

    private static string Unprotect(byte[] cipher)
    {
        var handle = GCHandle.Alloc(cipher, GCHandleType.Pinned);
        try
        {
            var input = new DATA_BLOB { cbData = cipher.Length, pbData = handle.AddrOfPinnedObject() };
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var output))
                throw new InvalidOperationException($"DPAPI unprotect failed ({Marshal.GetLastWin32Error()})");
            try
            {
                var plain = new byte[output.cbData];
                Marshal.Copy(output.pbData, plain, 0, output.cbData);
                return Encoding.UTF8.GetString(plain);
            }
            finally { LocalFree(output.pbData); }
        }
        finally { handle.Free(); }
    }
}
