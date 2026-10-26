using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

/// <summary>Ventana auxiliar compacta. Renderizado solo durante eventos/actualizacion de reloj.</summary>
public partial class IslandWindow : Window
{
    private IslandViewModel? _settings;
    private DispatcherTimer? _clockTimer;
    private bool _expanded;

    // Constructor para el cargador XAML de Avalonia.
    public IslandWindow() => InitializeComponent();

    public IslandWindow(IslandViewModel settings) : this()
    {
        _settings = settings;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _settings.PropertyChanged += OnSettingsChanged;
        Opened += (_, _) => { ApplySettings(); _clockTimer.Start(); };
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _settings.PropertyChanged -= OnSettingsChanged;
        };
        ApplySettings();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ApplySettings();

    public void ApplySettings()
    {
        if (_settings is null) return;
        EyesVisual.IsVisible = _settings.StyleIndex == 0;
        IndicatorFrame.IsVisible = _settings.StyleIndex != 0;
        IndicatorVisual.Fill = _settings.StyleIndex switch
        {
            1 => Brush.Parse("#00E676"),
            2 => Brush.Parse("#050505"),
            _ => Brush.Parse("#62E8FF")
        };
        ClockText.IsVisible = _settings.ShowClock;
        UpdateClock();
        Reposition();
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm");

    private void Reposition()
    {
        var screen = Screens.Primary;
        if (screen == null) return;
        var region = screen.WorkingArea;
        var scaling = screen.Scaling;
        Position = new PixelPoint(region.X + (int)((region.Width - Width * scaling) / 2),
                                  region.Y);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_expanded) return;
        _expanded = true;
        ExpandedTools.IsVisible = true;
        Height = 102;
        Reposition();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_expanded) return;
        _expanded = false;
        ExpandedTools.IsVisible = false;
        Height = 42;
        Reposition();
    }

    private void OnOpenDashboard(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => App.CurrentInstance?.ShowMainWindow();

    private void OnHideIsland(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settings is not null) _settings.Enabled = false;
    }
}
