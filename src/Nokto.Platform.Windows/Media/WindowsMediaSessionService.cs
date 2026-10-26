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
    private int _refreshPending;
    private string? _artworkKey;
    private byte[]? _artworkBytes;
    private bool _disposed;
    public event Action<MediaSessionSnapshot>? Updated;
    public MediaSessionSnapshot Snapshot { get; private set; } = MediaSessionSnapshot.Empty;

    public async Task StartAsync()
    {
        if (_disposed || _lifetime is not null) return;
        var cancel = new CancellationTokenSource();
        _lifetime = cancel;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (cancel.IsCancellationRequested) return;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            await ReloadCurrentAsync(cancel.Token);
        }
        catch (Exception)
        {
            SetSnapshot(MediaSessionSnapshot.Empty);
            // Puede no estar disponible en esta edición de Windows o sesión remota.
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => _ = RefreshCurrentSafeAsync();
    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => _ = RefreshCurrentSafeAsync();
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => QueueRead();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => QueueRead();
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => QueueRead();

    private async Task RefreshCurrentSafeAsync()
    {
        try
        {
            var cancel = _lifetime;
            if (cancel is not null) await ReloadCurrentAsync(cancel.Token);
        }
        catch (Exception) { SetSnapshot(MediaSessionSnapshot.Empty); }
    }

    private async Task ReloadCurrentAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested || _manager is null) return;
        var current = _manager.GetCurrentSession();
        if (ReferenceEquals(current, _current))
        {
            QueueRead();
            return;
        }
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
        await ReadSnapshotAsync(token);
    }

    private void QueueRead()
    {
        if (_lifetime is null || _lifetime.IsCancellationRequested) return;
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;
        _ = ReadQueuedSafeAsync();
    }

    private async Task ReadQueuedSafeAsync()
    {
        try
        {
            var token = _lifetime?.Token;
            if (token.HasValue) await ReadSnapshotAsync(token.Value);
        }
        catch (Exception) { SetSnapshot(MediaSessionSnapshot.Empty); }
        finally { Interlocked.Exchange(ref _refreshPending, 0); }
    }

    private async Task ReadSnapshotAsync(CancellationToken token)
    {
        var current = _current;
        if (token.IsCancellationRequested) return;
        if (current is null)
        {
            SetSnapshot(MediaSessionSnapshot.Empty);
            return;
        }
        var props = await current.TryGetMediaPropertiesAsync();
        if (token.IsCancellationRequested || !ReferenceEquals(current, _current)) return;
        string coverKey = $"{current.SourceAppUserModelId}|{props.Title}|{props.Artist}";
        if (_artworkKey != coverKey)
        {
            _artworkKey = coverKey;
            _artworkBytes = await TryLoadArtworkAsync(props.Thumbnail, token);
        }
        var playback = current.GetPlaybackInfo();
        var timeline = current.GetTimelineProperties();
        var controls = playback.Controls;
        var duration = timeline.EndTime - timeline.StartTime;
        SetSnapshot(new MediaSessionSnapshot(
            true,
            string.IsNullOrWhiteSpace(props.Title) ? "Reproducción multimedia" : props.Title,
            props.Artist ?? "",
            current.SourceAppUserModelId ?? "",
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled,
            controls.IsPreviousEnabled,
            controls.IsNextEnabled,
            timeline.Position,
            duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
            _artworkBytes));
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
        var cancel = _lifetime;
        _lifetime = null;
        cancel?.Cancel();
        if (_manager is { } manager)
        {
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            manager.SessionsChanged -= OnSessionsChanged;
        }
        UnsubscribeSession();
        _manager = null;
        Snapshot = MediaSessionSnapshot.Empty;
        cancel?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
