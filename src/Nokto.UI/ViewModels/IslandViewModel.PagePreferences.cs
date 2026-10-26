using CommunityToolkit.Mvvm.ComponentModel;

namespace Nokto.UI.ViewModels;

/// <summary>Opciones independientes por página, con persistencia compatible.</summary>
public partial class IslandViewModel
{
    [ObservableProperty] private bool _showOverviewPage = true;
    [ObservableProperty] private bool _showSoundPage = true;
    [ObservableProperty] private bool _showShortcutsPage = true;
    [ObservableProperty] private bool _showMainButton = true;
    [ObservableProperty] private bool _showHideButton = true;
    [ObservableProperty] private bool _soundShowVolume = true;
    [ObservableProperty] private bool _soundShowMicrophone = true;
    [ObservableProperty] private bool _soundShowOutputMute = true;
    [ObservableProperty] private bool _shortcutsShowRoutines = true;
    [ObservableProperty] private bool _shortcutsShowExplorer = true;
    [ObservableProperty] private bool _shortcutsShowFolders = true;
    [ObservableProperty] private bool _showWeather;
    [ObservableProperty] private string _weatherCity = "";
    [ObservableProperty] private bool _showWindowsNotifications;
    [ObservableProperty] private bool _privacyMode;

    public IReadOnlyList<int> AvailablePages
    {
        get
        {
            var pages = new List<int>(3);
            if (ShowOverviewPage) pages.Add(0);
            if (ShowSoundPage) pages.Add(1);
            if (ShowShortcutsPage) pages.Add(2);
            return pages.Count == 0 ? [1] : pages;
        }
    }

    private void InitializePagePreferences()
    {
        var p = _config.Island;
        ShowOverviewPage = p.ShowOverviewPage;
        ShowSoundPage = p.ShowSoundPage;
        ShowShortcutsPage = p.ShowShortcutsPage;
        ShowMainButton = p.ShowMainButton;
        ShowHideButton = p.ShowHideButton;
        SoundShowVolume = p.SoundShowVolume;
        SoundShowMicrophone = p.SoundShowMicrophone;
        SoundShowOutputMute = p.SoundShowOutputMute;
        ShortcutsShowRoutines = p.ShortcutsShowRoutines;
        ShortcutsShowExplorer = p.ShortcutsShowExplorer;
        ShortcutsShowFolders = p.ShortcutsShowFolders;
        ShowWeather = p.ShowWeather;
        WeatherCity = p.WeatherCity ?? "";
        ShowWindowsNotifications = p.ShowWindowsNotifications;
        PrivacyMode = p.PrivacyMode;
    }

    private void SavePagePreferences()
    {
        var p = _config.Island;
        p.ShowOverviewPage = ShowOverviewPage;
        p.ShowSoundPage = ShowSoundPage;
        p.ShowShortcutsPage = ShowShortcutsPage;
        p.ShowMainButton = ShowMainButton;
        p.ShowHideButton = ShowHideButton;
        p.SoundShowVolume = SoundShowVolume;
        p.SoundShowMicrophone = SoundShowMicrophone;
        p.SoundShowOutputMute = SoundShowOutputMute;
        p.ShortcutsShowRoutines = ShortcutsShowRoutines;
        p.ShortcutsShowExplorer = ShortcutsShowExplorer;
        p.ShortcutsShowFolders = ShortcutsShowFolders;
        p.ShowWeather = ShowWeather;
        p.WeatherCity = WeatherCity?.Trim().Length > 80 ? WeatherCity.Trim()[..80] : WeatherCity?.Trim() ?? "";
        p.ShowWindowsNotifications = ShowWindowsNotifications;
        p.PrivacyMode = PrivacyMode;
    }
    partial void OnShowOverviewPageChanged(bool value) => Save();
    partial void OnShowSoundPageChanged(bool value) => Save();
    partial void OnShowShortcutsPageChanged(bool value) => Save();
    partial void OnShowMainButtonChanged(bool value) => Save();
    partial void OnShowHideButtonChanged(bool value) => Save();
    partial void OnSoundShowVolumeChanged(bool value) => Save();
    partial void OnSoundShowMicrophoneChanged(bool value) => Save();
    partial void OnSoundShowOutputMuteChanged(bool value) => Save();
    partial void OnShortcutsShowRoutinesChanged(bool value) => Save();
    partial void OnShortcutsShowExplorerChanged(bool value) => Save();
    partial void OnShortcutsShowFoldersChanged(bool value) => Save();
    partial void OnShowWeatherChanged(bool value) => Save();
    partial void OnWeatherCityChanged(string value) => Save();
    partial void OnShowWindowsNotificationsChanged(bool value) => Save();
    partial void OnPrivacyModeChanged(bool value) => Save();
}
