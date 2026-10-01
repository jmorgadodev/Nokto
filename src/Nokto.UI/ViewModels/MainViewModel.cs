using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.UI.Tray;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISystemAdapter _systemAdapter;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly PersistenceService _persistence;
    private readonly CancellationTokenSource _disposalCts = new();

    private CancellationTokenSource? _keepAliveCts;
    private PeriodicTimer? _metricsTimer;
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

    [ObservableProperty]
    private PresetDefinition? _selectedPreset;

    public ObservableCollection<string> AvailableProcesses { get; } = [];
    public ObservableCollection<PresetDefinition> Presets { get; } = [];

    public MainViewModel(ISystemAdapter systemAdapter, IWorkflowEngine? engine = null, PersistenceService? persistence = null)
    {
        _systemAdapter = systemAdapter;
        _persistence = persistence ?? new PersistenceService();
        _workflowEngine = engine ?? new WorkflowEngine(systemAdapter, _persistence);

        LoadPresetsFromStorage();
        RefreshAvailableProcesses();
        SubscribeToEngineEvents();
        StartMetricsMonitoring();
    }

    private void SubscribeToEngineEvents()
    {
        _workflowEngine.StatusChanged += HandleEngineStatusChanged;
        _workflowEngine.GracePeriodTick += HandleGracePeriodTick;
        _workflowEngine.LogMessageReceived += HandleEngineLog;
    }

    private void HandleEngineStatusChanged(SystemStatusState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsTaskRunning = state.EngineState != EngineState.Idle &&
                            state.EngineState != EngineState.Completed &&
                            state.EngineState != EngineState.Failed;

            CurrentStatusText = state.EngineState switch
            {
                EngineState.Idle => "Listo",
                EngineState.WaitingTrigger => $"Esperando disparador ({state.ActivePresetName ?? "Tarea"})...",
                EngineState.ExecutingActions => "Ejecutando acciones intermedias...",
                EngineState.GracePeriod => $"¡Gracia activa! Apagado en {state.GracePeriodRemainingSeconds}s",
                EngineState.Paused => "Flujo en pausa",
                EngineState.Completed => "Flujo completado con éxito",
                EngineState.Failed => "Flujo abortado por error en paso",
                _ => "Listo"
            };

            if (IsTaskRunning)
            {
                ActiveTaskTitle = state.ActivePresetName ?? "Flujo Activo";
                SecondsRemaining = state.TimeRemainingSeconds;
                TimeRemainingText = TimeSpan.FromSeconds(state.TimeRemainingSeconds).ToString(@"hh\:mm\:ss");
                ProgressPercentage = state.ProgressPercentage;
            }
            else
            {
                ActiveTaskTitle = "Ninguna tarea en curso";
                TimeRemainingText = "--:--:--";
                ProgressPercentage = 0;
                SecondsRemaining = 0;
                RequestGraceOverlay?.Invoke(false, 0);
            }
        });
    }

    private void HandleGracePeriodTick(int secondsRemaining)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SecondsRemaining = secondsRemaining;
            TimeRemainingText = $"00:00:{secondsRemaining:D2}";
            ProgressPercentage = 100.0;
            CurrentStatusText = $"¡Apagado inminente en {secondsRemaining}s! Presione Esc para cancelar.";
            RequestGraceOverlay?.Invoke(true, secondsRemaining);
        });
    }

    private void HandleEngineLog(string message)
    {
        Debug.WriteLine(message);
    }

    private void LoadPresetsFromStorage()
    {
        Presets.Clear();
        var presetsFile = _persistence.LoadPresets();
        foreach (var p in presetsFile.Presets)
        {
            Presets.Add(p);
        }

        if (Presets.Count > 0)
        {
            SelectedPreset = Presets[0];
        }
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
    public async Task StartSleepMode()
    {
        var sleepPreset = new PresetDefinition
        {
            Id = "preset_quick_sleep",
            Name = "Modo Dormir (45 min)",
            Description = "Cuenta regresiva con atenuación de volumen y suspensión.",
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.Countdown,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["durationSeconds"] = JsonSerializer.SerializeToElement(45 * 60)
                }
            },
            Pipeline =
            [
                new PipelineStepDefinition
                {
                    StepOrder = 1,
                    ActionType = ActionType.AudioFadeOut,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(15),
                        ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(0)
                    },
                    IgnoreFailure = true
                },
                new PipelineStepDefinition
                {
                    StepOrder = 2,
                    ActionType = ActionType.TurnOffMonitors,
                    IgnoreFailure = true
                }
            ],
            TerminalAction = new TerminalActionDefinition
            {
                Type = TerminalActionType.Sleep,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(30)
                }
            }
        };

        await _workflowEngine.StartPresetAsync(sleepPreset);
    }

    [RelayCommand]
    public async Task StartQuickShutdown(string minutesStr)
    {
        int minutes = int.TryParse(minutesStr, out int m) && m > 0 ? m : QuickMinutesInput;
        await _workflowEngine.StartQuickCountdownAsync(
            $"Apagado Rápido ({minutes}m)",
            TimeSpan.FromMinutes(minutes),
            TerminalActionType.Shutdown);
    }

    [RelayCommand]
    public async Task StartProcessWatch()
    {
        if (string.IsNullOrWhiteSpace(SelectedProcessName)) return;

        var procPreset = new PresetDefinition
        {
            Id = "preset_watch_" + SelectedProcessName,
            Name = $"Vigilar {SelectedProcessName}",
            Description = $"Supervisa {SelectedProcessName}, toma captura y apaga con 60s de gracia.",
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.ProcessExit,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["processName"] = JsonSerializer.SerializeToElement(SelectedProcessName),
                    ["debounceSeconds"] = JsonSerializer.SerializeToElement(5)
                }
            },
            Pipeline =
            [
                new PipelineStepDefinition
                {
                    StepOrder = 1,
                    ActionType = ActionType.CaptureScreenshot,
                    IgnoreFailure = true
                },
                new PipelineStepDefinition
                {
                    StepOrder = 2,
                    ActionType = ActionType.AudioFadeOut,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(10),
                        ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(0)
                    },
                    IgnoreFailure = true
                }
            ],
            TerminalAction = new TerminalActionDefinition
            {
                Type = TerminalActionType.Shutdown,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60),
                    ["forced"] = JsonSerializer.SerializeToElement(false)
                }
            }
        };

        await _workflowEngine.StartPresetAsync(procPreset);
    }

    [RelayCommand]
    public async Task ExecuteSelectedPreset()
    {
        if (SelectedPreset != null)
        {
            await _workflowEngine.StartPresetAsync(SelectedPreset);
        }
    }

    [RelayCommand]
    public void SaveCurrentPresets()
    {
        var file = new PresetsFile
        {
            Version = 1,
            Presets = [.. Presets]
        };
        _persistence.SavePresets(file);
        CurrentStatusText = "Presets guardados en presets.json.";
    }

    [RelayCommand]
    public void AddNewPreset()
    {
        var newPreset = new PresetDefinition
        {
            Id = "preset_" + Guid.NewGuid().ToString("N")[..8],
            Name = "Nuevo Flujo Personalizado",
            Description = "Descripción del nuevo flujo automatizado.",
            IsFavorite = false,
            Icon = "Sparkles",
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.Countdown,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["durationSeconds"] = JsonSerializer.SerializeToElement(300)
                }
            },
            Pipeline =
            [
                new PipelineStepDefinition
                {
                    StepOrder = 1,
                    ActionType = ActionType.TurnOffMonitors,
                    IgnoreFailure = true
                }
            ],
            TerminalAction = new TerminalActionDefinition
            {
                Type = TerminalActionType.Shutdown,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60)
                }
            }
        };

        Presets.Add(newPreset);
        SelectedPreset = newPreset;
        SaveCurrentPresets();
    }

    [RelayCommand]
    public void AbortTask()
    {
        _workflowEngine.Abort();

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
        _workflowEngine.Postpone(TimeSpan.FromMinutes(minutes));
        RequestGraceOverlay?.Invoke(false, 0);
        CurrentStatusText = $"Pospuesto +{minutes} min.";
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
        _metricsTimer?.Dispose();
        _workflowEngine.Dispose();
    }
}
