using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Nokto.UI.ViewModels;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Display;
using Nokto.Platform.Windows.Input;
using Nokto.UI.Controls;

namespace Nokto.UI.Views;

/// <summary>
/// Compacta en reposo, transiciones bajo demanda y expansion interactiva.
/// El temporizador se adapta al monitor y SOLO funciona durante una transicion.
/// </summary>
public partial class IslandWindow : Window
{
    private double ScaleFactor => _settings?.IslandScale ?? 1.0;
    private double CompactHeight => (EyeStyleCatalog.HasIndicator(_settings?.SelectedStyle) ? 48 : 32) * ScaleFactor;
    private double ExpandedWidth
    {
        get
        {
            if (!_noticeOnly) ArrangeContentSections();
            return (_noticeOnly ? 320 : _contentWidth + 24) * ScaleFactor;
        }
    }
    // La altura expandida crece únicamente si hay una sesión multimedia real.

    private IslandViewModel? _settings;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _animationTimer;
    private DispatcherTimer? _cursorTimer;
    private DispatcherTimer? _eyeFrameTimer;
    private DispatcherTimer? _blinkTimer;
    private DispatcherTimer? _outsideTimer;
    private long _gazeStart, _blinkStart;
    private double _gazeX, _gazeY, _gazeFromX, _gazeFromY, _gazeTargetX, _gazeTargetY;
    private double _gazeRightX, _gazeRightY, _gazeRightFromX, _gazeRightFromY, _gazeRightTargetX, _gazeRightTargetY;
    private int _lastCursorX = int.MinValue, _lastCursorY = int.MinValue;
    private DateTime _lastCursorMovement = DateTime.UtcNow;
    private bool _gazeAnimating, _blinking, _sleeping;
    private bool _expanded;
    private bool _pinned;
    private long _animationStart;
    private double _fromWidth, _fromHeight, _toWidth, _toHeight;
    private double _widthVelocity, _heightVelocity, _fromWidthVelocity, _fromHeightVelocity;
    private double _reveal, _fromReveal, _toReveal, _revealVelocity, _fromRevealVelocity;
    private long _pointerLeaveStarted;
    private readonly SolidColorBrush _surfaceFill = new(Color.Parse("#10151E"), 0);
    private readonly SolidColorBrush _surfaceStroke = new(Color.Parse("#202B34"), 0);
    private readonly TranslateTransform _contentTranslation = new();
    private double _cachedExpandedHeight = 300;
    private bool _initialLayout = true;
    private bool _islandReady;
    private MainViewModel? _controlVm;

