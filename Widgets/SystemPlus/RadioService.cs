using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace SnowDock.Widgets.SystemPlus;

internal enum Switch { Unknown, Missing, Denied, Off, On, Busy }
internal enum QuickKind { Wifi, Bluetooth, Hotspot, Airplane }

/// <summary>
/// Wi-Fi and Bluetooth radios (Windows.Devices.Radios) and the mobile hotspot (tethering on the internet
/// connection). Reads every few seconds on the thread pool; switching is only ever done for a user's click.
/// </summary>
internal static class RadioService
{
    private static RadioAccessStatus? _access;
    private static IReadOnlyList<Radio>? _radios;
    private static DateTime _radiosAt, _last = DateTime.MinValue;
    private static int _busy;

    public static Switch Wifi { get; private set; }
    public static Switch Bluetooth { get; private set; }
    public static Switch Hotspot { get; private set; }
    /// <summary>Why the hotspot can't be used (empty when it can).</summary>
    public static string HotspotNote { get; private set; } = "";
    public static bool Ready { get; private set; }
    public static int Version { get; private set; }
    public static event Action? Changed;

    public static Switch Get(QuickKind k) => k switch
    {
        QuickKind.Wifi => Wifi, QuickKind.Bluetooth => Bluetooth, QuickKind.Hotspot => Hotspot, _ => Switch.Unknown
    };

    public static void Request(bool now = false)
    {
        if (!now && (DateTime.UtcNow - _last).TotalSeconds < 3) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _last = DateTime.UtcNow;
        var ui = System.Windows.Application.Current?.Dispatcher;
        Task.Run(async () =>
        {
            (Switch w, Switch b, Switch h, string note) r = (Switch.Unknown, Switch.Unknown, Switch.Unknown, "");
            try { r = await ReadAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppPaths.Log($"Radio read failed: {ex.Message}"); }
            ui?.BeginInvoke(() =>
            {
                bool changed = !Ready || (Wifi, Bluetooth, Hotspot, HotspotNote) != r;
                (Wifi, Bluetooth, Hotspot, HotspotNote, Ready) = (r.w, r.b, r.h, r.note, true);
                Interlocked.Exchange(ref _busy, 0);
                if (!changed) return;
                Version++;
                Changed?.Invoke();
            });
        });
    }

    private static async Task<(Switch, Switch, Switch, string)> ReadAsync()
    {
        Switch wifi = Switch.Missing, bt = Switch.Missing;
        _access ??= await Radio.RequestAccessAsync().AsTask().ConfigureAwait(false);
        if (_access != RadioAccessStatus.Allowed) wifi = bt = Switch.Denied;
        else
        {
            if (_radios == null || (DateTime.UtcNow - _radiosAt).TotalSeconds > 30)
            {
                _radios = await Radio.GetRadiosAsync().AsTask().ConfigureAwait(false);
                _radiosAt = DateTime.UtcNow;
            }
            foreach (var radio in _radios)
            {
                var s = radio.State == RadioState.On ? Switch.On : radio.State is RadioState.Off or RadioState.Disabled ? Switch.Off : Switch.Unknown;
                if (radio.Kind == RadioKind.WiFi && wifi != Switch.On) wifi = s;
                if (radio.Kind == RadioKind.Bluetooth && bt != Switch.On) bt = s;
            }
        }
        var (hotspot, note) = ReadHotspot();
        return (wifi, bt, hotspot, note);
    }

    private static (Switch, string) ReadHotspot()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null) return (Switch.Missing, "No internet connection to share.");
            var cap = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile);
            if (cap != TetheringCapability.Enabled) return (Switch.Missing, cap switch
            {
                TetheringCapability.DisabledByHardwareLimitation => "This PC's Wi-Fi can't host a hotspot (or there's no Wi-Fi).",
                TetheringCapability.DisabledByGroupPolicy => "Turned off by your organisation's policy.",
                TetheringCapability.DisabledBySku or TetheringCapability.DisabledBySystemCapability => "Not available on this edition of Windows.",
                _ => "Windows says this connection can't be shared."
            });
            var mgr = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            return (mgr.TetheringOperationalState switch
            {
                TetheringOperationalState.On => Switch.On,
                TetheringOperationalState.Off => Switch.Off,
                TetheringOperationalState.InTransition => Switch.Busy,
                _ => Switch.Unknown
            }, "");
        }
        catch (Exception ex) { return (Switch.Missing, $"Couldn't read the hotspot ({ex.Message.Trim()})."); }
    }

    /// <summary>Turn a radio or the hotspot on or off (a user's click). Shows "busy" until it settles.</summary>
    public static void Toggle(QuickKind kind)
    {
        var current = Get(kind);
        if (current is not (Switch.On or Switch.Off)) return;
        bool on = current == Switch.Off;
        if (kind == QuickKind.Wifi) Wifi = Switch.Busy;
        else if (kind == QuickKind.Bluetooth) Bluetooth = Switch.Busy;
        else if (kind == QuickKind.Hotspot) Hotspot = Switch.Busy;
        Version++;
        Changed?.Invoke();
        Task.Run(async () =>
        {
            try
            {
                if (kind == QuickKind.Hotspot)
                {
                    var profile = NetworkInformation.GetInternetConnectionProfile();
                    var mgr = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
                    var result = on ? await mgr.StartTetheringAsync().AsTask().ConfigureAwait(false)
                                    : await mgr.StopTetheringAsync().AsTask().ConfigureAwait(false);
                    if (result.Status != TetheringOperationStatus.Success) AppPaths.Log($"Hotspot: {result.Status} {result.AdditionalErrorMessage}");
                }
                else
                {
                    var want = kind == QuickKind.Wifi ? RadioKind.WiFi : RadioKind.Bluetooth;
                    foreach (var radio in _radios ?? Array.Empty<Radio>())
                        if (radio.Kind == want)
                        {
                            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask().ConfigureAwait(false);
                            if (result != RadioAccessStatus.Allowed) AppPaths.Log($"Radio {want}: {result}");
                        }
                }
            }
            catch (Exception ex) { AppPaths.Log($"Toggle {kind} failed: {ex.Message}"); }
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Request(now: true));
        });
    }
}
