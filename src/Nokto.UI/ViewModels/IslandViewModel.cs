using CommunityToolkit.Mvvm.ComponentModel;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows.Display;

namespace Nokto.UI.ViewModels;

public sealed record EyeStyleOption(string Id, string DisplayName);

/// <summary>Opciones propias de Isla Dinamica. Sin procesos ni servicios duplicados.</summary>
public partial class IslandViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly PersistenceService _persistence;
    private bool _loading;
    public event Action? Changed;
    public IslandProductivityViewModel Productivity { get; }

    public IslandViewModel(AppConfig config, PersistenceService persistence)
    {
        _config = config;
        _persistence = persistence;
        Productivity = new IslandProductivityViewModel(config, persistence);
        _loading = true;
        _enabled = config.Island.Enabled;
        _styleIndex = EyeStyleCatalog.IndexOf(config.Island.Style);
        _eyeStyleOptions = BuildOptions(config.Settings.Language);
        _startupModeIndex = !config.Island.StartupModeConfigured ? 0 : config.Island.StartupMode switch
        {
            "Dashboard" => 1, "Island" => 2, "Both" => 3, "Tray" => 4, _ => 1
        };
        _showClock = config.Island.ShowClock;
        _clockModeIndex = config.Island.ClockVisibility switch
        {
            "Always" => 1, "Never" => 2, "Expand" => 0, _ => config.Island.ShowClock ? 0 : 2
        };
        _showAudioControls = config.Island.ShowAudioControls;
        _showBatteryStatus = config.Island.ShowBatteryStatus;
        _showNetworkStatus = config.Island.ShowNetworkStatus;
        _showMediaControls = config.Island.ShowMediaControls;
        _showMediaInCompact = config.Island.ShowMediaInCompact;
        _expandOnHover = config.Island.ExpandOnHover;
        _reducedMotion = config.Island.ReducedMotion;
        _followCursor = config.Island.FollowCursor;
        _naturalBlink = config.Island.NaturalBlink;
        _sleepOnIdle = config.Island.SleepOnIdle;
        _eyeSensitivity = Math.Clamp(config.Island.EyeSensitivity, 0, 1);
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
    [ObservableProperty] private int _clockModeIndex;
    [ObservableProperty] private bool _showAudioControls;
    [ObservableProperty] private bool _showBatteryStatus;
    [ObservableProperty] private bool _showNetworkStatus;
    [ObservableProperty] private bool _showMediaControls;
    [ObservableProperty] private bool _showMediaInCompact;
    public bool ClockInCompact => ClockModeIndex == 1;
    public bool ClockInExpanded => ClockModeIndex != 2;
    [ObservableProperty] private bool _expandOnHover;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _followCursor;
    [ObservableProperty] private bool _naturalBlink;
    [ObservableProperty] private bool _sleepOnIdle;
    [ObservableProperty] private double _eyeSensitivity;
    private IReadOnlyList<EyeStyleOption> _eyeStyleOptions = [];
    public IReadOnlyList<EyeStyleOption> EyeStyleOptions => _eyeStyleOptions;
    private static IReadOnlyList<EyeStyleOption> BuildOptions(string language) =>
        EyeStyleCatalog.All.Select(s => new EyeStyleOption(s.Id,
            $"{s.Pack} · {(language == "en" ? s.En : s.Es)}")).ToArray();
    public void UpdateLanguage(string language)
    {
        int selected = StyleIndex;
        _loading = true;
        _eyeStyleOptions = BuildOptions(language);
        OnPropertyChanged(nameof(EyeStyleOptions));
        StyleIndex = selected;
        _loading = false;
    }
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

    public string SelectedStyle => StyleIndex < 0
        ? EyeStyleCatalog.Find(_config.Island.Style).Id
        : EyeStyleCatalog.All[Math.Clamp(StyleIndex, 0, EyeStyleCatalog.All.Count - 1)].Id;

    public string SelectedStartupMode => StartupModeIndex switch
    {
        2 => "Island", 3 => "Both", 4 => "Tray", _ => "Dashboard"
    };

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnStyleIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedStyle));
        if (value >= 0) Save();
    }
    partial void OnStartupModeIndexChanged(int value)
    {
        if (!_loading) _config.Island.StartupModeConfigured = value != 0;
        Save();
    }
    partial void OnShowClockChanged(bool value) => Save();
    partial void OnClockModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ClockInCompact));
        OnPropertyChanged(nameof(ClockInExpanded));
        Save();
    }
    partial void OnShowAudioControlsChanged(bool value) => Save();
    partial void OnShowBatteryStatusChanged(bool value) => Save();
    partial void OnShowNetworkStatusChanged(bool value) => Save();
    partial void OnShowMediaControlsChanged(bool value) => Save();
    partial void OnShowMediaInCompactChanged(bool value) => Save();
    partial void OnExpandOnHoverChanged(bool value) => Save();
    partial void OnReducedMotionChanged(bool value) => Save();
    partial void OnFollowCursorChanged(bool value) => Save();
    partial void OnNaturalBlinkChanged(bool value) => Save();
    partial void OnSleepOnIdleChanged(bool value) => Save();
    partial void OnEyeSensitivityChanged(double value) => Save();
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
        _config.Island.ShowClock = ClockModeIndex != 2;
        _config.Island.ClockVisibility = ClockModeIndex switch { 1 => "Always", 2 => "Never", _ => "Expand" };
        _config.Island.ShowAudioControls = ShowAudioControls;
        _config.Island.ShowBatteryStatus = ShowBatteryStatus;
        _config.Island.ShowNetworkStatus = ShowNetworkStatus;
        _config.Island.ShowMediaControls = ShowMediaControls;
        _config.Island.ShowMediaInCompact = ShowMediaInCompact;
        _config.Island.ExpandOnHover = ExpandOnHover;
        _config.Island.ReducedMotion = ReducedMotion;
        _config.Island.FollowCursor = FollowCursor;
        _config.Island.NaturalBlink = NaturalBlink;
        _config.Island.SleepOnIdle = SleepOnIdle;
        _config.Island.EyeSensitivity = Math.Clamp(EyeSensitivity, 0, 1);
        _config.Island.AnimationRateMode = AnimationRateIndex switch
        {
            1 => "60", 2 => "120", 3 => "144", 4 => "165", 5 => "240", _ => "Auto"
        };
        _persistence.SaveConfig(_config);
        Changed?.Invoke();
    }
}