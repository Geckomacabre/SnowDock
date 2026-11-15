using System.Runtime.InteropServices;

namespace SnowDock.Widgets.Media;

/// <summary>The Core Audio COM interfaces this needs, declared in the SDK headers' vtable order.</summary>
internal static class CoreAudioCom
{
    public const int Render = 0, Capture = 1, Console = 0, ClsCtxAll = 23, VtLpwstr = 31;
    public static readonly Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static PropertyKey FriendlyName = new() { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey { public Guid FormatId; public int PropertyId; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant value);
}

/// <summary>The default speakers or microphone as last read.</summary>
internal sealed record Endpoint(bool Present, string Name, float Volume, bool Muted)
{
    public static readonly Endpoint None = new(false, "", 0, false);
}

/// <summary>
/// The default output and input devices' volume and mute. All COM work runs on the thread pool, one call at a
/// time; results come back to the UI thread. Reads at most about once a second.
/// </summary>
internal static class AudioService
{
    private static readonly object Gate = new();
    private static CoreAudioCom.IMMDeviceEnumerator? _enumerator;
    private static readonly Dictionary<int, (string Id, CoreAudioCom.IAudioEndpointVolume Volume, string Name)> Cached = new();
    private static DateTime _last = DateTime.MinValue;
    private static int _busy;
    private static float _pendingVolume = -1;

    public static Endpoint Speakers { get; private set; } = Endpoint.None;
    public static Endpoint Microphone { get; private set; } = Endpoint.None;
    /// <summary>True once the first reading has arrived.</summary>
    public static bool Ready { get; private set; }
    public static int Version { get; private set; }
    /// <summary>Raised on the UI thread after each reading that changed something.</summary>
    public static event Action? Changed;

    /// <summary>Read again unless a reading is under a second old (or already running).</summary>
    public static void Request(bool now = false)
    {
        if (!now && (DateTime.UtcNow - _last).TotalMilliseconds < 900) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _last = DateTime.UtcNow;
        Run(() => { });
    }

    /// <summary>Set the speakers' volume (0..1). Rapid calls (a slider drag) collapse to the latest value.</summary>
    public static void SetVolume(float level)
    {
        level = Math.Clamp(level, 0, 1);
        Speakers = Speakers with { Volume = level };
        Interlocked.Exchange(ref _pendingVolume, level);
        Run(() =>
        {
            float v = Interlocked.Exchange(ref _pendingVolume, -1);
            if (v < 0) return;
            var ctx = Guid.Empty;
            Volume(CoreAudioCom.Render)?.SetMasterVolumeLevelScalar(v, ref ctx);
        });
    }

    public static void SetMute(bool capture, bool mute)
    {
        if (capture) Microphone = Microphone with { Muted = mute }; else Speakers = Speakers with { Muted = mute };
        Run(() =>
        {
            var ctx = Guid.Empty;
            Volume(capture ? CoreAudioCom.Capture : CoreAudioCom.Render)?.SetMute(mute, ref ctx);
        });
    }

    /// <summary>Do <paramref name="work"/> on the pool under the lock, then read both devices and publish.</summary>
    private static void Run(Action work)
    {
        var ui = System.Windows.Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            Endpoint speakers, mic;
            lock (Gate)
            {
                try { work(); }
                catch (Exception ex) { AppPaths.Log($"Audio change failed: {ex.Message}"); }
                speakers = Read(CoreAudioCom.Render);
                mic = Read(CoreAudioCom.Capture);
            }
            ui?.BeginInvoke(() =>
            {
                bool changed = speakers != Speakers || mic != Microphone || !Ready;
                (Speakers, Microphone, Ready) = (speakers, mic, true);
                Interlocked.Exchange(ref _busy, 0);
                if (!changed) return;
                Version++;
                Changed?.Invoke();
            });
        });
    }

    private static Endpoint Read(int flow)
    {
        try
        {
            var vol = Volume(flow);
            if (vol == null) return Endpoint.None;
            vol.GetMasterVolumeLevelScalar(out float level);
            vol.GetMute(out bool muted);
            return new Endpoint(true, Cached[flow].Name, level, muted);
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Audio read failed: {ex.Message}");
            Cached.Remove(flow);
            return Endpoint.None;
        }
    }

    /// <summary>The default device's volume control for a direction (re-found when the default changes). Call under the lock.</summary>
    private static CoreAudioCom.IAudioEndpointVolume? Volume(int flow)
    {
        _enumerator ??= (CoreAudioCom.IMMDeviceEnumerator)new CoreAudioCom.MMDeviceEnumerator();
        if (_enumerator.GetDefaultAudioEndpoint(flow, CoreAudioCom.Console, out var device) != 0 || device == null)
        {
            Cached.Remove(flow);
            return null;
        }
        try
        {
            if (device.GetId(out var id) != 0) return null;
            if (Cached.TryGetValue(flow, out var hit) && hit.Id == id) return hit.Volume;
            var iid = CoreAudioCom.AudioEndpointVolumeId;
            if (device.Activate(ref iid, CoreAudioCom.ClsCtxAll, IntPtr.Zero, out var obj) != 0) return null;
            var vol = (CoreAudioCom.IAudioEndpointVolume)obj;
            Cached[flow] = (id, vol, FriendlyName(device));
            return vol;
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    private static string FriendlyName(CoreAudioCom.IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out var store) != 0 || store == null) return "";
        try
        {
            var key = CoreAudioCom.FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0) return "";
            try { return pv.Type == CoreAudioCom.VtLpwstr ? Marshal.PtrToStringUni(pv.Pointer) ?? "" : ""; }
            finally { CoreAudioCom.PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
}
