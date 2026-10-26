using CommunityToolkit.Mvvm.ComponentModel;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows.Display;

namespace Nokto.UI.ViewModels;

/// <summary>Opciones propias de Isla Dinamica. Sin procesos ni servicios duplicados.</summary>
public partial class IslandViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly PersistenceService _persistence;
    private bool _loading;
    public event Action? Changed;

    public IslandViewModel(AppConfig config, PersistenceService persistence)
    {
        _config = config;
        _persistence = persistence;
        _loading = true;
        _enabled = config.Island.Enabled;
        _styleIndex = config.Island.Style switch { "GreenDot" => 1, "BlackDot" => 2, "GlowDot" => 3, _ => 0 };
        _startupModeIndex = !config.Island.StartupModeConfigured ? 0 : config.Island.StartupMode switch
        {
            "Dashboard" => 1, "Island" => 2, "Both" => 3, "Tray" => 4, _ => 1
        };
        _showClock = config.Island.ShowClock;
        _expandOnHover = config.Island.ExpandOnHover;
        _reducedMotion = config.Island.ReducedMotion;
        _animationRateIndex = config.Island.AnimationRateMode switch
        {
            "60" => 1, "120" => 2, "144" => 3, "165" => 4, "240" => 5, _ => 0
        };
        _detectedDisplayHz = DisplayRefreshRateDetector.GetPrimaryRefreshRate();
        _loading = false;
    }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _styleIndex;
    [ObservableProperty] private int _startupModeIndex;
    [ObservableProperty] private bool _showClock;
    [ObservableProperty] private bool _expandOnHover;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private int _animationRateIndex;
    private int _detectedDisplayHz = DisplayRefreshRateDetector.FallbackHz;

    public int DetectedDisplayHz => _detectedDisplayHz;
    public int TargetAnimationFps => AnimationRateIndex switch
    {
        1 => 60, 2 => 120, 3 => 144, 4 => 165, 5 => 240, _ => _detectedDisplayHz
    };
    public string DisplayRefreshStatus => $"{_detectedDisplayHz} Hz  ·  {TargetAnimationFps} FPS";

    public void UpdateDetectedDisplayHz(int hz)
    {
        if (hz == _detectedDisplayHz) return;
        _detectedDisplayHz = hz;
        OnPropertyChanged(nameof(DetectedDisplayHz));
        OnPropertyChanged(nameof(DisplayRefreshStatus));
        OnPropertyChanged(nameof(TargetAnimationFps));
    }

    public string SelectedStyle => StyleIndex switch
    {
        1 => "GreenDot", 2 => "BlackDot", 3 => "GlowDot", _ => "Eyes"
    };

    public string SelectedStartupMode => StartupModeIndex switch
    {
        2 => "Island", 3 => "Both", 4 => "Tray", _ => "Dashboard"
    };

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnStyleIndexChanged(int value) => Save();
    partial void OnStartupModeIndexChanged(int value)
    {
        if (!_loading) _config.Island.StartupModeConfigured = value != 0;
        Save();
    }
    partial void OnShowClockChanged(bool value) => Save();
    partial void OnExpandOnHoverChanged(bool value) => Save();
    partial void OnReducedMotionChanged(bool value) => Save();
    partial void OnAnimationRateIndexChanged(int value)
    {
        OnPropertyChanged(nameof(TargetAnimationFps));
        OnPropertyChanged(nameof(DisplayRefreshStatus));
        Save();
    }

    private void Save()
    {
        if (_loading) return;
        _config.Island.Enabled = Enabled;
        _config.Island.Style = SelectedStyle;
        _config.Island.StartupMode = SelectedStartupMode;
        _config.Island.ShowClock = ShowClock;
        _config.Island.ExpandOnHover = ExpandOnHover;
        _config.Island.ReducedMotion = ReducedMotion;
        _config.Island.AnimationRateMode = AnimationRateIndex switch
        {
            1 => "60", 2 => "120", 3 => "144", 4 => "165", 5 => "240", _ => "Auto"
        };
        _persistence.SaveConfig(_config);
        Changed?.Invoke();
    }
}