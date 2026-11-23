using Microsoft.UI.Dispatching;
using PulseIsland.Core.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PulseIsland.Platform.Media;

/// <summary>What is playing, from Windows' media controls (SMTC). Immutable snapshot.</summary>
internal sealed record NowPlaying(
    string Title,
    string Artist,
    string SourceApp,
    bool IsPlaying,
    MediaTimeline Timeline,
    IRandomAccessStreamReference? Thumbnail,
    bool CanPrevious,
    bool CanNext,
    bool CanSeek,
    int PropertiesVersion)
{
    /// <summary>Identifies the track (for "track changed").</summary>
    public string TrackKey => $"{SourceApp}\n{Title}\n{Artist}";

    /// <summary>
    /// Changes whenever the player re-sends its media properties. Browsers (Chromium: Brave, Chrome, Edge)
    /// first send the title with their app icon and the real artwork moments later, for the same track.
    /// </summary>
    public string ArtKey => $"{TrackKey}\n{PropertiesVersion}";
}

/// <summary>
/// Follows the current media session (Spotify, browsers, VLC, …) through
/// GlobalSystemMediaTransportControlsSessionManager. Events are raised on the UI thread.
/// </summary>
internal sealed class MediaService
{
    private readonly DispatcherQueue _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshVersion;
    private int _propertiesVersion;

    public NowPlaying? Current { get; private set; }

    /// <summary>Anything about the current media changed (track, play state, position, session).</summary>
    public event Action? Changed;

    public MediaService(DispatcherQueue ui) => _ui = ui;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.TryEnqueue(Rebind);
            Rebind();
        }
        catch (Exception e)
        {
            Log.Error("media: SMTC unavailable", e); // island then simply shows "nothing playing"
        }
    }

    public async void TogglePlayPause() => await Try(s => s.TryTogglePlayPauseAsync().AsTask());
    public async void Next() => await Try(s => s.TrySkipNextAsync().AsTask());
    public async void Previous() => await Try(s => s.TrySkipPreviousAsync().AsTask());
    public async void Seek(TimeSpan position) => await Try(s => s.TryChangePlaybackPositionAsync(position.Ticks).AsTask());

    private void Rebind()
    {
        var next = _manager?.GetCurrentSession();
        if (!ReferenceEquals(next, _session))
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropertiesChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
                _session.TimelinePropertiesChanged -= OnSessionChanged;
            }
            _session = next;
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnPropertiesChanged;
                _session.PlaybackInfoChanged += OnSessionChanged;
                _session.TimelinePropertiesChanged += OnSessionChanged;
            }
        }
        Refresh();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ui.TryEnqueue(Refresh);

    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args) =>
        _ui.TryEnqueue(() => { _propertiesVersion++; Refresh(); });

    private async void Refresh()
    {
        var version = ++_refreshVersion; // a newer refresh may finish first; only the latest wins
        var session = _session;
        NowPlaying? result = null;
        if (session != null)
        {
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                var controls = playback.Controls;
                if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                {
                    result = new NowPlaying(
                        props.Title.Trim(),
                        (string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist)?.Trim() ?? "",
                        session.SourceAppUserModelId,
                        playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        new MediaTimeline(timeline.Position, timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime,
                            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                            playback.PlaybackRate ?? 1),
                        props.Thumbnail,
                        controls.IsPreviousEnabled,
                        controls.IsNextEnabled,
                        controls.IsPlaybackPositionEnabled,
                        _propertiesVersion);
                }
            }
            catch (Exception e)
            {
                Log.Info($"media: refresh failed: {e.Message}"); // session closed mid-read
            }
        }
        if (version != _refreshVersion) return;
        Current = result;
        Changed?.Invoke();
    }

    private async Task Try(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        if (_session == null) return;
        try { await action(_session); }
        catch (Exception e) { Log.Info($"media: command failed: {e.Message}"); }
    }
}
