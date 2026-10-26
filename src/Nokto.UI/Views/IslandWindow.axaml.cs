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
    private const double CompactHeight = 42;
    private const double ExpandedWidth = 270;
    private const double ExpandedHeight = 114;
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

    public IslandWindow() => InitializeComponent();

    public IslandWindow(IslandViewModel settings) : this()
    {
        _settings = settings;
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
            ApplySettings();
            UpdateAnimationRate();
            SyncClockTimer();
            ConfigureEyeMotion();
        };
        Closed += (_, _) =>
        {
            _animationTimer.Stop();
            _clockTimer.Stop();
            _cursorTimer?.Stop();
            _eyeFrameTimer?.Stop();
            _blinkTimer?.Stop();
            _settings.PropertyChanged -= OnSettingsChanged;
        };
        ApplySettings();
    }

    private double CompactWidth => _settings?.ShowClock == true
        ? (EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) ? 128 : 162)
        : (EyeStyleCatalog.IsMinimal(_settings?.SelectedStyle) ? 58 : 90);

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
    }

    public void ApplySettings()
    {
        if (_settings is null) return;
        EyesVisual.StyleId = _settings.SelectedStyle;
        EyesVisual.Width = EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) ? 26 : 56;
        ClockText.IsVisible = _settings.ShowClock;
        UpdateClock();
        SyncClockTimer();
        UpdateAnimationRate();

        if (_initialLayout)
        {
            _initialLayout = false;
            Width = CompactWidth;
            Height = CompactHeight;
            _toWidth = Width;
            _toHeight = Height;
            Reposition();
        }
        else
        {
            AnimateTo(_expanded ? ExpandedWidth : CompactWidth,
                _expanded ? ExpandedHeight : CompactHeight);
        }
    }

    private void SyncClockTimer()
    {
        if (_clockTimer is null) return;
        if (_settings?.ShowClock == true && IsVisible) _clockTimer.Start();
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
        if (_settings?.ShowClock != true) return;
        string time = DateTime.Now.ToString("HH:mm");
        if (ClockText.Text != time) ClockText.Text = time;
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

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_settings?.ExpandOnHover == true) SetExpanded(true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_pinned) SetExpanded(false);
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
        if (expanded)
        {
            ExpandedTools.IsVisible = true;
        }
        AnimateTo(expanded ? ExpandedWidth : CompactWidth,
                  expanded ? ExpandedHeight : CompactHeight);
    }

    private void AnimateTo(double width, double height)
    {
        if (_animationTimer is null) return;
        UpdateAnimationRate();
        if (_settings?.ReducedMotion == true)
        {
            _animationTimer.Stop();
            Width = width;
            Height = height;
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
        Width = _fromWidth + (_toWidth - _fromWidth) * ease;
        Height = _fromHeight + (_toHeight - _fromHeight) * ease;
        // La opacidad depende del tamano real, asi tampoco salta al invertir a mitad.
        double reveal = (Height - CompactHeight) / (ExpandedHeight - CompactHeight);
        ExpandedTools.Opacity = Math.Clamp((reveal - 0.12) / 0.58, 0, 1);
        Reposition();
        if (t >= 1) FinishAnimation();
    }

    private void FinishAnimation()
    {
        _animationTimer?.Stop();
        Width = _toWidth;
        Height = _toHeight;
        ExpandedTools.Opacity = _expanded ? 1 : 0;
        ExpandedTools.IsVisible = _expanded;
        Reposition();
    }

    private void OnOpenDashboard(object? sender, RoutedEventArgs e)
        => App.CurrentInstance?.ShowMainWindow();

    private void OnHideIsland(object? sender, RoutedEventArgs e)
    {
        if (_settings is not null) _settings.Enabled = false;
    }
}
