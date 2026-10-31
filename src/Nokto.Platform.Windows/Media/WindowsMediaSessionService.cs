using Windows.Media.Control;

namespace Nokto.Platform.Windows.Media;

/// <summary>
/// Instantánea de una sesión multimedia declarada en Windows (GSMTC).
/// Un reproductor que no publique sesión no será visible.
/// </summary>
public sealed record MediaSessionSnapshot(
    bool Available,
    string Title,
    string Artist,
    string AppId,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanPrevious,
    bool CanNext,
    TimeSpan Position,
    TimeSpan Duration,
    byte[]? Artwork)
{
    public bool CanSeek { get; init; }
    public double PlaybackRate { get; init; } = 1;
    public static MediaSessionSnapshot Empty { get; } =
        new(false, "", "", "", false, false, false, false, TimeSpan.Zero, TimeSpan.Zero, null);
}

/// <summary>
/// Observador de Windows Media Control. Sin bucle de polling; se conecta mientras
/// la isla está abierta y el usuario habilita Multimedia.
/// </summary>
public sealed class WindowsMediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _current;
    private CancellationTokenSource? _lifetime;
    private readonly object _lifetimeLock = new();
    private CoalescingMediaRefresh? _refresh;
    private string? _artworkKey;
    private byte[]? _artworkBytes;
    private bool _disposed;
    private TimeSpan _timelineStart;
    public event Action<MediaSessionSnapshot>? Updated;
    public MediaSessionSnapshot Snapshot { get; private set; } = MediaSessionSnapshot.Empty;

    public async Task StartAsync()
    {
        CancellationTokenSource cancel;
        CoalescingMediaRefresh refresh;
        lock (_lifetimeLock)
        {
            if (_disposed || _lifetime is not null) return;
            cancel = new CancellationTokenSource();
            _lifetime = cancel;
            refresh = new CoalescingMediaRefresh(version => ReadSnapshotAsync(version, cancel.Token));
            _refresh = refresh;
        }
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (_lifetimeLock)
            {
                if (!ReferenceEquals(_lifetime, cancel) || cancel.IsCancellationRequested) return;
                _manager = manager;
                manager.CurrentSessionChanged += OnCurrentSessionChanged;
                manager.SessionsChanged += OnSessionsChanged;
                refresh.Request();
            }
        }
        catch (Exception)
        {
            refresh.Request(); // The guarded read publishes an empty state if no manager is available.
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => QueueRead();
    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => QueueRead();
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => QueueRead();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => QueueRead();
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => QueueRead();

    private void QueueRead() => Volatile.Read(ref _refresh)?.Request();

    private async Task ReadSnapshotAsync(long version, CancellationToken token)
    {
        var refresh = Volatile.Read(ref _refresh);
        if (refresh is null) return;
        GlobalSystemMediaTransportControlsSession? current = null;
        string? artworkKey = null;
        byte[]? artwork = null;
        try
        {
            if (!refresh.TryCommit(version, () =>
            {
                current = _manager?.GetCurrentSession();
                if (!ReferenceEquals(current, _current))
                {
                    UnsubscribeSession();
                    _current = current;
                    _artworkKey = null;
                    _artworkBytes = null;
                    if (current is not null)
                    {
                        current.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        current.PlaybackInfoChanged += OnPlaybackChanged;
                        current.TimelinePropertiesChanged += OnTimelineChanged;
                    }
                }
                artworkKey = _artworkKey;
                artwork = _artworkBytes;
            })) return;
            if (current is null)
            {
                refresh.TryCommit(version, () => SetSnapshot(MediaSessionSnapshot.Empty));
                return;
            }
            var props = await current.TryGetMediaPropertiesAsync();
            token.ThrowIfCancellationRequested();
            string coverKey = $"{current.SourceAppUserModelId}|{props.Title}|{props.Artist}";
            if (artworkKey != coverKey) artwork = await TryLoadArtworkAsync(props.Thumbnail, token);
            token.ThrowIfCancellationRequested();
            var playback = current.GetPlaybackInfo();
            var timeline = current.GetTimelineProperties();
            var controls = playback.Controls;
            var duration = timeline.EndTime - timeline.StartTime;
            double playbackRate = playback.PlaybackRate is double rate && double.IsFinite(rate) ? rate : 1;
            var position = timeline.Position - timeline.StartTime;
            var sinceUpdate = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                sinceUpdate >= TimeSpan.Zero && sinceUpdate < TimeSpan.FromHours(1))
                position += TimeSpan.FromSeconds(sinceUpdate.TotalSeconds * playbackRate);
            var snapshot = new MediaSessionSnapshot(
                true, string.IsNullOrWhiteSpace(props.Title) ? "Reproducción multimedia" : props.Title,
                props.Artist ?? "", current.SourceAppUserModelId ?? "",
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled,
                controls.IsPreviousEnabled, controls.IsNextEnabled,
                TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, duration.TotalSeconds))),
                duration > TimeSpan.Zero ? duration : TimeSpan.Zero, artwork)
                { CanSeek = controls.IsPlaybackPositionEnabled, PlaybackRate = playbackRate };
            refresh.TryCommit(version, () =>
            {
                // A session may change before Windows delivers CurrentSessionChanged.
                if (!ReferenceEquals(current, _manager?.GetCurrentSession())) { refresh.Request(); return; }
                _artworkKey = coverKey;
                _artworkBytes = artwork;
                _timelineStart = timeline.StartTime;
                SetSnapshot(snapshot);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            refresh.TryCommit(version, () => SetSnapshot(MediaSessionSnapshot.Empty));
        }
    }

    private static async Task<byte[]?> TryLoadArtworkAsync(
        global::Windows.Storage.Streams.IRandomAccessStreamReference? reference, CancellationToken token)
    {
        if (reference is null || token.IsCancellationRequested) return null;
        try
        {
            using var input = await reference.OpenReadAsync();
            if (input.Size == 0 || input.Size > 1_500_000) return null;
            using var stream = input.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, token);
            return buffer.Length <= 1_500_000 ? buffer.ToArray() : null;
        }
        catch (Exception) { return null; }
    }

    private void SetSnapshot(MediaSessionSnapshot value)
    {
        if (_lifetime?.IsCancellationRequested == true) return;
        if (Snapshot == value) return;
        Snapshot = value;
        Updated?.Invoke(value);
    }

    public async Task<bool> TogglePlayPauseAsync()
    {
        try { return _current is { } s && await s.TryTogglePlayPauseAsync(); }
        catch (Exception) { return false; }
    }
    public async Task<bool> PreviousAsync()
    {
        try { return _current is { } s && await s.TrySkipPreviousAsync(); }
        catch (Exception) { return false; }
    }
    public async Task<bool> NextAsync()
    {
        try { return _current is { } s && await s.TrySkipNextAsync(); }
        catch (Exception) { return false; }
    }

    public async Task<bool> SeekAsync(TimeSpan position)
    {
        var session = _current;
        if (session is null || !Snapshot.CanSeek || Snapshot.Duration <= TimeSpan.Zero) return false;
        position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Snapshot.Duration.Ticks));
        try { return await session.TryChangePlaybackPositionAsync((_timelineStart + position).Ticks); }
        catch (Exception) { return false; }
    }

    private void UnsubscribeSession()
    {
        if (_current is { } old)
        {
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.PlaybackInfoChanged -= OnPlaybackChanged;
            old.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _current = null;
    }

    public void Stop()
    {
        lock (_lifetimeLock)
        {
            var cancel = _lifetime;
            _lifetime = null;
            var refresh = _refresh;
            _refresh = null;
            cancel?.Cancel();
            refresh?.Stop(() =>
            {
                if (_manager is { } manager)
                {
                    manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                    manager.SessionsChanged -= OnSessionsChanged;
                }
                UnsubscribeSession();
                _manager = null;
                _artworkKey = null;
                _artworkBytes = null;
                Snapshot = MediaSessionSnapshot.Empty;
            });
            cancel?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }

}
