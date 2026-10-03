using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using PerfHud.Core;
using PerfHud.Settings;

namespace PerfHud.Monitoring.Monitors;

/// <summary>What's playing, as last read. <see cref="PositionAt"/> extrapolates between polls so progress bars move smoothly.</summary>
public sealed record MediaSnapshot(
    string Title, string Artist, string Album, string App, bool Playing,
    double Position, double Duration, double Rate, long Ticks, ImageSource? Art)
{
    public double PositionAt(long tickCount)
    {
        double p = Playing ? Position + (tickCount - Ticks) / 1000.0 * Rate : Position;
        return Duration > 0 ? Math.Clamp(p, 0, Duration) : Math.Max(0, p);
    }
}

/// <summary>
/// Now playing from any app that reports to Windows' media controls (Spotify, browsers, Media Player, VLC 4, …)
/// via GlobalSystemMediaTransportControlsSessionManager. Read-only; nothing leaves the PC.
/// </summary>
public sealed class MediaMonitor : MonitorBase
{
    public override string Name => "Media";
    public override string[] Prefixes => new[] { "media." };
    public override MonitorLane Lane => MonitorLane.Slow;
    public override int GetIntervalMs(MonitorContext ctx) => 1000;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private string _artKey = "";
    private ImageSource? _art;

    public override void Initialize(MonitorContext ctx)
    {
        if (!ctx.Settings.Media.Enabled) Disabled(ctx.Store);
        var task = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        if (!task.Wait(CallTimeout)) throw new MonitorUnavailableException("Windows media controls did not respond");
        _mgr = task.Result ?? throw new MonitorUnavailableException("Windows media controls are not available");
        Detail = "Windows media session";
    }

