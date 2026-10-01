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

    partial void OnIsKeepAliveActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(EngineStateText));
    }

    [ObservableProperty]
    private string _lanConnectionUrl = "http://localhost:4884";

    [ObservableProperty]
    private string _keepAliveButtonText = "Iniciar Modo";

    [ObservableProperty]
    private bool _isTaskRunning;

    partial void OnIsTaskRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(EngineStateText));
    }

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
    private double _windowWidth = 880;

    [ObservableProperty]
    private double _windowHeight = 640;

    [ObservableProperty]
    private string _selectedProcessName = "blender.exe";

    [ObservableProperty]
    private int _quickMinutesInput = 30;

    [ObservableProperty]
    private PresetDefinition? _selectedPreset;

    // --- CONFIGURACIÓN MANUAL: DISPARADOR ---
    [ObservableProperty]
    private int _selectedTriggerTypeIndex = 0;

    public bool IsTriggerCountdown => SelectedTriggerTypeIndex == 0;
    public bool IsTriggerExactTime => SelectedTriggerTypeIndex == 1;
    public bool IsTriggerInactivity => SelectedTriggerTypeIndex == 2;
    public bool IsTriggerProcessExit => SelectedTriggerTypeIndex == 3;
    public bool IsTriggerAudioSilence => SelectedTriggerTypeIndex == 4;
    public bool IsTriggerBatteryState => SelectedTriggerTypeIndex == 5;

    partial void OnSelectedTriggerTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTriggerCountdown));
        OnPropertyChanged(nameof(IsTriggerExactTime));
        OnPropertyChanged(nameof(IsTriggerInactivity));
        OnPropertyChanged(nameof(IsTriggerProcessExit));
        OnPropertyChanged(nameof(IsTriggerAudioSilence));
        OnPropertyChanged(nameof(IsTriggerBatteryState));
    }

    [ObservableProperty]
    private decimal _audioSilenceSeconds = 30;

    [ObservableProperty]
    private bool _batteryTriggerOnAcDisconnect = true;

    [ObservableProperty]
    private bool _batteryTriggerOnThreshold = false;

    [ObservableProperty]
    private decimal _batteryThresholdPercent = 20;

    [ObservableProperty]
    private decimal? _countdownHours = 0;

    partial void OnCountdownHoursChanged(decimal? value) => OnPropertyChanged(nameof(FormattedCountdownText));

    [ObservableProperty]
    private decimal? _countdownMinutes = 30;

    partial void OnCountdownMinutesChanged(decimal? value) => OnPropertyChanged(nameof(FormattedCountdownText));

    [ObservableProperty]
    private decimal? _countdownSeconds = 0;

    partial void OnCountdownSecondsChanged(decimal? value) => OnPropertyChanged(nameof(FormattedCountdownText));

    public string FormattedCountdownText
    {
        get
        {
            int h = (int)(CountdownHours ?? 0);
            int m = (int)(CountdownMinutes ?? 0);
            int s = (int)(CountdownSeconds ?? 0);
            int totalSec = h * 3600 + m * 60 + s;
            if (totalSec <= 0) return "00h 00m 00s (Sin tiempo fijado)";
            var targetTime = DateTime.Now.AddSeconds(totalSec);
            return $"{h:D2}h {m:D2}m {s:D2}s (Activará a las {targetTime:HH:mm:ss})";
        }
    }

    [ObservableProperty]
    private TimeSpan? _exactTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));

    partial void OnExactTimeChanged(TimeSpan? value)
    {
        OnPropertyChanged(nameof(ExactTimeSummaryText));
        OnPropertyChanged(nameof(SelectedExactTime));
    }

    public string ExactTimeSummaryText
    {
        get
        {
            var span = ExactTime ?? DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
            DateTime now = DateTime.Now;
            DateTime target = now.Date + span;
            if (target <= now) target = target.AddDays(1);
            TimeSpan diff = target - now;
            return $"Se ejecutará a las {target:HH:mm} (en {(int)diff.TotalHours}h {diff.Minutes}m)";
        }
    }

    [ObservableProperty]
    private decimal _inactivityMinutes = 15;

    [ObservableProperty]
    private bool _enableCpuThreshold = false;

    [ObservableProperty]
    private decimal _cpuThreshold = 8;

    // Propiedades de enlace compatibles / alias
    public bool IsTriggerIdle => IsTriggerInactivity;
    public decimal IdleMinutesThreshold
    {
        get => InactivityMinutes;
        set => InactivityMinutes = value;
    }
    public TimeSpan? SelectedExactTime
    {
        get => ExactTime;
        set => ExactTime = value;
    }
    public string SelectedProcessToWatch
    {
        get => SelectedProcessName;
        set => SelectedProcessName = value;
    }
    public bool WaitForCpuDrop
    {
        get => EnableCpuThreshold;
        set => EnableCpuThreshold = value;
    }

    // --- CONFIGURACIÓN MANUAL: ACCIÓN TERMINAL Y MODIFICADORES ---
    [ObservableProperty]
    private int _selectedTerminalActionIndex = 0;

    [ObservableProperty]
    private bool _optForceClose = false;

    [ObservableProperty]
    private bool _optAudioFadeOut = true;

    [ObservableProperty]
    private bool _optGracePeriod = true;

    [ObservableProperty]
    private bool _optScreenshot = false;

    public string StatusDotColor => IsTaskRunning ? "#00E676" : (IsKeepAliveActive ? "#00D2FF" : "#7D8390");
    public string EngineStateText => IsTaskRunning ? "En ejecución" : (IsKeepAliveActive ? "Keep-Alive" : "Inactivo");

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
    public void IncrementHours() => CountdownHours = Math.Min((CountdownHours ?? 0) + 1, 23);

    [RelayCommand]
    public void DecrementHours() => CountdownHours = Math.Max((CountdownHours ?? 0) - 1, 0);

    [RelayCommand]
    public void IncrementMinutes() => CountdownMinutes = Math.Min((CountdownMinutes ?? 0) + 1, 59);

    [RelayCommand]
    public void DecrementMinutes() => CountdownMinutes = Math.Max((CountdownMinutes ?? 0) - 1, 0);

    [RelayCommand]
    public void IncrementSeconds() => CountdownSeconds = Math.Min((CountdownSeconds ?? 0) + 1, 59);

    [RelayCommand]
    public void DecrementSeconds() => CountdownSeconds = Math.Max((CountdownSeconds ?? 0) - 1, 0);

    [RelayCommand]
    public void AddCountdownMinutes(string minutesStr)
    {
        if (int.TryParse(minutesStr, out int mins))
        {
            int totalMins = (int)((CountdownHours ?? 0) * 60 + (CountdownMinutes ?? 0) + mins);
            if (totalMins < 0) totalMins = 0;
            CountdownHours = Math.Clamp(totalMins / 60, 0, 23);
            CountdownMinutes = Math.Clamp(totalMins % 60, 0, 59);
        }
    }

    [RelayCommand]
    public void ResetCountdown()
    {
        CountdownHours = 0;
        CountdownMinutes = 0;
        CountdownSeconds = 0;
    }

    [RelayCommand]
    public void RefreshProcesses()
    {
        RefreshAvailableProcesses();
    }

    [RelayCommand]
    public async Task StartManualTask()
    {
        string taskName;
        TriggerDefinition trigger;

        switch (SelectedTriggerTypeIndex)
        {
            case 1: // Hora Exacta
                DateTime now = DateTime.Now;
                var span = ExactTime ?? DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
                DateTime target = now.Date + span;
                if (target <= now) target = target.AddDays(1);
                int waitSeconds = (int)(target - now).TotalSeconds;
                if (waitSeconds <= 0) waitSeconds = 5;
                taskName = $"Hora Exacta ({target:HH:mm})";
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.Countdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(waitSeconds)
                    }
                };
                break;

            case 2: // Inactividad
                int idleMins = (int)InactivityMinutes;
                if (idleMins <= 0) idleMins = 1;
                taskName = $"Inactividad ({idleMins} min)";
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.UserIdle,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["idleMinutes"] = JsonSerializer.SerializeToElement(idleMins)
                    }
                };
                break;

            case 3: // Al Terminar Proceso
                string proc = string.IsNullOrWhiteSpace(SelectedProcessName) ? "notepad.exe" : SelectedProcessName;
                taskName = $"Vigilar {proc}";
                if (EnableCpuThreshold)
                {
                    trigger = new TriggerDefinition
                    {
                        Type = TriggerType.SustainedLoad,
                        Parameters = new Dictionary<string, JsonElement>
                        {
                            ["thresholdPercentage"] = JsonSerializer.SerializeToElement((double)CpuThreshold),
                            ["durationSeconds"] = JsonSerializer.SerializeToElement(60)
                        }
                    };
                }
                else
                {
                    trigger = new TriggerDefinition
                    {
                        Type = TriggerType.ProcessExit,
                        Parameters = new Dictionary<string, JsonElement>
                        {
                            ["processName"] = JsonSerializer.SerializeToElement(proc),
                            ["debounceSeconds"] = JsonSerializer.SerializeToElement(5)
                        }
                    };
                }
                break;

            case 4: // Silencio de Audio
                int silenceSec = (int)AudioSilenceSeconds;
                if (silenceSec <= 0) silenceSec = 30;
                taskName = $"Silencio de Audio ({silenceSec}s)";
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.AudioSilence,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["silenceThresholdSeconds"] = JsonSerializer.SerializeToElement(silenceSec),
                        ["thresholdPeak"] = JsonSerializer.SerializeToElement(0.001)
                    }
                };
                break;

            case 5: // Estado de Batería
                int batPct = BatteryTriggerOnThreshold ? (int)BatteryThresholdPercent : 0;
                taskName = BatteryTriggerOnAcDisconnect ? "Desconexión de Cargador (AC)" : $"Batería baja ({batPct}%)";
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.BatteryState,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["onAcDisconnect"] = JsonSerializer.SerializeToElement(BatteryTriggerOnAcDisconnect),
                        ["batteryLevelThreshold"] = JsonSerializer.SerializeToElement(batPct)
                    }
                };
                break;

            case 0: // Cuenta Atrás
            default:
                int totalSeconds = (int)((CountdownHours ?? 0) * 3600 + (CountdownMinutes ?? 0) * 60 + (CountdownSeconds ?? 0));
                if (totalSeconds <= 0) totalSeconds = 60;
                taskName = $"Cuenta Atrás ({TimeSpan.FromSeconds(totalSeconds):hh\\:mm\\:ss})";
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.Countdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(totalSeconds)
                    }
                };
                break;
        }

        var pipeline = new List<PipelineStepDefinition>();
        int stepOrder = 1;

        if (OptScreenshot)
        {
            pipeline.Add(new PipelineStepDefinition
            {
                StepOrder = stepOrder++,
                ActionType = ActionType.CaptureScreenshot,
                IgnoreFailure = true
            });
        }

        if (OptAudioFadeOut)
        {
            pipeline.Add(new PipelineStepDefinition
            {
                StepOrder = stepOrder++,
                ActionType = ActionType.AudioFadeOut,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["durationSeconds"] = JsonSerializer.SerializeToElement(15),
                    ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(0)
                },
                IgnoreFailure = true
            });
        }

        TerminalActionType terminalType;
        switch (SelectedTerminalActionIndex)
        {
            case 0:
                terminalType = TerminalActionType.Shutdown;
                break;
            case 1:
                terminalType = TerminalActionType.Sleep;
                break;
            case 2:
                terminalType = TerminalActionType.Hibernate;
                break;
            case 3:
                terminalType = TerminalActionType.Restart;
                break;
            case 4:
                terminalType = TerminalActionType.LockStation;
                break;
            case 5:
                pipeline.Add(new PipelineStepDefinition
                {
                    StepOrder = stepOrder++,
                    ActionType = ActionType.TurnOffMonitors,
                    IgnoreFailure = true
                });
                terminalType = TerminalActionType.None;
                break;
            default:
                terminalType = TerminalActionType.Shutdown;
                break;
        }

        int graceSeconds = OptGracePeriod ? 60 : 0;
        var terminal = new TerminalActionDefinition
        {
            Type = terminalType,
            Parameters = new Dictionary<string, JsonElement>
            {
                ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(graceSeconds),
                ["forced"] = JsonSerializer.SerializeToElement(OptForceClose)
            }
        };

        var preset = new PresetDefinition
        {
            Id = "manual_task_" + Guid.NewGuid().ToString("N")[..8],
            Name = taskName,
            Description = "Tarea configurada manualmente por el usuario.",
            Trigger = trigger,
            Pipeline = pipeline,
            TerminalAction = terminal
        };

        await _workflowEngine.StartPresetAsync(preset);
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
