using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;

namespace Nokto.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly PersistenceService _persistence;
    private readonly AppConfig _config;

    [ObservableProperty]
    private string _currentVersionText;

    [ObservableProperty]
    private string _updateStatusBadgeText = "Comprobar";

    [ObservableProperty]
    private string _updateStatusBadgeColor = "#888888";

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressText = "";

    [ObservableProperty]
    private string _latestVersionText = "";

    [ObservableProperty]
    private string _releaseNotesText = "";

    [ObservableProperty]
    private string _releaseHtmlUrl = "";

    [ObservableProperty]
    private UpdateReleaseInfo? _latestRelease;

    public bool CheckUpdatesOnStartup
    {
        get => _config.Settings.CheckUpdatesOnStartup;
        set
        {
            if (_config.Settings.CheckUpdatesOnStartup == value) return;
            _config.Settings.CheckUpdatesOnStartup = value;
            _persistence.SaveConfig(_config);
            OnPropertyChanged();
        }
    }

    public SettingsViewModel(
        IUpdateService? updateService = null,
        PersistenceService? persistence = null,
        AppConfig? config = null)
    {
        _persistence = persistence ?? new PersistenceService();
        _config = config ?? _persistence.LoadConfig();
        _updateService = updateService ?? new UpdateService(_persistence.Storage);

        _currentVersionText = $"v{_updateService.CurrentVersion.ToString(3)}";
        _updateStatusBadgeText = "Al día";
        _updateStatusBadgeColor = "#4EBA6F";

        if (CheckUpdatesOnStartup)
        {
            _ = CheckForUpdatesAsync();
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates || IsDownloadingUpdate) return;

        IsCheckingUpdates = true;
        UpdateStatusBadgeText = "Comprobando...";
        UpdateStatusBadgeColor = "#E5A93C";

        try
        {
            var release = await _updateService.CheckForUpdatesAsync(
                _config.Settings.UpdateRepositoryOwner,
                _config.Settings.UpdateRepositoryName).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (release == null)
                {
                    UpdateStatusBadgeText = "Al día";
                    UpdateStatusBadgeColor = "#4EBA6F";
                    HasUpdateAvailable = false;
                    return;
                }

                LatestRelease = release;
                LatestVersionText = release.TagName;
                ReleaseNotesText = string.IsNullOrWhiteSpace(release.Body) ? "Sin notas de versión disponibles." : release.Body;
                ReleaseHtmlUrl = release.HtmlUrl;

                if (release.IsUpdateAvailable)
                {
                    HasUpdateAvailable = true;
                    UpdateStatusBadgeText = $"Nueva versión disponible: {release.TagName}";
                    UpdateStatusBadgeColor = "#4EBA6F";
                }
                else
                {
                    HasUpdateAvailable = false;
                    UpdateStatusBadgeText = "Al día";
                    UpdateStatusBadgeColor = "#4EBA6F";
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStatusBadgeText = "Error al comprobar";
                UpdateStatusBadgeColor = "#E05353";
                Debug.WriteLine($"[SettingsViewModel] Error checking updates: {ex.Message}");
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsCheckingUpdates = false;
            });
        }
    }

    [RelayCommand]
    public async Task DownloadAndInstallUpdateAsync()
    {
        if (LatestRelease == null || IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        DownloadProgressText = "Iniciando descarga...";
        UpdateStatusBadgeText = "Descargando...";
        UpdateStatusBadgeColor = "#3B82F6";

        try
        {
            var progress = new Progress<double>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgress = p * 100;
                    DownloadProgressText = $"{DownloadProgress:F0}%";
                });
            });

            string downloadedFile = await _updateService.DownloadUpdateAsync(LatestRelease, progress).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DownloadProgressText = "Aplicando actualización...";
                UpdateStatusBadgeText = "Reiniciando...";
                _updateService.ApplyUpdate(LatestRelease, downloadedFile);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DownloadProgressText = $"Error: {ex.Message}";
                UpdateStatusBadgeText = "Fallo en descarga";
                UpdateStatusBadgeColor = "#E05353";
                IsDownloadingUpdate = false;
            });
        }
    }

    [RelayCommand]
    public void OpenLinkedIn()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.linkedin.com/in/jorge-morgado/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsViewModel] Error opening LinkedIn: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/nokto/nokto",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsViewModel] Error opening GitHub: {ex.Message}");
        }
    }
}