    public override void Update(MonitorContext ctx)
    {
        var st = ctx.Store;
        var settings = ctx.Settings.Media;
        if (!settings.Enabled) Disabled(st);
        if (_mgr == null) return;

        var session = Pick(_mgr, settings);
        if (session == null) { Clear(st, "Nothing is playing"); return; }

        var propsTask = session.TryGetMediaPropertiesAsync().AsTask();
        if (!propsTask.Wait(CallTimeout)) return; // app is busy; keep the last values
        var props = propsTask.Result;
        var playback = session.GetPlaybackInfo();
        var status = playback?.PlaybackStatus ?? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
        if (props == null || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed || string.IsNullOrEmpty(props.Title))
        {
            Clear(st, "Nothing is playing");
            return;
        }

        bool playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var tl = session.GetTimelineProperties();
        double duration = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
        double rate = playback?.PlaybackRate ?? 1;
        double pos = (tl.Position - tl.StartTime).TotalSeconds;
        // The app only reports position now and then; advance it to "now" while playing.
        if (playing && tl.LastUpdatedTime.Year > 2000) pos += Math.Max(0, (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds) * rate;
        if (duration > 0) pos = Math.Clamp(pos, 0, duration);

        string app = FriendlyApp(session.SourceAppUserModelId);
        string artist = props.Artist ?? "";
        if (string.IsNullOrEmpty(artist)) artist = props.AlbumArtist ?? "";

        if (settings.ShowArtwork)
        {
            var key = $"{session.SourceAppUserModelId}|{props.Title}|{artist}|{props.AlbumTitle}";
            if (key != _artKey)
            {
                _artKey = key;
                _art = LoadArt(props.Thumbnail);
            }
        }
        else { _art = null; _artKey = ""; }

        st.SetText("media.title", props.Title);
        st.SetText("media.artist", string.IsNullOrEmpty(artist) ? null : artist);
        st.SetText("media.album", string.IsNullOrEmpty(props.AlbumTitle) ? null : props.AlbumTitle);
        st.SetText("media.line", string.IsNullOrEmpty(artist) ? props.Title : $"{artist} — {props.Title}");
        st.SetText("media.app", app);
        st.SetText("media.status", playing ? "Playing" : status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused ? "Paused" : status.ToString());
        if (duration > 0)
        {
            st.Set("media.position", pos);
            st.Set("media.duration", duration);
            st.Set("media.remaining", duration - pos);
            st.Set("media.progress", pos / duration * 100);
        }
        else
        {
            foreach (var id in new[] { "media.position", "media.duration", "media.remaining", "media.progress" })
                st.SetUnavailable(id, "Live stream / no timeline");
        }
        st.SetObject("media.snapshot", new MediaSnapshot(props.Title, artist, props.AlbumTitle ?? "", app, playing, pos, duration, rate, Environment.TickCount64, _art));
    }

    private static GlobalSystemMediaTransportControlsSession? Pick(GlobalSystemMediaTransportControlsSessionManager mgr, MediaSettings s)
    {
        var all = mgr.GetSessions().ToList();
        if (!string.IsNullOrWhiteSpace(s.AppFilter))
        {
            var wanted = s.AppFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            all = all.Where(x => wanted.Any(w => x.SourceAppUserModelId.Contains(w, StringComparison.OrdinalIgnoreCase)
                                                || FriendlyApp(x.SourceAppUserModelId).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            if (all.Count == 0) return null;
        }
        var current = mgr.GetCurrentSession();
        if (current != null && !all.Any(x => x.SourceAppUserModelId == current.SourceAppUserModelId)) current = null;
        if (s.PreferPlaying && (current == null || !IsPlaying(current)))
            return all.FirstOrDefault(IsPlaying) ?? current ?? all.FirstOrDefault();
        return current ?? all.FirstOrDefault();
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession x)
    {
        try { return x.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    private static ImageSource? LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            var open = thumb.OpenReadAsync().AsTask();
            if (!open.Wait(CallTimeout)) return null;
            using var winStream = open.Result;
            using var src = winStream.AsStreamForRead();
            var ms = new MemoryStream();
            src.CopyTo(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // created on the monitor thread, drawn on the UI thread
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Once("media-art", LogLevel.Info, $"Could not load media artwork: {ex.Message}");
            return null;
        }
    }

    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "Spotify", ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["308046B0AF4A39CB"] = "Firefox",
        ["brave"] = "Brave", ["opera"] = "Opera", ["vlc"] = "VLC", ["Microsoft.ZuneMusic"] = "Media Player", ["Microsoft.ZuneVideo"] = "Films & TV",
        ["AppleInc.AppleMusicWin"] = "Apple Music", ["foobar2000"] = "foobar2000", ["MusicBee"] = "MusicBee", ["Discord"] = "Discord",
        ["TIDAL"] = "TIDAL", ["Amazon Music"] = "Amazon Music", ["YouTubeMusic"] = "YouTube Music", ["mpv"] = "mpv", ["wmplayer"] = "Windows Media Player",
    };

    /// <summary>"Spotify.exe" → "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "Media Player".</summary>
    public static string FriendlyApp(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        foreach (var (k, v) in KnownApps)
            if (aumid.Contains(k, StringComparison.OrdinalIgnoreCase)) return v;
        var s = aumid;
        int bang = s.LastIndexOf('!');
        if (bang >= 0) s = s[..bang];
        int us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        s = Path.GetFileNameWithoutExtension(s.Replace('/', '\\'));
        int dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        return s;
    }

    private void Disabled(MetricStore st)
    {
        const string reason = "Media tracking is off (Settings → General)";
        Clear(st, reason);
        throw new MonitorUnavailableException(reason);
    }

    private void Clear(MetricStore st, string reason)
    {
        foreach (var id in new[] { "media.title", "media.artist", "media.album", "media.line", "media.app" }) st.SetText(id, null);
        st.SetText("media.status", "Stopped");
        foreach (var id in new[] { "media.position", "media.duration", "media.remaining", "media.progress" }) st.SetUnavailable(id, reason);
        st.SetObject("media.snapshot", null);
        _artKey = "";
        _art = null;
    }

    protected override void DisposeResources()
    {
        _mgr = null;
        _artKey = "";
        _art = null;
    }
}
