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

namespace Nokto.UI.Views;

/// <summary>
/// Compacta en reposo, transiciones bajo demanda y expansion interactiva.
/// El temporizador se adapta al monitor y SOLO funciona durante una transicion.
/// </summary>
public partial class IslandWindow : Window
{
    private double ScaleFactor => _settings?.IslandScale ?? 1.0;
    private double CompactHeight => 48 * ScaleFactor;
    private double ExpandedWidth => 332 * ScaleFactor;
    // La altura expandida crece únicamente si hay una sesión multimedia real.
    private const double TransitionSeconds = 0.26;

    private IslandViewModel? _settings;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _animationTimer;
    private DispatcherTimer? _cursorTimer;
    private DispatcherTimer? _eyeFrameTimer;
    private DispatcherTimer? _blinkTimer;
    private long _gazeStart, _blinkStart;
    private double _gazeX, _gazeY, _gazeFromX, _gazeFromY, _gazeTargetX, _gazeTargetY;
    private int _lastCursorX = int.MinValue, _lastCursorY = int.MinValue;
    private DateTime _lastCursorMovement = DateTime.UtcNow;
    private bool _gazeAnimating, _blinking, _sleeping;
    private bool _expanded;
    private bool _pinned;
    private long _animationStart;
    private double _fromWidth, _fromHeight, _toWidth, _toHeight;
    private bool _initialLayout = true;
    private bool _islandReady;
    private MainViewModel? _controlVm;

    public IslandWindow() => InitializeComponent();

    public IslandWindow(IslandViewModel settings, MainViewModel mainViewModel) : this()
    {
        _settings = settings;
        _controlVm = mainViewModel;
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
        };
        Closed += (_, _) =>
        {
            _islandReady = false;
            _animationTimer.Stop();
            _clockTimer.Stop();
            _cursorTimer?.Stop();
            _eyeFrameTimer?.Stop();
            _blinkTimer?.Stop();
            _demoTimer?.Stop();
            DisposeMedia();
            UnsubscribeProductivity();
            _settings.PropertyChanged -= OnSettingsChanged;
        };
        ApplySettings();
    }

    private double CompactWidth => ScaleFactor * (HasCompactMedia
        ? (_settings?.ClockInCompact == true ? 284 : 232) : HasCompactProductivity
        ? (_settings?.ClockInCompact == true ? 245 : 196)
        : _settings?.ClockInCompact == true
            ? (EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) ? 145 : 176)
            : (EyeStyleCatalog.IsMinimal(_settings?.SelectedStyle) ? 64 : 110));

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        IslandSurface.RenderTransform = new ScaleTransform(ScaleFactor, ScaleFactor);
        IslandSurface.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        EyesVisual.Width = EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) ? 30 : 74;
        TestEyesButton.IsVisible = !_settings.ReducedMotion && !EyeStyleCatalog.IsMinimal(_settings.SelectedStyle);
        ClockText.IsVisible = _expanded ? _settings.ClockInExpanded : _settings.ClockInCompact;
        AudioControls.IsVisible = _settings.ShowAudioControls;
        BatteryStatusText.IsVisible = _settings.ShowBatteryStatus;
        NetworkStatusText.IsVisible = _settings.ShowNetworkStatus;
        SystemStatus.IsVisible = _settings.ShowBatteryStatus || _settings.ShowNetworkStatus;
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
        if (_settings?.ClockModeIndex != 2 && IsVisible) _clockTimer.Start();
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
        Position = new PixelPoint(bounds.X + (int)Math.Round((bounds.Width - Width * scale) / 2), bounds.Y);
    }

    private bool CursorPhysicallyInside()
    {
        if (!CursorPositionReader.TryGet(out int x, out int y)) return false;
        double scale = Screens.Primary?.Scaling ?? 1.0;
        return x >= Position.X && x < Position.X + Width * scale &&
               y >= Position.Y && y < Position.Y + Height * scale;
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        // El compositor puede emitir Enter mientras se reajusta el tamaño de
        // ventanas sin marco. Solo es hover real si el cursor está dentro.
        if (_settings?.ExpandOnHover == true && CursorPhysicallyInside()) SetExpanded(true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_pinned && !CursorPhysicallyInside()) SetExpanded(false);
    }

    private void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // No interferir con los botones de la zona expandida.
        if (e.GetPosition(this).Y > CompactHeight) return;
        _pinned = !_pinned;
        SetExpanded(_pinned);
        e.Handled = true;
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (_settings is not null)
            ClockText.IsVisible = expanded ? _settings.ClockInExpanded : _settings.ClockInCompact;
        if (expanded)
        {
            ExpandedTools.IsVisible = true;
        }
        CompactTrackText.IsVisible = HasCompactMedia && !expanded;
        RefreshProductivity();
        SyncMediaProgressTimer();
        AnimateTo(expanded ? ExpandedWidth : CompactWidth,
                  expanded ? ExpandedHeightForContent : CompactHeight);
    }

    private void AnimateTo(double width, double height)
    {
        if (_animationTimer is null) return;
        UpdateAnimationRate();
        if (_settings?.ReducedMotion == true)
        {
            _animationTimer.Stop();
            SetIslandDimensions(width, height);
            ExpandedTools.Opacity = _expanded ? 1 : 0;
            ExpandedTools.IsVisible = _expanded;
            Reposition();
            return;
        }

        // Interrumpir una transicion conserva la geometria actual, sin saltos.
        _fromWidth = Width;
        _fromHeight = Height;
        _toWidth = width;
        _toHeight = height;
        if (Math.Abs(width - Width) < 0.5 && Math.Abs(height - Height) < 0.5)
        {
            FinishAnimation();
            return;
        }
        _animationStart = Stopwatch.GetTimestamp();
        _animationTimer.Start();
    }

    private void AdvanceAnimation()
    {
        double t = Math.Clamp(Stopwatch.GetElapsedTime(_animationStart).TotalSeconds / TransitionSeconds, 0, 1);
        // ease-out cubic: respuesta rapida al inicio, frenado suave al terminar.
        double ease = 1 - Math.Pow(1 - t, 3);
        SetIslandDimensions(_fromWidth + (_toWidth - _fromWidth) * ease,
            _fromHeight + (_toHeight - _fromHeight) * ease);
        // La opacidad depende del tamano real, asi tampoco salta al invertir a mitad.
        double reveal = (Height - CompactHeight) / Math.Max(1, ExpandedHeightForContent - CompactHeight);
        ExpandedTools.Opacity = Math.Clamp((reveal - 0.12) / 0.58, 0, 1);
        Reposition();
        if (t >= 1) FinishAnimation();
    }

    private void FinishAnimation()
    {
        _animationTimer?.Stop();
        SetIslandDimensions(_toWidth, _toHeight);
        ExpandedTools.Opacity = _expanded ? 1 : 0;
        ExpandedTools.IsVisible = _expanded;
        Reposition();
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
