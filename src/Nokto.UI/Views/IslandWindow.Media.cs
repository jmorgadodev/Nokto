using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nokto.Platform.Windows.Media;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private WindowsMediaSessionService? _mediaService;
    private MediaSessionSnapshot _mediaSnapshot = MediaSessionSnapshot.Empty;
    private DispatcherTimer? _mediaProgressTimer;
    private DateTimeOffset _mediaUpdatedAt;
    private byte[]? _mediaArtworkSource;
    private Bitmap? _mediaArtwork;

    private bool HasCompactMedia => _settings?.ShowMediaInCompact == true &&
        _mediaSnapshot.Available && _mediaSnapshot.IsPlaying;

    private double ExpandedHeightForContent => ScaleFactor * (_pageIndex switch
    {
        0 => 143 + (MediaPanel.IsVisible ? 112 : 0) + (SystemStatus.IsVisible ? 31 : 0),
        1 => 263,
        _ => 176 + (ProductivityPanel.IsVisible ? 36 : 0) +
            (QuickActionsPanel.IsVisible ? 158 : 0) +
            (FileShelfPanel.IsVisible ? 40 : 0) +
            (ShelfDetails.IsVisible ? 190 : 0) +
            (ClipboardPanel.IsVisible ? 56 : 0) +
            (ProductivityAlertFrame.IsVisible ? 42 : 0)
    });

    private void SyncMediaSession()
    {
        bool enabled = _islandReady && _settings is not null &&
            (_settings.ShowMediaControls || _settings.ShowMediaInCompact);
        if (enabled)
        {
            if (_mediaService is null)
            {
                _mediaService = new WindowsMediaSessionService();
                _mediaService.Updated += OnMediaUpdated;
                _ = _mediaService.StartAsync();
            }
        }
        else if (_mediaService is not null)
        {
            _mediaService.Updated -= OnMediaUpdated;
            _mediaService.Dispose();
            _mediaService = null;
            _mediaSnapshot = MediaSessionSnapshot.Empty;
            RefreshMediaPresentation(false);
        }
    }

    private void OnMediaUpdated(MediaSessionSnapshot snapshot)
    {
        // WinRT puede llamar desde cualquier hilo; Avalonia solo se modifica en UI.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_islandReady || _mediaService is null) return;
            _mediaSnapshot = snapshot;
            _mediaUpdatedAt = DateTimeOffset.UtcNow;
            RefreshMediaPresentation(true);
        });
    }

    private void RefreshMediaPresentation(bool animate)
    {
        bool show = _settings?.ShowMediaControls == true && _mediaSnapshot.Available;
        MediaPanel.IsVisible = show;
        CompactTrackText.IsVisible = HasCompactMedia && !_expanded;
        CompactTrackText.Text = _mediaSnapshot.Title;
        MediaTitle.Text = _mediaSnapshot.Title;
        MediaArtist.Text = _mediaSnapshot.Artist;
        if (!ReferenceEquals(_mediaArtworkSource, _mediaSnapshot.Artwork))
        {
            MediaCover.Source = null;
            _mediaArtwork?.Dispose();
            _mediaArtwork = null;
            _mediaArtworkSource = _mediaSnapshot.Artwork;
            if (_mediaArtworkSource is { Length: > 0 } bytes)
            {
                try
                {
                    using var stream = new MemoryStream(bytes, false);
                    _mediaArtwork = new Bitmap(stream);
                    MediaCover.Source = _mediaArtwork;
                }
                catch (Exception) { _mediaArtwork = null; }
            }
        }
        MediaCoverFrame.IsVisible = show && _mediaArtwork is not null;
        MediaPlayPauseButton.Content = _mediaSnapshot.IsPlaying ? "⏸" : "▶";
        MediaPlayPauseButton.IsEnabled = _mediaSnapshot.CanPlayPause;
        MediaPreviousButton.IsEnabled = _mediaSnapshot.CanPrevious;
        MediaNextButton.IsEnabled = _mediaSnapshot.CanNext;
        RefreshMediaProgress();
        SyncMediaProgressTimer();

        if (animate && _animationTimer is not null)
            AnimateTo(_expanded ? ExpandedWidth : CompactWidth,
                      _expanded ? ExpandedHeightForContent : CompactHeight);
    }

    private void RefreshMediaProgress()
    {
        if (_mediaSnapshot.Duration <= TimeSpan.Zero)
        {
            MediaProgress.Value = 0;
            MediaTime.Text = "—";
            return;
        }
        var elapsed = _mediaSnapshot.Position;
        if (_mediaSnapshot.IsPlaying)
            elapsed += DateTimeOffset.UtcNow - _mediaUpdatedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed > _mediaSnapshot.Duration) elapsed = _mediaSnapshot.Duration;
        MediaProgress.Value = 100 * elapsed.TotalMilliseconds / _mediaSnapshot.Duration.TotalMilliseconds;
        MediaTime.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void SyncMediaProgressTimer()
    {
        if (_mediaProgressTimer is null)
        {
            _mediaProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mediaProgressTimer.Tick += (_, _) => RefreshMediaProgress();
        }
        // Ningún temporizador cuando está compacta o pausada.
        if (_islandReady && _expanded && MediaPanel.IsVisible && _mediaSnapshot.IsPlaying)
            _mediaProgressTimer.Start();
        else
            _mediaProgressTimer.Stop();
    }

    private async void OnMediaToggle(object? sender, RoutedEventArgs e)
    {
        if (_mediaService is not null) await _mediaService.TogglePlayPauseAsync();
    }
    private async void OnMediaPrevious(object? sender, RoutedEventArgs e)
    {
        if (_mediaService is not null) await _mediaService.PreviousAsync();
    }
    private async void OnMediaNext(object? sender, RoutedEventArgs e)
    {
        if (_mediaService is not null) await _mediaService.NextAsync();
    }

    private void DisposeMedia()
    {
        _mediaProgressTimer?.Stop();
        _mediaProgressTimer = null;
        MediaCover.Source = null;
        _mediaArtwork?.Dispose();
        _mediaArtwork = null;
        _mediaArtworkSource = null;
        if (_mediaService is { } service)
        {
            service.Updated -= OnMediaUpdated;
            service.Dispose();
            _mediaService = null;
        }
    }
}
