using System.IO;
using Windows.Media.Control;
using Imaging = System.Windows.Media.Imaging;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace SnowDock.Widgets.Media;

/// <summary>What's playing, as last read from Windows' media controls.</summary>
internal sealed record Track(string Title, string Artist, string Album, string App, bool Playing,
    TimeSpan Position, TimeSpan Duration, DateTime PositionAtUtc, double Rate, bool CanPrevious, bool CanNext, bool CanPlayPause)
{
    /// <summary>The position now, carried forward from the last report while playing (apps only report it now and then).</summary>
    public TimeSpan PositionNow
    {
        get
        {
            if (Duration <= TimeSpan.Zero) return TimeSpan.Zero;
            var p = Playing ? Position + (DateTime.UtcNow - PositionAtUtc) * Rate : Position;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p > Duration ? Duration : p;
        }
    }

    /// <summary>0..1, or 0 when the app doesn't report a length.</summary>
    public double Progress => Duration <= TimeSpan.Zero ? 0 : PositionNow.TotalSeconds / Duration.TotalSeconds;
}

internal enum MediaCommand { Previous, PlayPause, Next }

/// <summary>
/// The current media session (whatever Windows shows in its media flyout). WinRT calls run on the thread pool;
/// results, including the decoded, frozen cover art, are handed to the UI thread.
/// </summary>
internal static class MediaService
{
    private static GsmtcManager? _manager;
    private static GsmtcSession? _watched;
    private static int _dirty = 1, _busy;
    private static DateTime _last = DateTime.MinValue;
    private static string _artKey = "";

    public static Track? Current { get; private set; }
    public static System.Windows.Media.ImageSource? Art { get; private set; }
    /// <summary>True after the first read finished (so "nothing playing" is a real answer, not "still loading").</summary>
    public static bool Ready { get; private set; }
    public static int Version { get; private set; }
    /// <summary>Raised on the UI thread whenever the track, state or art changed.</summary>
    public static event Action? Changed;

    /// <summary>Re-read when Windows said something changed, or every two seconds anyway.</summary>
    public static void Request()
    {
        bool due = Volatile.Read(ref _dirty) == 1 || (DateTime.UtcNow - _last).TotalSeconds >= 2;
        if (!due || Interlocked.Exchange(ref _busy, 1) == 1) return;
        _last = DateTime.UtcNow;
        Interlocked.Exchange(ref _dirty, 0);
        var ui = System.Windows.Application.Current?.Dispatcher;
        Task.Run(async () =>
        {
            Track? track = null;
            System.Windows.Media.ImageSource? art = Art;
            bool artChanged = false;
            try
            {
                _manager ??= await CreateManager().ConfigureAwait(false);
                var session = _manager.GetCurrentSession();
                Watch(session);
                if (session != null)
                {
                    var props = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
                    var info = session.GetPlaybackInfo();
                    var tl = session.GetTimelineProperties();
                    var duration = tl.EndTime - tl.StartTime;
                    var at = tl.LastUpdatedTime.Year > 2000 ? tl.LastUpdatedTime.UtcDateTime : DateTime.UtcNow;
                    track = new Track(props?.Title ?? "", props?.Artist is { Length: > 0 } a ? a : props?.AlbumArtist ?? "",
                        props?.AlbumTitle ?? "", AppName(session.SourceAppUserModelId),
                        info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        tl.Position - tl.StartTime, duration > TimeSpan.Zero ? duration : TimeSpan.Zero, at, info.PlaybackRate ?? 1,
                        info.Controls.IsPreviousEnabled, info.Controls.IsNextEnabled, info.Controls.IsPlayPauseToggleEnabled);
                    string key = $"{track.App}|{track.Title}|{track.Artist}|{track.Album}";
                    if (key != _artKey)
                    {
                        art = props?.Thumbnail == null ? null : await LoadArt(props.Thumbnail).ConfigureAwait(false);
                        _artKey = key;
                        artChanged = true;
                    }
                }
                else if (_artKey != "") { art = null; _artKey = ""; artChanged = true; }
            }
            catch (Exception ex) { AppPaths.Log($"Now playing read failed: {ex.Message}"); }
            ui?.BeginInvoke(() =>
            {
                bool changed = !Ready || artChanged || !Same(track, Current);
                (Current, Ready) = (track, true);
                if (artChanged) Art = art;
                Interlocked.Exchange(ref _busy, 0);
                if (!changed) return;
                Version++;
                Changed?.Invoke();
            });
        });
    }

