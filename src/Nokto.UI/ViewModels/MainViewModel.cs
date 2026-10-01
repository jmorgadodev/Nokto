using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.LanServer;
using Nokto.UI.Tray;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISystemAdapter _systemAdapter;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly PersistenceService _persistence;
    private readonly CancellationTokenSource _disposalCts = new();

    private LanHttpServer? _lanServer;
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
    private bool _isLanServerActive;

    partial void OnIsLanServerActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusLanDotColor));
    }

    public string StatusLanDotColor => IsLanServerActive ? "#00E676" : "#7D8390";

    public void AttachLanServer(LanHttpServer lanServer)
    {
        _lanServer = lanServer;
        _lanServer.ServerStateChanged += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsLanServerActive = _lanServer.IsRunning;
            });
        };
    }

    public void StartLanServer()
    {
        if (_lanServer != null && !_lanServer.IsRunning)
        {
            try
            {
                _lanServer.Start();
                LanConnectionUrl = _lanServer.GetConnectionUrl();
                IsLanServerActive = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LAN] Error al iniciar servidor bajo demanda: {ex.Message}");
            }
        }
        else if (_lanServer != null && _lanServer.IsRunning)
        {
            LanConnectionUrl = _lanServer.GetConnectionUrl();
        }
    }

    public void StopLanServer()
    {
        if (_lanServer != null && _lanServer.IsRunning)
        {
            try
            {
                _lanServer.Stop();
                IsLanServerActive = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LAN] Error al detener servidor: {ex.Message}");
            }
        }
    }

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

    partial void OnSelectedPresetChanged(PresetDefinition? value)
    {
        if (value != null)
        {
            LoadPresetIntoStudio(value);
        }
    }

    // ========================================================
    // --- MODO STUDIO: CONFIGURACIÓN INTERACTIVA (3 BLOQUES) -
    // ========================================================
    [ObservableProperty]
    private string _studioPresetId = "";

    [ObservableProperty]
    private string _studioPresetName = "";

    [ObservableProperty]
    private string _studioPresetDescription = "";

    // BLOQUE 1: DISPARADOR PRINCIPAL
    [ObservableProperty]
    private int _studioTriggerTypeIndex = 1; // 0=Proceso, 1=Cuenta Atrás, 2=Hora Fija, 3=Inactividad, 4=Silencio Audio, 5=Batería

    public bool IsStudioTriggerProcess => StudioTriggerTypeIndex == 0;
    public bool IsStudioTriggerCountdown => StudioTriggerTypeIndex == 1;
    public bool IsStudioTriggerExactTime => StudioTriggerTypeIndex == 2;
    public bool IsStudioTriggerInactivity => StudioTriggerTypeIndex == 3;
    public bool IsStudioTriggerAudioSilence => StudioTriggerTypeIndex == 4;
    public bool IsStudioTriggerBattery => StudioTriggerTypeIndex == 5;

    partial void OnStudioTriggerTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStudioTriggerProcess));
        OnPropertyChanged(nameof(IsStudioTriggerCountdown));
        OnPropertyChanged(nameof(IsStudioTriggerExactTime));
        OnPropertyChanged(nameof(IsStudioTriggerInactivity));
        OnPropertyChanged(nameof(IsStudioTriggerAudioSilence));
        OnPropertyChanged(nameof(IsStudioTriggerBattery));
    }

    [ObservableProperty]
    private string _studioProcessName = "notepad.exe";

    [ObservableProperty]
    private decimal _studioProcessDebounceSeconds = 5;

    [ObservableProperty]
    private decimal? _studioCountdownHours = 0;

    [ObservableProperty]
    private decimal? _studioCountdownMinutes = 30;

    [ObservableProperty]
    private decimal? _studioCountdownSeconds = 0;

    [ObservableProperty]
    private TimeSpan? _studioExactTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));

    [ObservableProperty]
    private decimal _studioInactivityMinutes = 15;

    [ObservableProperty]
    private decimal _studioAudioSilenceSeconds = 30;

    [ObservableProperty]
    private decimal _studioAudioSilencePeak = 0.001m;

    [ObservableProperty]
    private bool _studioBatteryOnAcDisconnect = true;

    [ObservableProperty]
    private bool _studioBatteryOnThreshold = false;

    [ObservableProperty]
    private decimal _studioBatteryThresholdPercent = 20;

    // BLOQUE 2: ACCIONES INTERMEDIAS
    public ObservableCollection<StudioStepItem> StudioPipelineSteps { get; } = [];

    // BLOQUE 3: ACCIÓN TERMINAL
    [ObservableProperty]
    private int _studioTerminalActionIndex = 0; // 0=Apagar, 1=Suspender, 2=Hibernar, 3=Reiniciar, 4=Bloquear, 5=Apagar Monitores, 6=Ninguna

    [ObservableProperty]
    private decimal _studioGracePeriodSeconds = 60; // 0 a 300s

    [ObservableProperty]
    private bool _studioForceClose = false;

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
            Name = "Nuevo Flujo",
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
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60),
                    ["forced"] = JsonSerializer.SerializeToElement(false)
                }
            }
        };

        Presets.Add(newPreset);
        SelectedPreset = newPreset;
        SaveCurrentPresets();
    }

    public void LoadPresetIntoStudio(PresetDefinition preset)
    {
        StudioPresetId = preset.Id;
        StudioPresetName = preset.Name;
        StudioPresetDescription = preset.Description;

        // Disparador
        if (preset.Trigger != null)
        {
            switch (preset.Trigger.Type)
            {
                case TriggerType.ProcessExit:
                    StudioTriggerTypeIndex = 0;
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("processName", out var pName))
                    {
                        StudioProcessName = pName.GetString() ?? "notepad.exe";
                    }
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("debounceSeconds", out var pDeb) &&
                        pDeb.TryGetDecimal(out var debVal))
                    {
                        StudioProcessDebounceSeconds = debVal;
                    }
                    break;

                case TriggerType.Countdown:
                    StudioTriggerTypeIndex = 1;
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("durationSeconds", out var pDur) &&
                        pDur.TryGetInt32(out var sec))
                    {
                        StudioCountdownHours = sec / 3600;
                        StudioCountdownMinutes = (sec % 3600) / 60;
                        StudioCountdownSeconds = sec % 60;
                    }
                    break;

                case TriggerType.UserIdle:
                    StudioTriggerTypeIndex = 3;
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("idleMinutes", out var pIdle) &&
                        pIdle.TryGetDecimal(out var idleVal))
                    {
                        StudioInactivityMinutes = idleVal;
                    }
                    break;

                case TriggerType.AudioSilence:
                    StudioTriggerTypeIndex = 4;
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("silenceThresholdSeconds", out var pSil) &&
                        pSil.TryGetDecimal(out var silVal))
                    {
                        StudioAudioSilenceSeconds = silVal;
                    }
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("thresholdPeak", out var pPeak) &&
                        pPeak.TryGetDecimal(out var peakVal))
                    {
                        StudioAudioSilencePeak = peakVal;
                    }
                    break;

                case TriggerType.BatteryState:
                    StudioTriggerTypeIndex = 5;
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("onAcDisconnect", out var pAc))
                    {
                        StudioBatteryOnAcDisconnect = pAc.GetBoolean();
                    }
                    if (preset.Trigger.Parameters != null &&
                        preset.Trigger.Parameters.TryGetValue("batteryLevelThreshold", out var pBat) &&
                        pBat.TryGetDecimal(out var batVal))
                    {
                        StudioBatteryThresholdPercent = batVal;
                        StudioBatteryOnThreshold = batVal > 0;
                    }
                    break;

                default:
                    StudioTriggerTypeIndex = 1;
                    break;
            }
        }
        else
        {
            StudioTriggerTypeIndex = 1;
        }

        // Acciones intermedias
        StudioPipelineSteps.Clear();
        if (preset.Pipeline != null)
        {
            foreach (var step in preset.Pipeline.OrderBy(s => s.StepOrder))
            {
                StudioPipelineSteps.Add(StudioStepItem.FromDefinition(step));
            }
        }
        RenumberStudioSteps();

        // Acción Terminal
        if (preset.TerminalAction != null)
        {
            StudioTerminalActionIndex = preset.TerminalAction.Type switch
            {
                TerminalActionType.Shutdown => 0,
                TerminalActionType.Sleep => 1,
                TerminalActionType.Hibernate => 2,
                TerminalActionType.Restart => 3,
                TerminalActionType.LockStation => 4,
                TerminalActionType.None => 6,
                _ => 0
            };

            if (preset.TerminalAction.Parameters != null)
            {
                if (preset.TerminalAction.Parameters.TryGetValue("gracePeriodSeconds", out var pGrace) &&
                    pGrace.TryGetDecimal(out var graceVal))
                {
                    StudioGracePeriodSeconds = Math.Clamp(graceVal, 0, 300);
                }
                if (preset.TerminalAction.Parameters.TryGetValue("forced", out var pForce))
                {
                    StudioForceClose = pForce.GetBoolean();
                }
            }
        }
        else
        {
            StudioTerminalActionIndex = 0;
            StudioGracePeriodSeconds = 60;
            StudioForceClose = false;
        }
    }

    public PresetDefinition BuildPresetFromStudio(string? overrideId = null)
    {
        string presetId = string.IsNullOrWhiteSpace(overrideId)
            ? (string.IsNullOrWhiteSpace(StudioPresetId) ? "preset_" + Guid.NewGuid().ToString("N")[..8] : StudioPresetId)
            : overrideId;

        // Disparador
        TriggerDefinition trigger;
        switch (StudioTriggerTypeIndex)
        {
            case 0: // Proceso
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.ProcessExit,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["processName"] = JsonSerializer.SerializeToElement(string.IsNullOrWhiteSpace(StudioProcessName) ? "notepad.exe" : StudioProcessName),
                        ["debounceSeconds"] = JsonSerializer.SerializeToElement((int)StudioProcessDebounceSeconds)
                    }
                };
                break;

            case 1: // Cuenta Atrás
            default:
                int totalSec = (int)((StudioCountdownHours ?? 0) * 3600 + (StudioCountdownMinutes ?? 0) * 60 + (StudioCountdownSeconds ?? 0));
                if (totalSec <= 0) totalSec = 60;
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.Countdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(totalSec)
                    }
                };
                break;

            case 2: // Hora Fija
                DateTime now = DateTime.Now;
                var span = StudioExactTime ?? DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
                DateTime target = now.Date + span;
                if (target <= now) target = target.AddDays(1);
                int waitSec = (int)(target - now).TotalSeconds;
                if (waitSec <= 0) waitSec = 5;
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.Countdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(waitSec)
                    }
                };
                break;

            case 3: // Inactividad
                int idleM = (int)StudioInactivityMinutes;
                if (idleM <= 0) idleM = 1;
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.UserIdle,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["idleMinutes"] = JsonSerializer.SerializeToElement(idleM)
                    }
                };
                break;

            case 4: // Silencio WASAPI
                int silSec = (int)StudioAudioSilenceSeconds;
                if (silSec <= 0) silSec = 30;
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.AudioSilence,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["silenceThresholdSeconds"] = JsonSerializer.SerializeToElement(silSec),
                        ["thresholdPeak"] = JsonSerializer.SerializeToElement((double)StudioAudioSilencePeak)
                    }
                };
                break;

            case 5: // Batería
                int batPct = StudioBatteryOnThreshold ? (int)StudioBatteryThresholdPercent : 0;
                trigger = new TriggerDefinition
                {
                    Type = TriggerType.BatteryState,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["onAcDisconnect"] = JsonSerializer.SerializeToElement(StudioBatteryOnAcDisconnect),
                        ["batteryLevelThreshold"] = JsonSerializer.SerializeToElement(batPct)
                    }
                };
                break;
        }

        // Acciones intermedias
        var pipeline = StudioPipelineSteps.Select(s => s.ToDefinition()).ToList();

        // Acción Terminal
        TerminalActionType termType = StudioTerminalActionIndex switch
        {
            0 => TerminalActionType.Shutdown,
            1 => TerminalActionType.Sleep,
            2 => TerminalActionType.Hibernate,
            3 => TerminalActionType.Restart,
            4 => TerminalActionType.LockStation,
            5 => TerminalActionType.None,
            6 => TerminalActionType.None,
            _ => TerminalActionType.Shutdown
        };

        if (StudioTerminalActionIndex == 5)
        {
            pipeline.Add(new PipelineStepDefinition
            {
                StepOrder = pipeline.Count + 1,
                ActionType = ActionType.TurnOffMonitors,
                IgnoreFailure = true
            });
        }

        var terminal = new TerminalActionDefinition
        {
            Type = termType,
            Parameters = new Dictionary<string, JsonElement>
            {
                ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement((int)StudioGracePeriodSeconds),
                ["forced"] = JsonSerializer.SerializeToElement(StudioForceClose)
            }
        };

        return new PresetDefinition
        {
            Id = presetId,
            Name = string.IsNullOrWhiteSpace(StudioPresetName) ? "Flujo Personalizado" : StudioPresetName,
            Description = StudioPresetDescription,
            Icon = "Sparkles",
            Trigger = trigger,
            Pipeline = pipeline,
            TerminalAction = terminal
        };
    }

    [RelayCommand]
    public void SaveStudioPreset()
    {
        var updated = BuildPresetFromStudio();
        int existingIndex = -1;
        for (int i = 0; i < Presets.Count; i++)
        {
            if (Presets[i].Id == updated.Id)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            Presets[existingIndex] = updated;
        }
        else
        {
            Presets.Add(updated);
        }

        SelectedPreset = updated;
        SaveCurrentPresets();
        CurrentStatusText = $"Flujo '{updated.Name}' guardado con éxito.";
    }

    [RelayCommand]
    public async Task StartStudioPreset()
    {
        var preset = BuildPresetFromStudio();
        await _workflowEngine.StartPresetAsync(preset);
    }

    [RelayCommand]
    public void DeleteSelectedPreset()
    {
        if (SelectedPreset == null) return;

        var toRemove = SelectedPreset;
        Presets.Remove(toRemove);
        SaveCurrentPresets();

        SelectedPreset = Presets.FirstOrDefault();
        CurrentStatusText = $"Flujo '{toRemove.Name}' eliminado.";
    }

    [RelayCommand]
    public void AddStudioStep(string? actionTypeStr)
    {
        var actionType = actionTypeStr switch
        {
            "Screenshot" => ActionType.CaptureScreenshot,
            "AudioFade" => ActionType.AudioFadeOut,
            "MediaControl" => ActionType.MediaControl,
            "Command" => ActionType.ExecuteCommand,
            "MonitorsOff" => ActionType.TurnOffMonitors,
            _ => ActionType.CaptureScreenshot
        };

        var step = new StudioStepItem
        {
            StepOrder = StudioPipelineSteps.Count + 1,
            ActionType = actionType,
            IgnoreFailure = true
        };
        StudioPipelineSteps.Add(step);
        RenumberStudioSteps();
    }

    [RelayCommand]
    public void RemoveStudioStep(StudioStepItem step)
    {
        if (StudioPipelineSteps.Remove(step))
        {
            RenumberStudioSteps();
        }
    }

    [RelayCommand]
    public void MoveStudioStepUp(StudioStepItem step)
    {
        int index = StudioPipelineSteps.IndexOf(step);
        if (index > 0)
        {
            StudioPipelineSteps.Move(index, index - 1);
            RenumberStudioSteps();
        }
    }

    [RelayCommand]
    public void MoveStudioStepDown(StudioStepItem step)
    {
        int index = StudioPipelineSteps.IndexOf(step);
        if (index >= 0 && index < StudioPipelineSteps.Count - 1)
        {
            StudioPipelineSteps.Move(index, index + 1);
            RenumberStudioSteps();
        }
    }

    private void RenumberStudioSteps()
    {
        for (int i = 0; i < StudioPipelineSteps.Count; i++)
        {
            StudioPipelineSteps[i].StepOrder = i + 1;
        }
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
        StartLanServer();
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
