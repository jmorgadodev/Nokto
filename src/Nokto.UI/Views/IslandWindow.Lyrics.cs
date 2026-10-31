using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nokto.UI.Controls;
using Nokto.Core.Services;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private IslandLyricsWindow? _lyricsWindow;
    private IslandLyricsService? _lyricsService;
    private CancellationTokenSource? _lyricsRequest;
    private string _lyricsTrackKey = "";
    private bool _lyricsOpen;
    private DispatcherTimer? _lyricsMotion;
    private long _lyricsMotionStart;
    private double _lyricsReveal, _lyricsFrom, _lyricsTarget, _lyricsVelocity, _lyricsFromVelocity;
    private SongRecognitionView? _recognitionView;

    private void OnOpenRecognition(object? sender, RoutedEventArgs args)
    {
        if (_settings?.ShowRecognitionButton != true) return;
        if (_recognitionView is not null) return;
        CloseLyrics(); FinishLyricsClose();
        _recognitionView = new SongRecognitionView();
        _recognitionView.DismissRequested += CloseRecognition;
        _recognitionView.ContentChanged += OnRecognitionContentChanged;
        ArrangeContentSections();
        if (!_expanded) SetExpanded(true);
        AnimateTo(ExpandedWidth, ExpandedHeightForContent);
    }

    private void OnRecognitionContentChanged()
    {
        // La altura cambia con proveedor, resultado o error; medir después de actualizar el panel.
        Dispatcher.UIThread.Post(() =>
        {
            if (_recognitionView is null || !_expanded) return;
            ArrangeContentSections(); AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        }, DispatcherPriority.Loaded);
    }

    private void CloseRecognition()
    {
        if (_recognitionView is not { } panel) return;
        _recognitionView = null;
        panel.DismissRequested -= CloseRecognition; panel.ContentChanged -= OnRecognitionContentChanged;
        panel.Dispose();
        ArrangeContentSections();
        if (_expanded) AnimateTo(ExpandedWidth, ExpandedHeightForContent);
    }

    private async void OnToggleLyrics(object? sender, RoutedEventArgs args)
    {
        if (_lyricsOpen) { CloseLyrics(); return; }
        if (!_mediaSnapshot.Available) return;
        if (_settings?.ShowLyricsButton != true || _settings.EnableOnlineLyrics != true || _settings.PrivacyMode) return;
        FinishLyricsClose();
        _lyricsOpen = true;
        _lyricsWindow = new IslandLyricsWindow(_settings.ReducedMotion) { Opacity = 0 };
        _lyricsWindow.DismissRequested += CloseLyrics;
        _lyricsWindow.SeekRequested += OnLyricSeek;
        _lyricsWindow.RetryRequested += OnRetryLyrics;
        _lyricsWindow.Show(this);
        ArrangeContentSections();
        AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        AnimateLyrics(1);
        PositionLyrics();
        SyncMediaProgressTimer();
        await LoadLyricsAsync();
    }
    private string CurrentLyricsKey => $"{_mediaSnapshot.AppId}|{_mediaSnapshot.Title}|{_mediaSnapshot.Artist}|{_mediaSnapshot.Duration.TotalSeconds:0}";
    private async void OnRetryLyrics() => await LoadLyricsAsync();
    private async Task LoadLyricsAsync()
    {
        var pane = _lyricsWindow;
        if (!_lyricsOpen || pane is null) return;
        _lyricsRequest?.Cancel(); _lyricsRequest?.Dispose();
        var request = new CancellationTokenSource(); _lyricsRequest = request;
        var cancellation = request.Token;
        _lyricsTrackKey = CurrentLyricsKey;
        bool IsCurrent() => !cancellation.IsCancellationRequested && _lyricsOpen && ReferenceEquals(pane, _lyricsWindow) && ReferenceEquals(request, _lyricsRequest);
        pane.Loading(_mediaSnapshot.Title);
        try
        {
            _lyricsService ??= new IslandLyricsService();
            var lyrics = await _lyricsService.GetAsync(_mediaSnapshot.Title, _mediaSnapshot.Artist, _mediaSnapshot.Duration, cancellation);
            if (!IsCurrent()) return;
            pane.SetLyrics(lyrics, _mediaSnapshot.CanSeek);
            pane.UpdatePosition(CurrentPlaybackPosition());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrent()) pane.Error(LyricsErrorMessage(ex)); }
    }

    private static string LyricsErrorMessage(Exception error) => error switch
    {
        OperationCanceledException => "LRCLIB tardó demasiado. Puedes reintentar.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "LRCLIB ha limitado las consultas. Espera antes de reintentar.",
        HttpRequestException { StatusCode: { } code } when (int)code >= 500 => "LRCLIB no está disponible temporalmente. Reintenta en unos instantes.",
        HttpRequestException { StatusCode: null } => "No se pudo conectar con LRCLIB. Comprueba tu conexión y reintenta.",
        HttpRequestException => "LRCLIB rechazó la consulta de esta pista. Reintenta más tarde.",
        JsonException or InvalidDataException => "LRCLIB devolvió una respuesta no válida. Reintenta más tarde.",
        _ => "No se pudo cargar la letra. Puedes reintentar."
    };
    private async void OnLyricSeek(TimeSpan position)
    {
        if (_mediaService is null) return;
        string track = CurrentLyricsKey;
        bool accepted = await _mediaService.SeekAsync(position);
        if (track != CurrentLyricsKey || !_lyricsOpen) return;
        if (accepted)
        {
            _mediaSnapshot = _mediaSnapshot with { Position = position };
            _mediaUpdatedAt = DateTimeOffset.UtcNow;
            RefreshMediaProgress();
        }
        else _lyricsWindow?.PlaybackNotice("El reproductor no aceptó el cambio de posición.");
    }
    private TimeSpan CurrentPlaybackPosition()
    {
        double elapsed = _mediaSnapshot.IsPlaying ? (DateTimeOffset.UtcNow - _mediaUpdatedAt).TotalSeconds * _mediaSnapshot.PlaybackRate : 0;
        return TimeSpan.FromSeconds(Math.Clamp(_mediaSnapshot.Position.TotalSeconds + elapsed, 0, Math.Max(0, _mediaSnapshot.Duration.TotalSeconds)));
    }
    private void RefreshLyrics()
    {
        if (!_lyricsOpen) return;
        if (!_mediaSnapshot.Available || _settings?.PrivacyMode == true || _settings?.EnableOnlineLyrics != true || _settings?.ShowLyricsButton != true)
        { CloseLyrics(); FinishLyricsClose(); return; }
        if (_lyricsTrackKey != CurrentLyricsKey) _ = LoadLyricsAsync();
        else _lyricsWindow?.UpdatePosition(CurrentPlaybackPosition(), _mediaSnapshot.CanSeek);
    }
    private void PositionLyrics()
    {
        if (_lyricsWindow is not { IsVisible: true } pane) return;
        var screen = Screens.Primary; if (screen is null) return;
        double scale = screen.Scaling;
        double available = screen.WorkingArea.Width / scale;
        pane.Width = Math.Min(340 * ScaleFactor, Math.Max(180, available - Width - 32));
        double reveal = Math.Clamp(_lyricsReveal, 0, 1);
        pane.Opacity = reveal;
        double total = Width + pane.Width + 12;
        if (total <= available - 16)
        {
            int left = screen.WorkingArea.X + (int)Math.Round(((available - Width) / 2 - (pane.Width + 12) * reveal / 2) * scale);
            Position = new PixelPoint(left, screen.WorkingArea.Y);
            pane.Position = new PixelPoint(left + (int)Math.Round((Width + 12 - 10 * (1-reveal)) * scale), screen.WorkingArea.Y + 12);
        }
        else pane.Position = new PixelPoint(Position.X, Position.Y + (int)Math.Round((Height + 12) * scale));
    }
    private bool PointerInsideLyrics(int x, int y)
    {
        if (_lyricsWindow is not { IsVisible: true } pane) return false;
        double scale = Screens.Primary?.Scaling ?? 1;
        return x >= pane.Position.X && x < pane.Position.X + pane.Width * scale && y >= pane.Position.Y && y < pane.Position.Y + pane.Height * scale;
    }
    private void CloseLyrics()
    {
        if (!_lyricsOpen) return;
        _lyricsOpen = false; _lyricsTrackKey = "";
        _lyricsRequest?.Cancel(); _lyricsRequest?.Dispose(); _lyricsRequest = null;
        ArrangeContentSections();
        if (_expanded) AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        AnimateLyrics(0); SyncMediaProgressTimer();
    }
    private void AnimateLyrics(double target)
    {
        _lyricsMotion ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _lyricsMotion.Tick -= OnLyricsMotion; _lyricsMotion.Tick += OnLyricsMotion;
        _lyricsFrom = _lyricsReveal; _lyricsFromVelocity = _lyricsVelocity;
        _lyricsTarget = target; _lyricsMotionStart = Stopwatch.GetTimestamp();
        if (_settings?.ReducedMotion == true)
        {
            _lyricsReveal = target; _lyricsVelocity = 0;
            if (target == 0) FinishLyricsClose(); else Reposition();
        }
        else _lyricsMotion.Start();
    }
    private void OnLyricsMotion(object? sender, EventArgs args)
    {
        double elapsed = Stopwatch.GetElapsedTime(_lyricsMotionStart).TotalSeconds;
        var sample = IslandSpring.Sample(_lyricsFrom, _lyricsTarget, _lyricsFromVelocity, elapsed);
        _lyricsReveal = sample.Position; _lyricsVelocity = sample.Velocity;
        Reposition();
        if (elapsed < .7) return;
        _lyricsMotion?.Stop(); _lyricsReveal = _lyricsTarget; _lyricsVelocity = 0;
        if (_lyricsTarget == 0) FinishLyricsClose(); else Reposition();
    }
    private void FinishLyricsClose()
    {
        _lyricsMotion?.Stop(); _lyricsReveal = _lyricsVelocity = 0;
        var pane = _lyricsWindow; _lyricsWindow = null; pane?.Close(); Reposition();
    }
}
