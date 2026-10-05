using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Services;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel
{
    private SettingsViewModel? _settingsVm;
    public SettingsViewModel SettingsVm => _settingsVm ??= InitializeSettingsVm();

    private SettingsViewModel InitializeSettingsVm()
    {
        var vm = new SettingsViewModel(new UpdateService(_persistence.Storage), _persistence, _config);
        vm.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                OnPropertyChanged(e.PropertyName);
        };
        return vm;
    }

    public string UpdateStatusBadgeText => SettingsVm.UpdateStatusBadgeText;
    public string UpdateStatusBadgeColor => SettingsVm.UpdateStatusBadgeColor;
    public bool IsCheckingUpdates => SettingsVm.IsCheckingUpdates;
    public bool HasUpdateAvailable => SettingsVm.HasUpdateAvailable;
    public bool IsDownloadingUpdate => SettingsVm.IsDownloadingUpdate;
    public string LatestVersionText => SettingsVm.LatestVersionText;
    public string ReleaseNotesText => SettingsVm.ReleaseNotesText;
    public double DownloadProgress => SettingsVm.DownloadProgress;
    public string DownloadProgressText => SettingsVm.DownloadProgressText;

    public bool CheckUpdatesOnStartup
    {
        get => SettingsVm.CheckUpdatesOnStartup;
        set => SettingsVm.CheckUpdatesOnStartup = value;
    }

    public IAsyncRelayCommand CheckForUpdatesCommand => SettingsVm.CheckForUpdatesCommand;
    public IAsyncRelayCommand DownloadAndInstallUpdateCommand => SettingsVm.DownloadAndInstallUpdateCommand;
    public IRelayCommand OpenLinkedInCommand => SettingsVm.OpenLinkedInCommand;
    public IRelayCommand OpenGitHubCommand => SettingsVm.OpenGitHubCommand;
}