    /// <summary>Same track and state, ignoring the small drift in reported position.</summary>
    private static bool Same(Track? a, Track? b) =>
        a == null || b == null ? a == b
            : a with { Position = default, PositionAtUtc = default } == b with { Position = default, PositionAtUtc = default }
              && Math.Abs((a.PositionNow - b.PositionNow).TotalSeconds) < 2;

    private static async Task<GsmtcManager> CreateManager()
    {
        var m = await GsmtcManager.RequestAsync().AsTask().ConfigureAwait(false);
        m.CurrentSessionChanged += (_, _) => Interlocked.Exchange(ref _dirty, 1);
        m.SessionsChanged += (_, _) => Interlocked.Exchange(ref _dirty, 1);
        return m;
    }

    /// <summary>Follow the current session's change events (they only mark the state stale).</summary>
    private static void Watch(GsmtcSession? session)
    {
        if (ReferenceEquals(session, _watched)) return;
        if (_watched != null)
        {
            _watched.MediaPropertiesChanged -= OnSessionChanged;
            _watched.PlaybackInfoChanged -= OnPlaybackChanged;
            _watched.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _watched = session;
        if (session == null) return;
        session.MediaPropertiesChanged += OnSessionChanged;
        session.PlaybackInfoChanged += OnPlaybackChanged;
        session.TimelinePropertiesChanged += OnTimelineChanged;
    }

    private static void OnSessionChanged(GsmtcSession s, MediaPropertiesChangedEventArgs e) => Interlocked.Exchange(ref _dirty, 1);
    private static void OnPlaybackChanged(GsmtcSession s, PlaybackInfoChangedEventArgs e) => Interlocked.Exchange(ref _dirty, 1);
    private static void OnTimelineChanged(GsmtcSession s, TimelinePropertiesChangedEventArgs e) => Interlocked.Exchange(ref _dirty, 1);

    /// <summary>Decode the cover off the UI thread, small, and frozen so the UI thread can draw it.</summary>
    private static async Task<System.Windows.Media.ImageSource?> LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference thumb)
    {
        try
        {
            using var ras = await thumb.OpenReadAsync().AsTask().ConfigureAwait(false);
            using var src = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await src.CopyToAsync(ms).ConfigureAwait(false);
            ms.Position = 0;
            var bmp = new Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 192;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Cover art failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Send a transport command to the current session (play/pause flips at once, then is re-read).</summary>
    public static void Send(MediaCommand command)
    {
        var session = _watched;
        if (session == null || Current is not Track t) return;
        if (command == MediaCommand.PlayPause && t.CanPlayPause)
        {
            Current = t with { Playing = !t.Playing, Position = t.PositionNow, PositionAtUtc = DateTime.UtcNow };
            Version++;
            Changed?.Invoke();
        }
        Task.Run(async () =>
        {
            try
            {
                switch (command)
                {
                    case MediaCommand.Previous: await session.TrySkipPreviousAsync().AsTask().ConfigureAwait(false); break;
                    case MediaCommand.Next: await session.TrySkipNextAsync().AsTask().ConfigureAwait(false); break;
                    default: await session.TryTogglePlayPauseAsync().AsTask().ConfigureAwait(false); break;
                }
            }
            catch (Exception ex) { AppPaths.Log($"Media command {command} failed: {ex.Message}"); }
            Interlocked.Exchange(ref _dirty, 1);
        });
    }

    /// <summary>A readable name for the app behind a session (its id is an exe name or a package id).</summary>
    public static string AppName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        string s = id.Contains('!') ? id[(id.LastIndexOf('!') + 1)..] : id;
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        s = s.Split('\\').Last();
        return s.ToLowerInvariant() switch
        {
            "spotify" => "Spotify",
            "chrome" => "Chrome",
            "msedge" or "microsoftedge" => "Edge",
            "firefox" or "308046b0af4a39cb" => "Firefox",
            "microsoft.zunemusic" or "zunemusic" => "Media Player",
            "vlc" => "VLC",
            _ => s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s
        };
    }
}
