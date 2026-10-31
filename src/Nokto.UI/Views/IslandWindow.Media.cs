using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Controls;
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

    private double ExpandedHeightForContent
    {
        get
        {
            if (_noticeOnly) return 142 * ScaleFactor;
            ArrangeContentSections();
            _cachedExpandedHeight = ScaleFactor * ((IslandPageNav.IsVisible ? 118 : 78) + (IslandFooter.IsVisible ? 34 : 0) + _contentHeight + (HudPanel.IsVisible ? 84 : 0));
            return _cachedExpandedHeight;
        }
    }

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
        bool enabledHere = _settings?.ShowMediaControls == true && _settings.PageHasFeature("media", _pageIndex);
        bool show = enabledHere && _mediaSnapshot.Available;
        MediaPanel.IsVisible = show;
        MediaEmptyState.IsVisible = enabledHere && !show && _settings?.PrivacyMode != true;
        CompactTrackText.IsVisible = HasCompactMedia && !_expanded && !HasCustomCompactHeader;
        CompactTrackText.Text = _settings?.PrivacyMode == true ? "" : _mediaSnapshot.Title;
        MediaTitle.Text = _settings?.PrivacyMode == true ? "Reproducción activa" : _mediaSnapshot.Title;
        MediaArtist.Text = _settings?.PrivacyMode == true ? "" : _mediaSnapshot.Artist;
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
        MediaCoverFrame.IsVisible = show && _mediaArtwork is not null && _settings?.PrivacyMode != true;
        MediaPlayIcon.IsVisible = !_mediaSnapshot.IsPlaying;
        MediaPauseIcon.IsVisible = _mediaSnapshot.IsPlaying;
        string action = _mediaSnapshot.IsPlaying
            ? PageLabel("IslandMediaPauseLabel", "Pausar") : PageLabel("IslandMediaPlayLabel", "Reproducir");
        MediaPlayLabel.Text = action;
        ToolTip.SetTip(MediaPlayPauseButton, action);
        AutomationProperties.SetName(MediaPlayPauseButton, action);
        MediaStateText.Text = _mediaSnapshot.IsPlaying
            ? PageLabel("IslandMediaPlayingState", "REPRODUCIENDO") : PageLabel("IslandMediaPausedState", "EN PAUSA");
        MediaPlayPauseButton.IsEnabled = _mediaSnapshot.CanPlayPause;
        MediaPreviousButton.IsEnabled = _mediaSnapshot.CanPrevious;
        MediaNextButton.IsEnabled = _mediaSnapshot.CanNext;
        LyricsButton.IsEnabled = show && _settings?.EnableOnlineLyrics == true && _settings.PrivacyMode != true;
        bool lyrics = enabledHere && _settings?.ShowLyricsButton == true;
        bool recognition = enabledHere && _settings?.ShowRecognitionButton == true;
        LyricsButton.IsVisible = lyrics;
        RecognitionButton.IsVisible = recognition;
        MediaToolsRow.ColumnDefinitions[0].Width = new GridLength(lyrics ? 1 : 0, lyrics ? GridUnitType.Star : GridUnitType.Pixel);
        MediaToolsRow.ColumnDefinitions[1].Width = new GridLength(recognition ? 1 : 0, recognition ? GridUnitType.Star : GridUnitType.Pixel);
        LyricsButton.Margin = new Avalonia.Thickness(0, 0, recognition ? 4 : 0, 0);
        RecognitionButton.Margin = new Avalonia.Thickness(lyrics ? 4 : 0, 0, 0, 0);
        MediaToolsRow.IsVisible = lyrics || recognition;
        RefreshMediaProgress();
        SyncMediaProgressTimer();

        if (animate && _animationTimer is not null)
            AnimateTo(_expanded ? ExpandedWidth : CompactWidth,
                      _expanded ? ExpandedHeightForContent : CompactHeight);
    }

    private void RefreshMediaProgress()
    {
        RefreshLyrics();
        if (_mediaSnapshot.Duration <= TimeSpan.Zero)
        {
            MediaProgress.Value = 0;
            MediaTime.Text = "--:--";
            MediaDuration.Text = "--:--";
            return;
        }
        var elapsed = CurrentPlaybackPosition();
        MediaProgress.Value = 100 * elapsed.TotalMilliseconds / _mediaSnapshot.Duration.TotalMilliseconds;
        MediaTime.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        MediaDuration.Text = $"{(int)_mediaSnapshot.Duration.TotalMinutes:00}:{_mediaSnapshot.Duration.Seconds:00}";
    }

    private void SyncMediaProgressTimer()
    {
        if (_mediaProgressTimer is null)
        {
            _mediaProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mediaProgressTimer.Tick += (_, _) => RefreshMediaProgress();
        }
        _mediaProgressTimer.Interval = TimeSpan.FromMilliseconds(_lyricsOpen ? 250 : 1000);
        // Ningún temporizador cuando está compacta o pausada.
        if (_islandReady && _expanded && (MediaPanel.IsVisible || _lyricsOpen) && _mediaSnapshot.IsPlaying)
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
        CloseLyrics();
        FinishLyricsClose();
        _lyricsService?.Dispose(); _lyricsService = null;
        CloseRecognition();
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