    public IslandWindow()
    {
        InitializeComponent();
        InitializeContentSections();
        IslandSurface.Background = _surfaceFill;
        IslandSurface.BorderBrush = _surfaceStroke;
        ExpandedTools.RenderTransform = _contentTranslation;
        ExpandedTools.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        IslandSurface.AddHandler(InputElement.PointerPressedEvent, OnAudioInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        IslandSurface.AddHandler(InputElement.PointerMovedEvent, OnAudioInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        IslandSurface.AddHandler(InputElement.KeyDownEvent, OnAudioInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        IslandSurface.AddHandler(InputElement.PointerWheelChangedEvent, OnWheelGuard,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) => ExcludeFromAltTab();
    }

    public IslandWindow(IslandViewModel settings, MainViewModel mainViewModel) : this()
    {
        _settings = settings;
        _pageIndex = settings.AvailablePages[0];
        _controlVm = mainViewModel;
        _controlVm.PropertyChanged += OnHeaderAudioChanged;
        DataContext = mainViewModel;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / settings.TargetAnimationFps) };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _cursorTimer.Tick += (_, _) => SampleCursor();
        _eyeFrameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / Math.Min(settings.TargetAnimationFps, 120)) };
        _eyeFrameTimer.Tick += (_, _) => AdvanceEyeFrame();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _blinkTimer.Tick += (_, _) => StartBlink();
        _outsideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _outsideTimer.Tick += (_, _) => CollapseIfPointerOutside();
        _settings.PropertyChanged += OnSettingsChanged;
        Opened += (_, _) =>
        {
            _islandReady = true;
            ApplySettings();
            UpdateAnimationRate();
            SyncClockTimer();
            ConfigureEyeMotion();
            SyncMediaSession();
            SubscribeProductivity();
            RefreshShelf();
            SyncSystemObserver();
        };
        Closed += (_, _) =>
        {
            _islandReady = false;
            _animationTimer.Stop();
            _clockTimer.Stop();
            _cursorTimer?.Stop();
            _eyeFrameTimer?.Stop();
            _blinkTimer?.Stop();
            _outsideTimer?.Stop();
            _demoTimer?.Stop();
            DisposeMedia();
            DisposeCodexQuota();
            DisposeWeather();
            DisposeSystemNotices();
            UnsubscribeProductivity();
            _settings.PropertyChanged -= OnSettingsChanged;
            _controlVm.PropertyChanged -= OnHeaderAudioChanged;
        };
        ApplySettings();
    }

    private bool HasCustomCompactHeader => _settings is { } settings &&
        (settings.HeaderLeftMode is not ("Auto" or "None") || settings.HeaderRightMode is not ("Auto" or "None"));
    private double CompactWidth => Math.Max(BaseCompactWidth, HasCustomCompactHeader ? 320 * ScaleFactor : 0);
    private double BaseCompactWidth => ScaleFactor * (HasCompactMedia
        ? (_settings?.ClockInCompact == true ? 400 : 350) : HasCompactProductivity
        ? (_settings?.ClockInCompact == true ? 320 : 270)
        : _settings?.ClockInCompact == true
            ? (EyeStyleCatalog.HasIndicator(_settings.SelectedStyle) ?
                  (EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) ? 170 : _settings.EyeCount == 1 ? 186 : 218) : 105)
            : (EyeStyleCatalog.HasIndicator(_settings?.SelectedStyle) ?
                  (EyeStyleCatalog.IsMinimal(_settings?.SelectedStyle) ? 64 : _settings?.EyeCount == 1 ? 78 : 110) : 64));

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IslandViewModel.ShowRecognitionButton) && _settings?.ShowRecognitionButton == false) CloseRecognition();
        if (e.PropertyName == nameof(IslandViewModel.PrivacyMode) && _settings?.PrivacyMode == true)
        {
            CloseLyrics(); FinishLyricsClose(); OnNoticeExpired(null, EventArgs.Empty);
        }
        if (e.PropertyName is nameof(IslandViewModel.NoticePreferencesVersion))
        {
            SyncSystemObserver(); return;
        }
        if (e.PropertyName is nameof(IslandViewModel.EyeCount) or nameof(IslandViewModel.StyleIndex))
            _lastCursorX = int.MinValue;
        if (_settings?.ReducedMotion == true || EyeStyleCatalog.IsMinimal(_settings?.SelectedStyle))
            EyesVisual.StopActivation();
        if (e.PropertyName is nameof(IslandViewModel.WeatherUseLocation) or nameof(IslandViewModel.WeatherCity) or
            nameof(IslandViewModel.ShowWeather) or nameof(IslandViewModel.PrivacyMode))
        {
            _weatherRequest?.Cancel();
            _weatherCityLast = "";
        }
        if (e.PropertyName == nameof(IslandViewModel.Enabled) ||
            e.PropertyName == nameof(IslandViewModel.DetectedDisplayHz) ||
            e.PropertyName == nameof(IslandViewModel.TargetAnimationFps) ||
            e.PropertyName == nameof(IslandViewModel.DisplayRefreshStatus))
            return;
        if (e.PropertyName == nameof(IslandViewModel.AnimationRateIndex))
        {
            UpdateAnimationRate();
            return;
        }
        ApplySettings();
        // Al cambiar de punto minimalista a ojos (o viceversa) hay que iniciar o detener
        // el motor, no basta con dibujar el nuevo estilo.
        ConfigureEyeMotion();
        SyncMediaSession();
    }

    public void ApplySettings()
    {
        if (_settings is null) return;
        EyesVisual.StyleId = _settings.SelectedStyle;
        EyesVisual.StaticDotColor = _settings.StaticDotColor;
        EyesVisual.EyeCount = _settings.EyeCount;
        EyesVisual.IsVisible = EyeStyleCatalog.HasIndicator(_settings.SelectedStyle);
        IslandHeader.MinHeight = EyeStyleCatalog.HasIndicator(_settings.SelectedStyle) ? 34 : 18;
        IslandSurface.RenderTransform = new ScaleTransform(ScaleFactor, ScaleFactor);
        IslandSurface.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        EyesVisual.Width = EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) ? 30 : _settings.EyeCount == 1 ? 42 : 74;

        TestEyesButton.IsVisible = !_settings.ReducedMotion && !EyeStyleCatalog.IsMinimal(_settings.SelectedStyle);
        ClockText.IsVisible = _expanded ? _settings.ClockInExpanded : _settings.ClockInCompact;
        AudioControls.IsVisible = true;
        BatteryStatusText.IsVisible = _settings.ShowBatteryStatus;
        NetworkStatusText.IsVisible = _settings.ShowNetworkStatus;
        SystemStatus.IsVisible = !_settings.PrivacyMode && (_settings.ShowBatteryStatus || _settings.ShowNetworkStatus);
        RefreshPages();
        RefreshProductivity();
        RefreshShelf();
        UpdateClock();
        SyncClockTimer();
        UpdateAnimationRate();
        RefreshMediaPresentation(false);

        if (_initialLayout)
        {
            _initialLayout = false;
            SetIslandDimensions(CompactWidth, CompactHeight);
            _toWidth = Width;
            _toHeight = Height;
            Reposition();
        }
        else
        {
            AnimateTo(_expanded ? ExpandedWidth : CompactWidth,
                _expanded ? ExpandedHeightForContent : CompactHeight);
        }
    }

    private void SyncClockTimer()
    {
        if (_clockTimer is null) return;
        if ((ClockText.IsVisible || CodexQuotaWanted) && IsVisible) _clockTimer.Start();
        else _clockTimer.Stop();
    }

    private void UpdateAnimationRate()
    {
        if (_settings is null || _animationTimer is null) return;
        // Consultar al iniciar/cambiar opciones y antes de animar, nunca en cada fotograma.
        var scale = Screens.Primary?.Scaling ?? 1.0;
        int x = Position.X + (int)Math.Round(Width * scale / 2);
        int y = Position.Y + 12;
        int detected = DisplayRefreshRateDetector.GetRefreshRateAt(x, y);
        _settings.UpdateDetectedDisplayHz(detected);
        _animationTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(_settings.TargetAnimationFps, 30, 240));
        if (_eyeFrameTimer is not null)
            _eyeFrameTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(_settings.TargetAnimationFps, 30, 240));
    }

    private void UpdateClock()
    {
        RenderCodexQuota(); UpdateContextHeader();
        if (_islandReady) _ = RefreshCodexQuota();
        if (_settings?.ClockModeIndex == 2) return;
        string time = DateTime.Now.ToString("HH:mm");
        if (ClockText.Text != time) ClockText.Text = time;
    }

    private void SetIslandDimensions(double width, double height)
    {
        Width = width;
        Height = height;
        IslandSurface.Width = width / ScaleFactor;
        IslandSurface.Height = height / ScaleFactor;
    }

    private void Reposition()
    {
        // La posicion del centro es estable: el ancho crece por ambos lados.
        var screen = Screens.Primary;
        if (screen == null) return;
        var bounds = screen.WorkingArea;
        double scale = screen.Scaling;
        Position = new PixelPoint(bounds.X + (int)Math.Round((bounds.Width - Width * scale) / 2),
            bounds.Y);
        PositionLyrics();
    }

    private bool CursorPhysicallyInside()
    {
        if (!CursorPositionReader.TryGet(out int x, out int y)) return false;
        return IsPointInside(x, y);
    }

    private bool IsPointInside(int x, int y)
    {
        double scale = Screens.Primary?.Scaling ?? 1.0;
        return (x >= Position.X && x < Position.X + Width * scale &&
               y >= Position.Y && y < Position.Y + Height * scale) || PointerInsideLyrics(x, y);
    }

    private void CollapseIfPointerOutside()
    {
        if (_noticeOnly && _noticeTimer?.IsEnabled == true) return;
        if (!_expanded || !CursorPositionReader.TryGet(out int x, out int y)) return;
        if (IsPointInside(x, y)) { _pointerLeaveStarted = 0; return; }
        if (_pointerLeaveStarted == 0) _pointerLeaveStarted = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_pointerLeaveStarted).TotalMilliseconds >= 180)
        {
            _pinned = false;
            SetExpanded(false);
        }
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _pointerLeaveStarted = 0;
        if (_noticeOnly && CursorPhysicallyInside())
        {
            PromoteNoticeToMenu();
        }
        // El compositor puede emitir Enter mientras se reajusta el tamaño de
        // ventanas sin marco. Solo es hover real si el cursor está dentro.
        if (_settings?.ExpandOnHover == true && CursorPhysicallyInside()) SetExpanded(true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        CollapseIfPointerOutside();
    }

    private void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // No interferir con los botones de la zona expandida.
        if (e.GetPosition(this).Y > CompactHeight) return;
        if (_noticeOnly) { PromoteNoticeToMenu(); e.Handled = true; return; }
        var eyePoint = e.GetPosition(EyesVisual);
        if (_settings?.SelectedStyle != "None" &&
            eyePoint.X >= 0 && eyePoint.X < EyesVisual.Bounds.Width &&
            eyePoint.Y >= 0 && eyePoint.Y < EyesVisual.Bounds.Height)
        {
            if (!_expanded) SetExpanded(true);
            else if (_settings?.ReducedMotion == false) EyesVisual.PlayActivation();
            e.Handled = true;
            return;
        }
        _pinned = !_pinned;
        SetExpanded(_pinned);
        e.Handled = true;
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        if (!expanded && !_noticeOnly) _menuFeedbackUntil = DateTimeOffset.UtcNow.AddMilliseconds(650);
        _expanded = expanded;
        UpdateContextHeader();
        _pointerLeaveStarted = 0;

        if (expanded) _outsideTimer?.Start(); else _outsideTimer?.Stop();
        if (expanded)
        {
            ExpandedTools.IsVisible = !_noticeOnly;
            if (!_noticeOnly && _settings?.ReducedMotion == false) EyesVisual.PlayActivation();
        }
        CompactTrackText.IsVisible = HasCompactMedia && !expanded && !HasCustomCompactHeader;
        if (!expanded) { ClearClipboardPreview(); CloseLyrics(); CloseRecognition(); }
        RefreshPages();
        RefreshProductivity();
        SyncMediaProgressTimer();
        AnimateTo(expanded ? ExpandedWidth : CompactWidth,
                  expanded ? ExpandedHeightForContent : CompactHeight);
        SyncClockTimer();
    }

    private void AnimateTo(double width, double height)
    {
        if (_animationTimer is null) return;
        double targetReveal = _expanded ? 1 : 0;
        if (_animationTimer.IsEnabled && Math.Abs(width - _toWidth) < .5 && Math.Abs(height - _toHeight) < .5 && _toReveal == targetReveal) return;
        UpdateAnimationRate();
        if (_settings?.ReducedMotion == true)
        {
            _animationTimer.Stop();
            SetIslandDimensions(width, height);
            _reveal = targetReveal;
            _widthVelocity = _heightVelocity = _revealVelocity = 0;
            ApplyTransitionPresentation(_reveal);
            ExpandedTools.IsVisible = _expanded && !_noticeOnly;
            if (!_expanded) { HudPanel.IsVisible = false; _noticeOnly = false; CloseLyrics(); }
            Reposition();
            return;
        }

        // Interrumpir una transicion conserva la geometria actual, sin saltos.
        if (_animationTimer.IsEnabled) AdvanceAnimation(false);
        _fromWidth = Width;
        _fromHeight = Height;
        _fromWidthVelocity = _widthVelocity;
        _fromHeightVelocity = _heightVelocity;
        _fromReveal = _reveal;
        _fromRevealVelocity = _revealVelocity;
        _toReveal = targetReveal;
        _toWidth = width;
        _toHeight = height;
        if (Math.Abs(width - Width) < 0.5 && Math.Abs(height - Height) < 0.5 && Math.Abs(_reveal - targetReveal) < .001)
        {
            FinishAnimation();
            return;
        }
        _animationStart = Stopwatch.GetTimestamp();
        _animationTimer.Start();
    }

    private void AdvanceAnimation(bool finish = true)
    {
        // Transición física común a todas las apariencias.
        {
            double elapsed = Stopwatch.GetElapsedTime(_animationStart).TotalSeconds;
            var w = IslandSpring.Sample(_fromWidth, _toWidth, _fromWidthVelocity, elapsed);
            var h = IslandSpring.Sample(_fromHeight, _toHeight, _fromHeightVelocity, elapsed);
            var reveal = IslandSpring.Sample(_fromReveal, _toReveal, _fromRevealVelocity, elapsed);
            _widthVelocity = w.Velocity;
            _heightVelocity = h.Velocity;
            _reveal = reveal.Position;
            _revealVelocity = reveal.Velocity;
            SetIslandDimensions(w.Position, h.Position);
            ApplyTransitionPresentation(_reveal);
            Reposition();
            if (finish && (elapsed >= 1 || (Math.Abs(w.Position - _toWidth) < .1 && Math.Abs(h.Position - _toHeight) < .1 &&
                Math.Abs(w.Velocity) < 1 && Math.Abs(h.Velocity) < 1 && Math.Abs(_reveal - _toReveal) < .001))) FinishAnimation();
            return;
        }
    }

    private void FinishAnimation()
    {
        _widthVelocity = _heightVelocity = _revealVelocity = 0;
        _reveal = _toReveal;
        _animationTimer?.Stop();
        SetIslandDimensions(_toWidth, _toHeight);
        ApplyTransitionPresentation(_reveal);
        ExpandedTools.IsVisible = _expanded && !_noticeOnly;
        if (!_expanded) { HudPanel.IsVisible = false; _noticeOnly = false; CloseLyrics(); }
        Reposition();
    }

    private static double SmoothReveal(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }

    private void ApplyTransitionPresentation(double progress)
    {
        double reveal = Math.Clamp(progress, 0, 1);
        _surfaceFill.Opacity = SmoothReveal(reveal / .65);
        _surfaceStroke.Opacity = SmoothReveal(reveal / .8);
        double content = SmoothReveal((reveal - .12) / .76);
        ExpandedTools.Opacity = _noticeOnly ? 0 : content;
        HudPanel.Opacity = content;
        _contentTranslation.Y = (1 - content) * 8;
        IslandSurface.CornerRadius = new CornerRadius(24 + 8 * SmoothReveal(reveal));
        if (_settings is not null)
        {
            bool explicitClock = _settings.HeaderRightMode == "Clock";
            ClockText.IsVisible = _settings.HeaderRightMode != "None" &&
                (explicitClock || _settings.HeaderRightMode == "Auto" &&
                    (_settings.ClockInCompact && !_expanded || _settings.ClockInExpanded && _headerRightUsesClock && reveal > 0));
            HeaderLeftText.Opacity = _settings.HeaderLeftMode is not ("Auto" or "None") ? 1 : SmoothReveal(reveal);
            HeaderRightText.Opacity = _settings.HeaderRightMode is not ("Auto" or "None") ? 1 : SmoothReveal(reveal);
            ClockText.Opacity = explicitClock || _settings.ClockInCompact && _settings.ClockInExpanded ? 1 :
                _settings.ClockInExpanded ? SmoothReveal(reveal) : 1 - SmoothReveal(reveal);
        }
    }

    private void OnOpenDashboard(object? sender, RoutedEventArgs e)
        => App.CurrentInstance?.ShowMainWindow();

    private void OnVolumeDown(object? sender, RoutedEventArgs e)
    {
        if (_controlVm is not null) _controlVm.MasterVolumePercent = Math.Clamp(_controlVm.MasterVolumePercent - 5, 0, 100);
    }

    private void OnVolumeUp(object? sender, RoutedEventArgs e)
    {
        if (_controlVm is not null) _controlVm.MasterVolumePercent = Math.Clamp(_controlVm.MasterVolumePercent + 5, 0, 100);
    }

    private void OnHideIsland(object? sender, RoutedEventArgs e)
    {
        if (_settings is not null) _settings.Enabled = false;
    }
}
