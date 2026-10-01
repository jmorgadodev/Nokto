using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.UI.Tray;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISystemAdapter _systemAdapter;
    private readonly CancellationTokenSource _disposalCts = new();

    private CancellationTokenSource? _keepAliveCts;
    private CancellationTokenSource? _activeTaskCts;
    private PeriodicTimer? _metricsTimer;
    private PeriodicTimer? _countdownTimer;

    private DateTimeOffset _taskEndTime = DateTimeOffset.MinValue;
    private TimeSpan _totalTaskDuration = TimeSpan.Zero;
    private bool _isAudioFadeStarted;
    private bool _isDisplayPowerOffSent;
    private float _pulsePhase = 0f;

    public event Action<TrayIconVisualState, double, int, float>? RequestTrayIconUpdate;
    public event Action<bool, int>? RequestGraceOverlay;
    public event Action? RequestQrModal;

    [ObservableProperty]
    private bool _isKeepAliveActive;

    [ObservableProperty]
    private string _keepAliveButtonText = "Iniciar Modo";

    [ObservableProperty]
    private bool _isTaskRunning;

    [ObservableProperty]
    private string _currentStatusText = "Listo";

    [ObservableProperty]
    private string _activeTaskTitle = "Ninguna tarea en curso";

    [ObservableProperty]
    private string _timeRemainingText = "--:--:--";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private int _secondsRemaining;

    [ObservableProperty]
    private string _cpuText = "0.0%";

    [ObservableProperty]
    private string _ramText = "0 / 0 MB";

    [ObservableProperty]
    private string _netText = "0.0 / 0.0 KB/s";

    [ObservableProperty]
    private bool _isStudioMode;

    [ObservableProperty]
    private double _windowWidth = 460;

    [ObservableProperty]
    private double _windowHeight = 580;

    [ObservableProperty]
    private string _selectedProcessName = "blender.exe";

    [ObservableProperty]
    private int _quickMinutesInput = 30;

    public ObservableCollection<string> AvailableProcesses { get; } = [];
    public ObservableCollection<PresetDefinition> Presets { get; } = [];

    public MainViewModel(ISystemAdapter systemAdapter)
    {
        _systemAdapter = systemAdapter;

        LoadInitialPresets();
        RefreshAvailableProcesses();
        StartMetricsMonitoring();
    }

    private void LoadInitialPresets()
    {
        Presets.Add(new PresetDefinition
        {
            Id = "preset_blender",
            Name = "Render Nocturno Blender",
            Description = "Supervisa blender.exe, toma captura y apaga con 60s de gracia.",
            IsFavorite = true,
            Icon = "Movie"
        });

        Presets.Add(new PresetDefinition
        {
            Id = "preset_workday",
            Name = "Jornada Laboral Anti-Ausente",
            Description = "Mantiene activo Teams y bloquea la estación a las 18:00.",
            IsFavorite = true,
            Icon = "Sun"
        });

        Presets.Add(new PresetDefinition
        {
            Id = "preset_download",
            Name = "Descarga de Medios 4K",
            Description = "Espera a que la red caiga bajo 50 KB/s durante 2 minutos y suspende el equipo.",
            IsFavorite = false,
            Icon = "Download"
        });
    }

    public void RefreshAvailableProcesses()
    {
        try
        {
            AvailableProcesses.Clear();
            var processes = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero)
                .Select(p => p.ProcessName + ".exe")
                .Distinct()
                .OrderBy(name => name)
                .Take(20);

            foreach (var proc in processes)
            {
                AvailableProcesses.Add(proc);
            }

            if (!AvailableProcesses.Contains(SelectedProcessName) && AvailableProcesses.Count > 0)
            {
                SelectedProcessName = AvailableProcesses[0];
            }
        }
        catch
        {
            // Fallback
            if (AvailableProcesses.Count == 0)
            {
                AvailableProcesses.Add("blender.exe");
                AvailableProcesses.Add("handbrake.exe");
                AvailableProcesses.Add("qbittorrent.exe");
            }
        }
    }

    private void StartMetricsMonitoring()
    {
        Task.Run(async () =>
        {
            _metricsTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (!_disposalCts.IsCancellationRequested)
            {
                try
                {
                    await _metricsTimer.WaitForNextTickAsync(_disposalCts.Token);
                    var metrics = _systemAdapter.GetCurrentMetrics();

                    CpuText = $"{metrics.CpuUsagePercentage:F1}%";
                    RamText = $"{metrics.RamUsedMb:F0} / {metrics.RamTotalMb:F0} MB";
                    NetText = $"{metrics.NetworkDownKBs:F1} / {metrics.NetworkUpKBs:F1} KB/s";

                    // Actualizar tray icon pulse
                    _pulsePhase = (float)(Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * Math.PI) * 0.5 + 0.5);

                    if (IsTaskRunning)
                    {
                        RequestTrayIconUpdate?.Invoke(TrayIconVisualState.InProgress, ProgressPercentage, SecondsRemaining, _pulsePhase);
                    }
                    else if (IsKeepAliveActive)
                    {
                        RequestTrayIconUpdate?.Invoke(TrayIconVisualState.InProgress, 100, 0, _pulsePhase);
                    }
                    else
                    {
                        RequestTrayIconUpdate?.Invoke(TrayIconVisualState.Idle, 0, 0, 1.0f);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient metrics read errors
                }
            }
        });
    }

    [RelayCommand]
    public void ToggleKeepAlive()
    {
        if (IsKeepAliveActive)
        {
            StopKeepAlive();
        }
        else
        {
            StartKeepAlive();
        }
    }

    private void StartKeepAlive()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts = new CancellationTokenSource();

        IsKeepAliveActive = true;
        KeepAliveButtonText = "Activo (Jitter ON)";
        CurrentStatusText = "Modo Trabajo Activo (Teams/Slack)";

        Task.Run(async () =>
        {
            try
            {
                await _systemAdapter.RunKeepAliveLoopAsync(
                    KeepAliveMode.Mixed,
                    45,
                    105,
                    msg => { },
                    _keepAliveCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                IsKeepAliveActive = false;
                KeepAliveButtonText = "Iniciar Modo";
                if (!IsTaskRunning)
                {
                    CurrentStatusText = "Listo";
                }
            }
        });
    }

    [RelayCommand]
    public void StopKeepAlive()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts = null;
        IsKeepAliveActive = false;
        KeepAliveButtonText = "Iniciar Modo";
        if (!IsTaskRunning)
        {
            CurrentStatusText = "Listo";
        }
    }

    [RelayCommand]
    public void StartSleepMode()
    {
        // 45 minutos de reposo progresivo
        StartCountdownTask("Modo Dormir (45m)", TimeSpan.FromMinutes(45), isSleepMode: true);
    }

    [RelayCommand]
    public void StartQuickShutdown(string minutesStr)
    {
        if (int.TryParse(minutesStr, out int minutes) && minutes > 0)
        {
            StartCountdownTask($"Apagado Rápido ({minutes}m)", TimeSpan.FromMinutes(minutes), isSleepMode: false);
        }
        else
        {
            StartCountdownTask($"Apagado Rápido ({QuickMinutesInput}m)", TimeSpan.FromMinutes(QuickMinutesInput), isSleepMode: false);
        }
    }

    [RelayCommand]
    public void StartProcessWatch()
    {
        if (string.IsNullOrWhiteSpace(SelectedProcessName)) return;

        _activeTaskCts?.Cancel();
        _activeTaskCts = new CancellationTokenSource();

        IsTaskRunning = true;
        ActiveTaskTitle = $"Monitoreando {SelectedProcessName}";
        CurrentStatusText = $"Esperando cierre de {SelectedProcessName}...";
        ProgressPercentage = 0;
        TimeRemainingText = "Vigilando proceso";

        var ct = _activeTaskCts.Token;

        Task.Run(async () =>
        {
            string targetName = SelectedProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            using var procTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));

            bool processDetectedOnce = false;

            while (!ct.IsCancellationRequested)
            {
                await procTimer.WaitForNextTickAsync(ct);

                bool isRunning = Process.GetProcessesByName(targetName).Length > 0;
                if (isRunning)
                {
                    processDetectedOnce = true;
                    CurrentStatusText = $"Proceso {targetName} en ejecución...";
                }
                else if (processDetectedOnce)
                {
                    // El proceso ha cerrado: Entrar en cuenta regresiva de gracia
                    CurrentStatusText = $"Proceso {targetName} terminado. Iniciando gracia...";
                    await TriggerGraceAndTerminalActionAsync(TerminalActionType.Shutdown, ct);
                    break;
                }
            }
        }, ct);
    }

    private void StartCountdownTask(string title, TimeSpan duration, bool isSleepMode)
    {
        _activeTaskCts?.Cancel();
        _activeTaskCts = new CancellationTokenSource();

        IsTaskRunning = true;
        ActiveTaskTitle = title;
        _totalTaskDuration = duration;
        _taskEndTime = DateTimeOffset.UtcNow.Add(duration);
        _isAudioFadeStarted = false;
        _isDisplayPowerOffSent = false;

        var ct = _activeTaskCts.Token;

        Task.Run(async () =>
        {
            _countdownTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            while (!ct.IsCancellationRequested)
            {
                await _countdownTimer.WaitForNextTickAsync(ct);

                var remaining = _taskEndTime - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    // Tiempo cumplido: disparar acción terminal
                    var terminalAction = isSleepMode ? TerminalActionType.Sleep : TerminalActionType.Shutdown;
                    await TriggerGraceAndTerminalActionAsync(terminalAction, ct);
                    break;
                }

                SecondsRemaining = (int)remaining.TotalSeconds;
                TimeRemainingText = remaining.ToString(@"hh\:mm\:ss");
                double elapsed = (_totalTaskDuration - remaining).TotalSeconds;
                ProgressPercentage = Math.Clamp((elapsed / _totalTaskDuration.TotalSeconds) * 100.0, 0, 100);
                CurrentStatusText = $"Ejecutando: {title}";

                // Lógica de Modo Dormir: fade de audio en los últimos 10 min (600s)
                if (isSleepMode && remaining <= TimeSpan.FromMinutes(10) && !_isAudioFadeStarted)
                {
                    _isAudioFadeStarted = true;
                    _ = _systemAdapter.SetMasterVolumeFadeAsync(0f, TimeSpan.FromMinutes(10), ct);
                }

                // Apagado de pantallas en los últimos 5 min (300s)
                if (isSleepMode && remaining <= TimeSpan.FromMinutes(5) && !_isDisplayPowerOffSent)
                {
                    _isDisplayPowerOffSent = true;
                    _ = _systemAdapter.SetDisplayPowerAsync(false, ct);
                }

                // Desplegar GraceOverlayWindow si restan <= 60 segundos
                if (remaining <= TimeSpan.FromSeconds(60))
                {
                    RequestGraceOverlay?.Invoke(true, SecondsRemaining);
                }
            }
        }, ct);
    }

    private async Task TriggerGraceAndTerminalActionAsync(TerminalActionType action, CancellationToken ct)
    {
        // Periodo de gracia final de 60 segundos
        const int graceSeconds = 60;
        RequestGraceOverlay?.Invoke(true, graceSeconds);

        using var graceTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        for (int i = graceSeconds; i > 0; i--)
        {
            SecondsRemaining = i;
            TimeRemainingText = $"00:00:{i:D2}";
            ProgressPercentage = 100.0;
            CurrentStatusText = $"¡Apagado inminente en {i}s! Presione Esc para cancelar.";
            RequestGraceOverlay?.Invoke(true, i);

            await graceTimer.WaitForNextTickAsync(ct);
        }

        // Ejecutar acción terminal
        RequestGraceOverlay?.Invoke(false, 0);
        RequestTrayIconUpdate?.Invoke(TrayIconVisualState.Completed, 100, 0, 1.0f);

        switch (action)
        {
            case TerminalActionType.Shutdown:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Shutdown, force: false, ct);
                break;
            case TerminalActionType.Sleep:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Sleep, force: false, ct);
                break;
            case TerminalActionType.LockStation:
                await _systemAdapter.SetPowerStateAsync(PowerAction.LockStation, force: false, ct);
                break;
        }

        AbortTask();
    }

    [RelayCommand]
    public void AbortTask()
    {
        _activeTaskCts?.Cancel();
        _activeTaskCts = null;

        IsTaskRunning = false;
        ActiveTaskTitle = "Ninguna tarea en curso";
        CurrentStatusText = "Listo";
        TimeRemainingText = "--:--:--";
        ProgressPercentage = 0;
        SecondsRemaining = 0;

        RequestGraceOverlay?.Invoke(false, 0);
        RequestTrayIconUpdate?.Invoke(TrayIconVisualState.Idle, 0, 0, 1.0f);
    }

    [RelayCommand]
    public void PostponeTask(string minutesStr)
    {
        int minutes = int.TryParse(minutesStr, out int m) ? m : 10;
        if (IsTaskRunning)
        {
            _taskEndTime = _taskEndTime.AddMinutes(minutes);
            _totalTaskDuration += TimeSpan.FromMinutes(minutes);
            RequestGraceOverlay?.Invoke(false, 0);
            CurrentStatusText = $"Pospuesto +{minutes} min.";
        }
    }

    [RelayCommand]
    public void ToggleStudioMode()
    {
        IsStudioMode = !IsStudioMode;
        if (IsStudioMode)
        {
            WindowWidth = 820;
            WindowHeight = 620;
        }
        else
        {
            WindowWidth = 460;
            WindowHeight = 580;
        }
    }

    [RelayCommand]
    public async Task TurnOffMonitorsNow()
    {
        await _systemAdapter.SetDisplayPowerAsync(false);
    }

    [RelayCommand]
    public void OpenQrModal()
    {
        RequestQrModal?.Invoke();
    }

    public void Dispose()
    {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _activeTaskCts?.Cancel();
        _activeTaskCts?.Dispose();
        _metricsTimer?.Dispose();
        _countdownTimer?.Dispose();
    }
}
