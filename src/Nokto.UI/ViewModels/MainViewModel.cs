using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;
using Nokto.Platform.Windows.Hotkeys;
using Nokto.Platform.Windows.Network;
using Nokto.Platform.Windows.Startup;
using Nokto.UI.Tray;

namespace Nokto.UI.ViewModels;

public class EvidenceItem
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string TimestampText { get; set; } = "";
    public string FileSizeText { get; set; } = "";
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISystemAdapter _systemAdapter;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly PersistenceService _persistence;
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly AppConfig _config;
    private GlobalHotkeyService? _panicHotkeyService;
    private readonly IAiQuotaService _aiQuotaService;

    private CancellationTokenSource? _keepAliveCts;
    private PeriodicTimer? _metricsTimer;
    private float _pulsePhase = 0f;
    private int _batteryGuardianTicks = 0;
    private int _scheduleThemeTicks = 0;
    private int _networkTickCounter = 0;
    private int _quotaRefreshInProgress;

    public event Action<TrayIconVisualState, double, int, float>? RequestTrayIconUpdate;
    public event Action<bool, int>? RequestGraceOverlay;
    public event Func<Task<string?>>? RequestFilePicker;

    // ========================================================
    // --- NAVEGACIÓN PRINCIPAL (4 PESTAÑAS) -------------------
    // ========================================================
    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0=Inicio, 1=Control Manual, 2=Rutinas, 3=Ajustes

    [RelayCommand]
    public void NavigateToTab(object? param)
    {
        if (param is int idx)
        {
            SelectedTabIndex = idx;
        }
        else if (param is string s && int.TryParse(s, out int parsed))
        {
            SelectedTabIndex = parsed;
        }
    }

    // ========================================================
    // --- PESTAÑA PRINCIPAL [ ◈ INICIO ] ----------------------
    // ========================================================
    [ObservableProperty]
    private string _localIpAddress = "127.0.0.1";

    [ObservableProperty]
    private string _networkNameAndType = "Consultando red local...";

    [ObservableProperty]
    private string _connectedNetworkName = "Consultando red local...";

    [ObservableProperty]
    private string _networkInterfaceText = "Consultando interfaz...";

    [ObservableProperty]
    private bool _isVpnConnected;

    [ObservableProperty]
    private string _vpnStatusText = "VPN: Desconectada (Tráfico directo)";

    [ObservableProperty]
    private string _batteryAndPowerText = "Red Eléctrica (AC) - 100%";

    [ObservableProperty]
    private string _powerSourceStatusText = "Red Eléctrica (AC) - 100%";

    [ObservableProperty]
    private string _workModeStatusText = "Inactivo";

    public bool IsWorkModeRunning => WorkModeStatusText != "Inactivo";
    partial void OnWorkModeStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsWorkModeRunning));
        OnPropertyChanged(nameof(WorkModeStatusBadge));
    }

    public string WorkModeStatusBadge => WorkModeStatusText;
    public string WorkModeDetailText => IsWorkModeRunning ? "Mantener equipo activo para evitar el estado Ausente." : "El equipo puede entrar en reposo automáticamente.";

    public HomeDashboardSettings Config { get; }
    public bool MinimizeToTrayOnClose
    {
        get => _config.Settings.MinimizeToTrayOnClose;
        set
        {
            if (_config.Settings.MinimizeToTrayOnClose == value) return;
            _config.Settings.MinimizeToTrayOnClose = value;
            _persistence.SaveConfig(_config);
            OnPropertyChanged();
        }
    }
    public HardwareProfile HardwareProfile { get; }
    public int NetworkCardColumnSpan => Config.ShowEnergyStatusCardInHome ? 1 : 2;
    public int EnergyCardColumn => Config.ShowNetworkCardInHome ? 1 : 0;
    public int EnergyCardColumnSpan => Config.ShowNetworkCardInHome ? 1 : 2;
    public int HardwareCardColumnSpan => Config.ShowAiRadarCardInHome ? 1 : 2;
    public int AiRadarCardColumn => Config.ShowHardwareCardInHome ? 1 : 0;
    public int AiRadarCardColumnSpan => Config.ShowHardwareCardInHome ? 1 : 2;

    [ObservableProperty]
    private AudioDeviceProfile _audioDevices = new();
    [ObservableProperty]
    private string _uptimeText = FormatUptime();

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}d {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    private void RefreshHomeStatusCardLayout()
    {
        OnPropertyChanged(nameof(NetworkCardColumnSpan));
        OnPropertyChanged(nameof(EnergyCardColumn));
        OnPropertyChanged(nameof(EnergyCardColumnSpan));
        OnPropertyChanged(nameof(HardwareCardColumnSpan));
        OnPropertyChanged(nameof(AiRadarCardColumn));
        OnPropertyChanged(nameof(AiRadarCardColumnSpan));
    }

    [ObservableProperty]
    private AiQuotaSnapshot _aiQuotaSnapshot = new(false, [], null);

    public ObservableCollection<AiEnvironmentItem> AiEnvironments { get; } = new();
    public ObservableCollection<AiEnvironmentItem> DetectedAiEnvironments { get; } = new();
    public bool HasDetectedAiEnvironments => DetectedAiEnvironments.Count > 0;
    public bool HasVisibleAiEnvironments => AiEnvironments.Count > 0;
    private void SetAiEnvironmentVisibility(string id, bool visible)
    {
        _config.Settings.HiddenAiEnvironmentIds ??= [];
        _config.Settings.HiddenAiEnvironmentIds.RemoveAll(hidden => hidden == id);
        if (!visible) _config.Settings.HiddenAiEnvironmentIds.Add(id);
        _persistence.SaveConfig(_config);
        RefreshVisibleAiEnvironments();
    }
    private void RefreshVisibleAiEnvironments()
    {
        AiEnvironments.Clear();
        foreach (var item in DetectedAiEnvironments.Where(item => item.ShowInHome)) AiEnvironments.Add(item);
        OnPropertyChanged(nameof(HasVisibleAiEnvironments));
    }

    private void ApplyAiEnvironmentOrder()
    {
        var preference = _config.Settings.AiEnvironmentOrder ?? ["codex"];
        var ordered = DetectedAiEnvironments.OrderBy(item =>
        {
            int index = preference.IndexOf(item.Id);
            return index < 0 ? int.MaxValue : index;
        }).ToArray();
        for (int index = 0; index < ordered.Length; index++)
            DetectedAiEnvironments.Move(DetectedAiEnvironments.IndexOf(ordered[index]), index);
        UpdateAiOrderPositions();
        RefreshVisibleAiEnvironments();
    }

    private void UpdateAiOrderPositions()
    {
        for (int index = 0; index < DetectedAiEnvironments.Count; index++)
            DetectedAiEnvironments[index].SetOrderPosition(index, DetectedAiEnvironments.Count);
    }

    private void MoveAiEnvironment(string id, int direction)
    {
        int index = DetectedAiEnvironments.ToList().FindIndex(item => item.Id == id);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= DetectedAiEnvironments.Count) return;
        DetectedAiEnvironments.Move(index, target);
        // Retain preferences for tools temporarily absent from the local installation.
        _config.Settings.AiEnvironmentOrder = DetectedAiEnvironments.Select(item => item.Id)
            .Concat(_config.Settings.AiEnvironmentOrder ?? []).Distinct(StringComparer.Ordinal).ToList();
        _persistence.SaveConfig(_config);
        UpdateAiOrderPositions();
        RefreshVisibleAiEnvironments();
    }

    [RelayCommand]
    public void OpenAntigravity() => _aiQuotaService.LaunchEnvironment("antigravity");

    [RelayCommand]
    public void OpenCodex() => _aiQuotaService.LaunchEnvironment("codex");

    [RelayCommand]
    public void OpenOpenCode() => _aiQuotaService.LaunchEnvironment("opencode");

    [RelayCommand]
    public void OpenEnvironment(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _aiQuotaService.LaunchEnvironment(id);
        }
    }

    [RelayCommand]
    public async Task RefreshAiQuotas()
    {
        if (Interlocked.Exchange(ref _quotaRefreshInProgress, 1) != 0) return;
        try
        {
            var snapshot = await Task.Run(_aiQuotaService.InspectLocalQuotas, _disposalCts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposalCts.IsCancellationRequested) return;
                AiQuotaSnapshot = snapshot;
                DetectedAiEnvironments.Clear();
                foreach (var env in snapshot.Environments)
                    DetectedAiEnvironments.Add(new AiEnvironmentItem(env, id => OpenEnvironment(id),
                        !(_config.Settings.HiddenAiEnvironmentIds?.Contains(env.Id) ?? false), SetAiEnvironmentVisibility, MoveAiEnvironment));
                ApplyAiEnvironmentOrder();
                OnPropertyChanged(nameof(HasDetectedAiEnvironments));
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.Exchange(ref _quotaRefreshInProgress, 0);
        }
    }

    public void RefreshNetworkDiagnostics()
    {
        try
        {
            var netSnap = NetworkDiagnostics.GetSnapshot();
            LocalIpAddress = netSnap.IpAddress;
            NetworkNameAndType = netSnap.NetworkNameAndType;
            ConnectedNetworkName = netSnap.ConnectedNetworkName;
            NetworkInterfaceText = netSnap.InterfaceNameAndType;
            IsVpnConnected = netSnap.IsVpnActive;
            VpnStatusText = netSnap.VpnStatusText;
        }
        catch { }
    }

    public void RefreshBatteryStatus()
    {
        try
        {
            var batt = _systemAdapter.GetBatteryStatus();
            if (batt.IsAcConnected)
            {
                if (batt.HasBattery && batt.BatteryLifePercent >= 0 && batt.BatteryLifePercent < 100)
                {
                    BatteryAndPowerText = $"Red Eléctrica (AC) - {batt.BatteryLifePercent}%";
                }
                else
                {
                    BatteryAndPowerText = "Red Eléctrica (AC) - 100%";
                }
            }
            else
            {
                if (batt.HasBattery && batt.BatteryLifePercent >= 0)
                {
                    BatteryAndPowerText = $"Batería: {batt.BatteryLifePercent}% (Descargando)";
                }
                else
                {
                    BatteryAndPowerText = "Batería en uso";
                }
            }
        }
        catch
        {
            BatteryAndPowerText = "Red Eléctrica (AC) - 100%";
        }
        PowerSourceStatusText = BatteryAndPowerText;
        OnPropertyChanged(nameof(BatteryAndPowerText));
        OnPropertyChanged(nameof(PowerSourceStatusText));
    }

    // ========================================================
    // --- ESTADO GLOBAL Y TELEMETRÍA DEL FOOTER (5 MÉTRICAS) --
    // ========================================================
    [ObservableProperty]
    private bool _isTaskRunning;

    [ObservableProperty]
    private bool _isKeepAliveActive;

    partial void OnIsTaskRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(EngineStateText));
        OnPropertyChanged(nameof(ManualStartButtonText));
        OnPropertyChanged(nameof(CanStartManualTask));
        OnPropertyChanged(nameof(CanFinishTask));
        StartManualTaskCommand.NotifyCanExecuteChanged();
        FinishTaskCommand.NotifyCanExecuteChanged();
    }

    public string ManualStartButtonText => IsTriggerNow ? "▶ Ejecutar ahora" : "▶ Activar tarea";
    public bool CanStartManualTask => TryBuildManualTask(out _, out _, out _);
    public bool CanFinishManualTask => ActiveRoutines.Any(item => item.IsManual);
    public bool CanFinishTask => IsTaskRunning;

    partial void OnIsKeepAliveActiveChanged(bool value)
    {
        WorkModeStatusText = value || _workflowEngine.GetActiveWorkflows().Any(info => info.KeepAliveActive) ? "Activo (Jitter F15)" : "Inactivo";
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(EngineStateText));
        OnPropertyChanged(nameof(WorkModeStatusBadge));
        OnPropertyChanged(nameof(WorkModeStatusText));
        OnPropertyChanged(nameof(WorkModeDetailText));
    }

    public string StatusDotColor => (IsTaskRunning || IsKeepAliveActive) ? "#00E676" : "#7D8390";
    public string EngineStateText => (IsTaskRunning || IsKeepAliveActive) ? "Ejecutando" : "Inactivo";

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

    // 5 Métricas compactas con ToolTips expandidos
    [ObservableProperty]
    private string _cpuText = "CPU: 0%";

    [ObservableProperty]
    private string _cpuToolTip = "Desglose CPU:\n• Kernel: 0.0%\n• Usuario: 0.0%";

    [ObservableProperty]
    private string _ramText = "RAM: 0%";

    [ObservableProperty]
    private string _ramToolTip = "En uso: 0.0 GB | Libre: 0.0 GB | Total: 0.0 GB";

    [ObservableProperty]
    private string _gpuText = "GPU: 0%";

    [ObservableProperty]
    private string _gpuToolTip = "Adaptador: GPU\nMotor: 3D / Compute";

    [ObservableProperty]
    private string _diskText = "Disco: -- GB / -- GB libres";

    [ObservableProperty]
    private string _diskToolTip = "Espacio en disco del sistema";

    private long? _systemDiskFreeGb;
    public bool IsDiskSpaceLow => DiskSpaceAlert.IsLowSpace(_systemDiskFreeGb, (int)DiskAlertThresholdGb);
    [ObservableProperty]
    private decimal _diskAlertThresholdGb = 15;

    partial void OnDiskAlertThresholdGbChanged(decimal value)
    {
        int threshold = (int)Math.Clamp(decimal.Truncate(value), 0, 1000000);
        if (value != threshold) { DiskAlertThresholdGb = threshold; return; }
        _config.Settings.DiskAlertThresholdGb = threshold;
        _persistence.SaveConfig(_config);
        OnPropertyChanged(nameof(IsDiskSpaceLow));
    }

    [ObservableProperty]
    private string _netText = "Red: 0.0 KB/s";

    [ObservableProperty]
    private string _netToolTip = "Bajada: 0.0 KB/s | Subida: 0.0 KB/s";

    public void RaisePropertyChanged(string propertyName) => OnPropertyChanged(propertyName);

    // ========================================================
    // --- 1. CONTROL MANUAL (ESTILO RS SOMNÍFERO) ------------
    // ========================================================
    public ObservableCollection<string> ManualTriggerOptions { get; } =
    [
        "Cuenta Atrás",
        "Hora Fija del Día",
        "Inactividad de Periféricos",
        "Proceso terminado", "Silencio de audio", "Batería", "Ahora"
    ];

    [ObservableProperty]
    private int _selectedTriggerTypeIndex = 0; // 0=Cuenta Atrás, 1=Hora Fija del Día, 2=Inactividad de Periféricos

    public bool IsTriggerNow => SelectedTriggerTypeIndex == 6;
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
        OnPropertyChanged(nameof(IsTriggerNow));
        OnPropertyChanged(nameof(IsOtherManualCondition));
        OnPropertyChanged(nameof(ManualOtherConditionIndex));
    }

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
    private TimeSpan? _exactTime = DateTime.Now.AddHours(1).TimeOfDay;
    partial void OnExactTimeChanged(TimeSpan? value)
    {
        OnPropertyChanged(nameof(ExactTimeSummaryText));
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
            return $"{(target.Date == now.Date ? "Hoy" : "Mañana")} a las {target:HH:mm} (en {(int)diff.TotalHours}h {diff.Minutes}m), antes del aviso previo.";
        }
    }

    [ObservableProperty]
    private decimal _inactivityMinutes = 15;

    [ObservableProperty]
    private bool _enableCpuThreshold = false;

    [ObservableProperty]
    private decimal _cpuThreshold = 8;

    [ObservableProperty]
    private string _selectedProcessName = "";

    [ObservableProperty]
    private decimal _debounceSeconds = 5;

    [ObservableProperty]
    private decimal _audioSilenceSeconds = 30;

    [ObservableProperty]
    private bool _batteryTriggerOnAcDisconnect = true;

    [ObservableProperty]
    private bool _batteryTriggerOnThreshold = false;

    [ObservableProperty]
    private decimal _batteryThresholdPercent = 20;

    // Acción terminal manual
    [ObservableProperty]
    private int _manualPowerActionIndex = 0; // 0=sin acción de energía

    public IReadOnlyList<string> ManualPowerActionOptions { get; } = new[]
    {
        "Finalizar sin apagar ni suspender",
        "Apagar el PC",
        "Suspender",
        "Hibernar",
        "Reiniciar"
    };

    public int SelectedTerminalActionIndex
    {
        get => ManualPowerActionIndex;
        set => ManualPowerActionIndex = value;
    }

    [ObservableProperty]
    private bool _manualForceClose = false;

    [ObservableProperty] private bool _manualMuteOutput;
    [ObservableProperty] private bool _manualMuteMicrophone;
    [ObservableProperty] private bool _manualPauseMedia;
    [ObservableProperty] private bool _manualTurnOffMonitors;
    [ObservableProperty] private bool _manualLockSession;
    [ObservableProperty] private bool _manualCloseForegroundApps;

    public bool ForceCloseApps
    {
        get => ManualForceClose;
        set
        {
            ManualForceClose = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private bool _manualGracePeriodEnabled = true;

    public bool EnableGraceOverlay
    {
        get => ManualGracePeriodEnabled;
        set
        {
            ManualGracePeriodEnabled = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private decimal _manualGraceSeconds = 60;

    [ObservableProperty]
    private bool _manualAudioFadeEnabled = false;

    public bool EnableWasapiFade
    {
        get => ManualAudioFadeEnabled;
        set
        {
            ManualAudioFadeEnabled = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private bool _manualScreenshotEnabled = false;

    public bool TakeEvidenceScreenshot
    {
        get => ManualScreenshotEnabled;
        set
        {
            ManualScreenshotEnabled = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> AvailableProcesses { get; } = [];

    // ========================================================
    // --- 2. ACCESOS RÁPIDOS ---------------------------------
    // ========================================================
    [ObservableProperty]
    private string _keepAliveButtonText = "▶ Iniciar";

    // ========================================================
    // --- 3. RUTINAS (SUSTITUYE A MODO STUDIO) ---------------
    // ========================================================
    public ObservableCollection<PresetDefinition> SavedPipelines { get; } = [];

    [ObservableProperty]
    private PresetDefinition? _selectedRoutine;

    public bool CanDeleteSelectedRoutine => SelectedRoutine != null && !SelectedRoutine.IsSystemPreset && !_workflowEngine.IsRoutineRunning(SelectedRoutine.Id);

    partial void OnSelectedRoutineChanged(PresetDefinition? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedRoutine));
        DeleteSelectedPipelineCommand.NotifyCanExecuteChanged();
        if (value != null)
        {
            LoadRoutineIntoEditor(value);
            SelectedRoutineItem = RoutineCards.FirstOrDefault(item => item.Id == value.Id);
            RefreshSelectedRoutineCommands();
        }
    }

    [ObservableProperty]
    private string _studioPresetId = "";

    [ObservableProperty]
    private string _studioPresetName = "Nueva Rutina";

    [ObservableProperty]
    private string _studioPresetDescription = "Rutina automatizada secuencial";

    // PASO 1: ¿CUÁNDO EJECUTAR? (Disparador)
    [ObservableProperty]
    private int _studioTriggerTypeIndex = 7; // Process, Countdown, ScheduledTime, UserIdle, AudioSilence, Battery, NetworkIdle, Manual

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
        OnPropertyChanged(nameof(IsStudioTriggerNetworkIdle));
        OnPropertyChanged(nameof(IsStudioTriggerManual));
    }

    // Modo del proceso
    [ObservableProperty]
    private int _studioProcessWatchMode = 0; // 0=Vigilar una ventana abierta, 1=Lanzar y vigilar un archivo

    public bool IsWatchOpenWindow => StudioProcessWatchMode == 0;
    public bool IsLaunchAndWatchFile => StudioProcessWatchMode == 1;

    partial void OnStudioProcessWatchModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsWatchOpenWindow));
        OnPropertyChanged(nameof(IsLaunchAndWatchFile));
    }

    public ObservableCollection<string> ActiveWindowProcesses { get; } = [];

    [ObservableProperty]
    private string _studioSelectedWindowProcess = "";

    [ObservableProperty]
    private string _studioLaunchFilePath = "";

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

    // PASO 2: ¿LUEGO QUÉ HACER? (Acciones intermedias en serie)
    public ObservableCollection<StudioStepItem> StudioPipelineSteps { get; } = [];

    // PASO 3: ¿CÓMO FINALIZAR? (Acción terminal)
    [ObservableProperty]
    private int _studioTerminalActionIndex = 0; // 0=Apagar, 1=Suspender, 2=Hibernar, 3=Reiniciar, 4=Bloquear, 5=Apagar Monitores, 6=Solo finalizar rutina

    [ObservableProperty]
    private decimal _studioGracePeriodSeconds = 60; // 0 a 300s

    [ObservableProperty]
    private bool _studioForceClose = false;

    // Dry-Check (Verificador de Conflictos)
    [ObservableProperty]
    private bool _isRoutineCheckVisible = false;

    [ObservableProperty]
    private string _routineCheckStatusMessage = "";

    [ObservableProperty]
    private string _routineCheckStatusColor = "#00E676";

    // ========================================================
    // --- 4. AJUSTES -----------------------------------------
    // ========================================================
    // IDIOMA
    [ObservableProperty]
    private int _selectedLanguageIndex = 0; // 0=Español, 1=English

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        string lang = value == 1 ? "en" : "es";
        _config.Settings.Language = lang;
        _persistence.SaveConfig(_config);
        App.CurrentInstance?.SetLanguage(lang);
    }

    // APARIENCIA
    [ObservableProperty]
    private int _selectedThemeModeIndex = 3; // 0=Sincronizar con Windows, 1=Automático por Horario, 2=Modo Día, 3=Modo Noche

    public bool IsScheduleThemeActive => SelectedThemeModeIndex == 1;

    partial void OnSelectedThemeModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsScheduleThemeActive));
        string mode = value switch
        {
            0 => "Windows",
            1 => "Schedule",
            2 => "Light",
            3 => "Dark",
            4 => "Slate",
            _ => "Dark"
        };
        _config.Settings.Theme = mode;
        _persistence.SaveConfig(_config);
        App.CurrentInstance?.SetTheme(mode, ScheduleDayTime, ScheduleNightTime);
    }

    [ObservableProperty]
    private TimeSpan _scheduleDayTime = new(8, 0, 0);

    partial void OnScheduleDayTimeChanged(TimeSpan value)
    {
        _config.Settings.ScheduleDayTime = value;
        _persistence.SaveConfig(_config);
        if (IsScheduleThemeActive)
        {
            App.CurrentInstance?.SetTheme("Schedule", value, ScheduleNightTime);
        }
    }

    [ObservableProperty]
    private TimeSpan _scheduleNightTime = new(20, 0, 0);

    partial void OnScheduleNightTimeChanged(TimeSpan value)
    {
        _config.Settings.ScheduleNightTime = value;
        _persistence.SaveConfig(_config);
        if (IsScheduleThemeActive)
        {
            App.CurrentInstance?.SetTheme("Schedule", ScheduleDayTime, value);
        }
    }

    // TELETRABAJO Y ARRANQUE
    [ObservableProperty]
    private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value)
    {
        WindowsStartupHelper.SetStartup(value, StartMinimizedToTray, StartInWorkMode);
    }

    [ObservableProperty]
    private bool _startMinimizedToTray;

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        _config.Settings.StartMinimizedToTray = value;
        _persistence.SaveConfig(_config);
        if (StartWithWindows)
        {
            WindowsStartupHelper.SetStartup(true, value, StartInWorkMode);
        }
    }

    [ObservableProperty]
    private bool _startInWorkMode;

    partial void OnStartInWorkModeChanged(bool value)
    {
        _config.Settings.StartInWorkMode = value;
        _persistence.SaveConfig(_config);
        if (StartWithWindows)
        {
            WindowsStartupHelper.SetStartup(true, StartMinimizedToTray, value);
        }
    }

    // ATAJO GLOBAL DE PÁNICO
    [ObservableProperty]
    private string _panicHotkeyText = "Pause";

    // GUARDIÁN DE BATERÍA (Portátiles)
    [ObservableProperty]
    private bool _batteryProtectionEnabled;

    partial void OnBatteryProtectionEnabledChanged(bool value)
    {
        _config.Settings.BatteryProtectionEnabled = value;
        _persistence.SaveConfig(_config);
    }

    [ObservableProperty]
    private int _batteryThresholdPercentIndex = 1; // 0=5%, 1=10%, 2=15%, 3=20%

    partial void OnBatteryThresholdPercentIndexChanged(int value)
    {
        int pct = value switch
        {
            0 => 5,
            1 => 10,
            2 => 15,
            3 => 20,
            _ => 10
        };
        _config.Settings.BatteryThresholdPercent = pct;
        _persistence.SaveConfig(_config);
    }

    [ObservableProperty]
    private int _batteryActionIndex = 0; // 0=Hibernar, 1=Suspender

    partial void OnBatteryActionIndexChanged(int value)
    {
        string act = value == 1 ? "Sleep" : "Hibernate";
        _config.Settings.BatteryAction = act;
        _persistence.SaveConfig(_config);
    }

    // EVIDENCIAS Y MANTENIMIENTO
    [ObservableProperty]
    private bool _evidenceScreenshotsEnabled = true;

    partial void OnEvidenceScreenshotsEnabledChanged(bool value)
    {
        _config.Settings.EvidenceScreenshotsEnabled = value;
        OnPropertyChanged(nameof(ManualScreenshotStatus));
        RefreshManualPreview();
        _persistence.SaveConfig(_config);
    }

    [ObservableProperty]
    private int _maxEvidenceRetention = 20;

    partial void OnMaxEvidenceRetentionChanged(int value)
    {
        _config.Settings.MaxEvidenceRetention = value;
        _persistence.SaveConfig(_config);
    }

    public ObservableCollection<EvidenceItem> EvidenceGalleryItems { get; } = [];

    [ObservableProperty]
    private bool _isEvidenceGalleryOpen = false;

    // ========================================================
    // --- CONSTRUCTOR E INICIALIZACIÓN -----------------------
    // ========================================================
    public MainViewModel(ISystemAdapter systemAdapter, IWorkflowEngine? engine = null, PersistenceService? persistence = null,
        IAiQuotaService? aiQuotaService = null, IInstalledAppsService? installedAppsService = null)
    {
        _systemAdapter = systemAdapter;
        _aiQuotaService = aiQuotaService ?? new AiQuotaService();
        _persistence = persistence ?? new PersistenceService();
        _workflowEngine = engine ?? new WorkflowEngine(systemAdapter, _persistence);
        _config = _persistence.LoadConfig();
        Config = new HomeDashboardSettings(_config.Settings, () => _persistence.SaveConfig(_config));
        Config.PropertyChanged += (_, e) =>
        {
            RefreshHomeStatusCardLayout();
        };
        HardwareProfile = _systemAdapter.GetHardwareProfile();
        AudioDevices = _systemAdapter.GetAudioDevices();

        // This project exposes keep-alive through the workflow snapshot and the local loop.
        WorkModeStatusText = (_workflowEngine.GetStatusSnapshot().KeepAliveActive || IsKeepAliveActive)
            ? "Activo (Jitter F15)" : "Inactivo";
        RefreshBatteryStatus();
        OnPropertyChanged(nameof(WorkModeStatusText));
        OnPropertyChanged(nameof(WorkModeStatusBadge));
        OnPropertyChanged(nameof(WorkModeDetailText));

        LoadSettingsFromConfig();
        SavedPipelines.CollectionChanged += (_, _) => RefreshRoutineCards();
        LoadRoutinesFromStorage();
        RefreshActiveWindowProcesses();
        RefreshAvailableProcesses();
        RefreshNetworkDiagnostics();

        try
        {
            string? sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
            var drive = new DriveInfo(sysRoot ?? "C:\\");
            long freeGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
            long totalGb = drive.TotalSize / (1024 * 1024 * 1024);
            DiskText = $"Disco: {freeGb} GB / {totalGb} GB libres";
            DiskToolTip = $"Espacio en disco del sistema: {freeGb} GB disponibles de {totalGb} GB totales";
            _systemDiskFreeGb = freeGb;
            OnPropertyChanged(nameof(IsDiskSpaceLow));
        }
        catch { }

        _ = RefreshAiQuotas();
        OnPropertyChanged(nameof(ManualStartButtonText));
        OnPropertyChanged(nameof(CanDeleteSelectedRoutine));
        SubscribeToEngineEvents();
        StartMetricsMonitoring();

        InitializePanicHotkey();
        InitializeAudioControls();
        InitializeRemoteControls();
        InitializeManualControls();
        _ = LoadInstalledAppsAsync(installedAppsService);
    }

    private void LoadSettingsFromConfig()
    {
        SelectedLanguageIndex = _config.Settings.Language == "en" ? 1 : 0;
        SelectedThemeModeIndex = _config.Settings.Theme switch
        {
            "Windows" => 0,
            "Schedule" => 1,
            "Light" => 2,
            "Slate" => 4,
            "Dark" => 3,
            _ => 3
        };
        ScheduleDayTime = _config.Settings.ScheduleDayTime;
        ScheduleNightTime = _config.Settings.ScheduleNightTime;

        StartWithWindows = WindowsStartupHelper.IsStartupEnabled();
        StartMinimizedToTray = _config.Settings.StartMinimizedToTray;
        StartInWorkMode = _config.Settings.StartInWorkMode;

        BatteryProtectionEnabled = _config.Settings.BatteryProtectionEnabled;
        BatteryThresholdPercentIndex = _config.Settings.BatteryThresholdPercent switch
        {
            5 => 0,
            10 => 1,
            15 => 2,
            20 => 3,
            _ => 1
        };
        BatteryActionIndex = _config.Settings.BatteryAction == "Sleep" ? 1 : 0;

        EvidenceScreenshotsEnabled = _config.Settings.EvidenceScreenshotsEnabled;
        MaxEvidenceRetention = _config.Settings.MaxEvidenceRetention;
        DiskAlertThresholdGb = _config.Settings.DiskAlertThresholdGb;
        Config.Reload(_config.Settings);
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        LoadPanicHotkeySettings();
        LoadAudioSettings();
        foreach (var item in DetectedAiEnvironments)
            item.ShowInHome = !(_config.Settings.HiddenAiEnvironmentIds?.Contains(item.Id) ?? false);
        ApplyAiEnvironmentOrder();
    }

    private void SubscribeToEngineEvents()
    {
        _workflowEngine.StatusChanged += HandleEngineStatusChanged;
        
        _workflowEngine.LogMessageReceived += HandleEngineLog;
    }

    private void HandleEngineStatusChanged(SystemStatusState state)
    {
        // Read the current registry on the UI thread, rather than applying an old queued snapshot.
        Dispatcher.UIThread.Post(RefreshActiveRoutines);
    }
    private void HandleEngineLog(string message)
    {
        Debug.WriteLine(message);
    }

    // ========================================================
    // --- TELEMETRÍA DEL FOOTER Y MONITOREO PASIVO -----------
    // ========================================================
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

                    double ramPercent = metrics.RamTotalMb > 0 ? (metrics.RamUsedMb / metrics.RamTotalMb) * 100.0 : 0;
                    double ramUsedGb = metrics.RamUsedMb / 1024.0;
                    double ramTotalGb = metrics.RamTotalMb / 1024.0;
                    double ramFreeGb = Math.Max(0, ramTotalGb - ramUsedGb);

                    Dispatcher.UIThread.Post(() =>
                    {
                        CpuText = $"CPU: {metrics.CpuUsagePercentage:F0}%";
                        CpuToolTip = $"Desglose CPU:\n• Kernel: {metrics.CpuKernelPercentage:F1}%\n• Usuario: {metrics.CpuUserPercentage:F1}%";

                        RamText = $"RAM: {ramPercent:F0}%";
                        RamToolTip = $"En uso: {ramUsedGb:F1} GB | Libre: {ramFreeGb:F1} GB | Total: {ramTotalGb:F1} GB";

                        GpuText = $"GPU: {metrics.GpuUsagePercentage:F0}%";
                        GpuToolTip = $"Adaptadores: {HardwareProfile.GraphicsAdapterName}\nUso de los motores gráficos";

                        DiskText = $"Disco: {metrics.DiskFreeGb} GB / {metrics.DiskTotalGb} GB libres";
                        DiskToolTip = $"Espacio en disco del sistema: {metrics.DiskFreeGb} GB disponibles de {metrics.DiskTotalGb} GB totales";
                        _systemDiskFreeGb = metrics.DiskTotalGb > 0 ? metrics.DiskFreeGb : null;
                        OnPropertyChanged(nameof(IsDiskSpaceLow));
                        UptimeText = FormatUptime();

                        double totalNetKBs = metrics.NetworkDownKBs + metrics.NetworkUpKBs;
                        if (totalNetKBs >= 1024)
                        {
                            NetText = $"Red: {(totalNetKBs / 1024.0):F1} MB/s";
                        }
                        else
                        {
                            NetText = $"Red: {totalNetKBs:F0} KB/s";
                        }
                        NetToolTip = $"Bajada: {metrics.NetworkDownKBs:F1} KB/s | Subida: {metrics.NetworkUpKBs:F1} KB/s";

                        RefreshActiveRoutines();
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
                    });

                    // Monitoreo Guardián de Batería (cada ~30s = 15 ticks)
                    _batteryGuardianTicks++;
                    if (_batteryGuardianTicks >= 15)
                    {
                        _batteryGuardianTicks = 0;
                        EvaluateBatteryGuardian(metrics.Battery);
                    }

                    // Monitoreo de Red Local y VPN (cada ~6s = 3 ticks)
                    _networkTickCounter++;
                    if (_networkTickCounter >= 3)
                    {
                        _networkTickCounter = 0;
                        var netSnap = NetworkDiagnostics.GetSnapshot();
                        var batt = metrics.Battery;
                        Dispatcher.UIThread.Post(() =>
                        {
                            LocalIpAddress = netSnap.IpAddress;
                            NetworkNameAndType = netSnap.NetworkNameAndType;
                            ConnectedNetworkName = netSnap.ConnectedNetworkName;
                            NetworkInterfaceText = netSnap.InterfaceNameAndType;
                            IsVpnConnected = netSnap.IsVpnActive;
                            VpnStatusText = netSnap.VpnStatusText;
                            RefreshAudioPanel(true);

                            RefreshBatteryStatus();
                        });
                    }

                    // Monitoreo Tema Automático (cada ~60s = 30 ticks)
                    _scheduleThemeTicks++;
                    if (_scheduleThemeTicks >= 30)
                    {
                        _scheduleThemeTicks = 0;
                        if (IsScheduleThemeActive)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                App.CurrentInstance?.SetTheme("Schedule", ScheduleDayTime, ScheduleNightTime);
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignora fallos transitorios
                }
            }
        });
    }

    private void EvaluateBatteryGuardian(BatteryStatus? battery)
    {
        if (battery == null || !BatteryProtectionEnabled || !battery.HasBattery || battery.IsOnAcPower || battery.BatteryLifePercent < 0)
        {
            return;
        }

        int threshold = _config.Settings.BatteryThresholdPercent;
        if (battery.BatteryLifePercent <= threshold && !IsTaskRunning)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var action = _config.Settings.BatteryAction == "Sleep" ? PowerAction.Sleep : PowerAction.Hibernate;
                CurrentStatusText = $"Guardián de Batería: Batería crítica ({battery.BatteryLifePercent}%). Activando {action}...";
                try
                {
                    await _systemAdapter.SetPowerStateAsync(action, force: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error al ejecutar acción de batería: {ex.Message}");
                }
            });
        }
    }

    // ========================================================
    // --- RUTINAS (MÉTODOS Y COMANDOS) -----------------------
    // ========================================================
    private static readonly HashSet<string> SystemNoiseProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost", "explorer", "ShellExperienceHost", "ApplicationFrameHost",
        "SystemSettings", "SearchHost", "StartMenuExperienceHost", "Nokto", "taskhostw",
        "dwm", "svchost", "csrss", "services", "lsass", "smss", "fontdrvhost"
    };

    [RelayCommand]
    public void RefreshActiveWindowProcesses()
    {
        try
        {
            var list = Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        return p.MainWindowHandle != IntPtr.Zero
                            && !string.IsNullOrWhiteSpace(p.MainWindowTitle)
                            && !SystemNoiseProcesses.Contains(p.ProcessName);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .Select(p => $"{p.ProcessName}.exe ({p.MainWindowTitle})")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ActiveWindowProcesses.Clear();
            foreach (var item in list)
            {
                ActiveWindowProcesses.Add(item);
            }

            if (ActiveWindowProcesses.Count > 0 && string.IsNullOrWhiteSpace(StudioSelectedWindowProcess))
            {
                StudioSelectedWindowProcess = ActiveWindowProcesses[0];
            }
        }
        catch
        {
        }
    }

    public void RefreshAvailableProcesses()
    {
        try
        {
            AvailableProcesses.Clear();
            var processes = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero && !SystemNoiseProcesses.Contains(p.ProcessName))
                .Select(p => p.ProcessName + ".exe")
                .Distinct()
                .OrderBy(name => name)
                .Take(25);

            foreach (var proc in processes)
            {
                AvailableProcesses.Add(proc);
            }

            if (!AvailableProcesses.Contains(SelectedProcessName)) SelectedProcessName = "";
        }
        catch
        {
        }
    }

    private void LoadRoutinesFromStorage()
    {
        SavedPipelines.Clear();
        var presetsFile = _persistence.LoadPresets();
        foreach (var p in presetsFile.Presets)
        {
            SavedPipelines.Add(p);
        }

        if (SavedPipelines.Count > 0)
        {
            SelectedRoutine = SavedPipelines[0];
        }
    }

    [RelayCommand]
    public void NewStudioPipeline()
    {
        SelectedRoutine = null;
        SelectedRoutineItem = null;
        StudioPresetId = "routine_" + Guid.NewGuid().ToString("N")[..8];
        StudioPresetName = "Nueva Rutina";
        StudioPresetDescription = "Rutina personalizada";
        StudioTriggerTypeIndex = 7;
        StudioProcessWatchMode = 0;
        StudioSelectedWindowProcess = ActiveWindowProcesses.FirstOrDefault() ?? "notepad.exe";
        StudioLaunchFilePath = "";
        StudioPipelineSteps.Clear();
        StudioTerminalActionIndex = 6;
        StudioGracePeriodSeconds = 60;
        StudioForceClose = false;
        StudioRepeatSchedule = true;
        foreach (var day in StudioWeekdays) day.IsSelected = true;
        IsRoutineCheckVisible = false;
    }

    private void LoadRoutineIntoEditor(PresetDefinition preset)
    {
        StudioPresetId = preset.Id;
        StudioPresetName = preset.Name;
        StudioPresetDescription = preset.DisplayDescription;
        IsRoutineCheckVisible = false;

        // Disparador
        foreach (var day in StudioWeekdays) day.IsSelected = true;
        StudioRepeatSchedule = true;
        if (preset.Trigger != null)
        {
            switch (preset.Trigger.Type)
            {
                case TriggerType.ProcessExit:
                    StudioTriggerTypeIndex = 0;
                    if (preset.Trigger.Parameters != null)
                    {
                        if (preset.Trigger.Parameters.TryGetValue("launchFilePath", out var pPath))
                        {
                            StudioLaunchFilePath = pPath.GetString() ?? "";
                            StudioProcessWatchMode = !string.IsNullOrWhiteSpace(StudioLaunchFilePath) ? 1 : 0;
                        }
                        else
                        {
                            StudioProcessWatchMode = 0;
                        }

                        if (preset.Trigger.Parameters.TryGetValue("processName", out var pProc))
                        {
                            string raw = pProc.GetString() ?? "";
                            var match = ActiveWindowProcesses.FirstOrDefault(a => a.StartsWith(raw, StringComparison.OrdinalIgnoreCase));
                            StudioSelectedWindowProcess = match ?? raw;
                        }
                        if (preset.Trigger.Parameters.TryGetValue("debounceSeconds", out var pDebounce) && pDebounce.TryGetDecimal(out var dVal))
                        {
                            StudioProcessDebounceSeconds = dVal;
                        }
                    }
                    break;

                case TriggerType.Countdown:
                    if (preset.Trigger.Parameters != null && preset.Trigger.Parameters.TryGetValue("isFixedTime", out var pIsFixed) && pIsFixed.GetBoolean())
                    {
                        StudioTriggerTypeIndex = 2; // Hora Fija
                        LoadScheduleFields(preset.Trigger);
                        if (preset.Trigger.Parameters.TryGetValue("timeOfDay", out var pTime) && TimeSpan.TryParse(pTime.GetString(), out var ts))
                        {
                            StudioExactTime = ts;
                        }
                    }
                    else
                    {
                        StudioTriggerTypeIndex = 1; // Cuenta Atrás
                        if (preset.Trigger.Parameters != null && preset.Trigger.Parameters.TryGetValue("durationSeconds", out var pDur) && pDur.TryGetInt32(out var sec))
                        {
                            StudioCountdownHours = sec / 3600;
                            StudioCountdownMinutes = (sec % 3600) / 60;
                            StudioCountdownSeconds = sec % 60;
                        }
                    }
                    break;

                case TriggerType.UserIdle:
                    StudioTriggerTypeIndex = 3;
                    if (preset.Trigger.Parameters != null && preset.Trigger.Parameters.TryGetValue("idleMinutes", out var pIdle) && pIdle.TryGetDecimal(out var idleVal))
                    {
                        StudioInactivityMinutes = idleVal;
                    }
                    else
                    {
                        StudioInactivityMinutes = 3m;
                    }
                    break;

                case TriggerType.Schedule:
                case TriggerType.FixedTime:
                case TriggerType.ScheduledTime:
                    StudioTriggerTypeIndex = 2; // Hora Fija
                    LoadScheduleFields(preset.Trigger);
                    break;

                case TriggerType.AudioSilence:
                    StudioTriggerTypeIndex = 4;
                    if (preset.Trigger.Parameters != null)
                    {
                        if (preset.Trigger.Parameters.TryGetValue("silenceThresholdSeconds", out var pSil) && pSil.TryGetDecimal(out var silVal))
                            StudioAudioSilenceSeconds = silVal;
                        if (preset.Trigger.Parameters.TryGetValue("thresholdPeak", out var pPeak) && pPeak.TryGetDecimal(out var peakVal))
                            StudioAudioSilencePeak = peakVal;
                    }
                    break;

                case TriggerType.BatteryState:
                    StudioTriggerTypeIndex = 5;
                    if (preset.Trigger.Parameters != null)
                    {
                        if (preset.Trigger.Parameters.TryGetValue("onAcDisconnect", out var pAc))
                            StudioBatteryOnAcDisconnect = pAc.GetBoolean();
                        if (preset.Trigger.Parameters.TryGetValue("batteryLevelThreshold", out var pBat) && pBat.TryGetDecimal(out var batVal))
                        {
                            StudioBatteryThresholdPercent = batVal;
                            StudioBatteryOnThreshold = batVal > 0;
                        }
                    }
                    break;

                case TriggerType.NetworkThroughput:
                case TriggerType.NetworkIdle:
                    StudioTriggerTypeIndex = 6;
                    if (preset.Trigger.Parameters?.TryGetValue("thresholdKBs", out var threshold) == true) StudioNetworkThresholdKBs = threshold.GetDecimal();
                    if (preset.Trigger.Parameters?.TryGetValue("durationSeconds", out var duration) == true) StudioNetworkIdleSeconds = duration.GetDecimal();
                    break;
                default:
                    StudioTriggerTypeIndex = 7;
                    break;
            }
        }

        StudioPipelineSteps.Clear();
        foreach (var definition in WorkflowDefinition.GetActions(preset))
        {
            var step = StudioStepItem.FromDefinition(definition);
            AttachStepCallbacks(step);
            StudioPipelineSteps.Add(step);
        }
        RenumberStudioSteps();
        StudioTerminalActionIndex = 6;
    }
    [RelayCommand]
    public async Task BrowseStudioLaunchFileAsync()
    {
        if (RequestFilePicker != null)
        {
            var path = await RequestFilePicker();
            if (!string.IsNullOrWhiteSpace(path))
            {
                StudioLaunchFilePath = path;
                StudioProcessWatchMode = 1;
            }
        }
    }

    [RelayCommand]
    public void AddStudioStep(string? actionTypeStr)
    {
        var actionType = actionTypeStr switch
        {
            "Screenshot" => ActionType.CaptureScreenshot,
            "AudioFade" => ActionType.AudioFadeOut,
            "MediaControl" => ActionType.MediaControl,
            "WaitDelay" => ActionType.WaitDelay,
            "Command" => ActionType.ExecuteCommand,
            "LaunchApp" => ActionType.LaunchApp,
            "AudioConfig" => ActionType.AudioConfig,
            "PowerAction" => ActionType.PowerAction,
            "MonitorsOff" => ActionType.TurnOffMonitors,
            "LockWorkstation" => ActionType.LockWorkstation,
            "KeepAlive" => ActionType.KeepAliveEngine,
            _ => ActionType.CaptureScreenshot
        };

        var step = new StudioStepItem
        {
            StepOrder = StudioPipelineSteps.Count + 1,
            ActionType = actionType,
            IgnoreFailure = false
        };
        AttachStepCallbacks(step);
        StudioPipelineSteps.Add(step);
        RenumberStudioSteps();
    }

    private void AttachStepCallbacks(StudioStepItem step)
    {
        step.MoveUpRequested = MoveStudioStepUp;
        step.MoveDownRequested = MoveStudioStepDown;
        step.RemoveRequested = RemoveStudioStep;
        step.BrowseFileRequested = () => RequestFilePicker?.Invoke() ?? Task.FromResult<string?>(null);
        step.SetInstalledApps(InstalledApps);
    }

    public void RemoveStudioStep(StudioStepItem step)
    {
        if (StudioPipelineSteps.Remove(step))
        {
            RenumberStudioSteps();
        }
    }

    public void MoveStudioStepUp(StudioStepItem step)
    {
        int index = StudioPipelineSteps.IndexOf(step);
        if (index > 0)
        {
            StudioPipelineSteps.Move(index, index - 1);
            RenumberStudioSteps();
        }
    }

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
    public void VerifyRoutine()
    {
        try
        {
            BuildPresetFromStudio();
            RoutineCheckStatusMessage = StudioPipelineSteps.Any(step => step.IsPowerAction)
                ? "Rutina válida. Contiene una acción explícita de energía / sesión; revisa su aviso previo antes de iniciarla."
                : "Rutina válida. Al finalizar, el equipo continuará encendido.";
            RoutineCheckStatusColor = "#00E676";
            IsRoutineCheckVisible = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException) { ShowRoutineValidationError(ex); }
    }
    [RelayCommand]
    public void SaveCurrentStudioPipeline()
    {
        PresetDefinition preset;
        try { preset = BuildPresetFromStudio(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException) { ShowRoutineValidationError(ex); return; }
        int existingIndex = -1;
        for (int i = 0; i < SavedPipelines.Count; i++)
        {
            if (SavedPipelines[i].Id == preset.Id)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            SavedPipelines[existingIndex] = preset;
        }
        else
        {
            SavedPipelines.Add(preset);
        }

        _persistence.SavePresets(new PresetsFile { Presets = [.. SavedPipelines] });
        SelectedRoutine = preset;
        CurrentStatusText = $"Rutina '{preset.Name}' guardada con éxito.";
    }

    [RelayCommand(CanExecute = nameof(CanStartSelectedRoutine), AllowConcurrentExecutions = true)]
    public async Task RunCurrentStudioPipelineAsync()
    {
        PresetDefinition preset;
        try { preset = BuildPresetFromStudio(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException) { ShowRoutineValidationError(ex); return; }
        StudioPresetId = preset.Id;
        CurrentStatusText = $"Iniciando rutina: {preset.Name}...";
        await RunRoutineSafelyAsync(preset);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedRoutine))]
    public void DeleteSelectedPipeline()
    {
        if (SelectedRoutine == null || SelectedRoutine.IsSystemPreset) return;

        var toRemove = SelectedRoutine;
        SavedPipelines.Remove(toRemove);
        _persistence.SavePresets(new PresetsFile { Presets = [.. SavedPipelines] });

        if (SavedPipelines.Count == 0)
        {
            RestoreFactoryRoutines();
        }
        else
        {
            SelectedRoutine = SavedPipelines.FirstOrDefault();
        }
        CurrentStatusText = $"Rutina '{toRemove.Name}' eliminada.";
    }

    [RelayCommand]
    public void RestoreFactoryRoutines()
    {
        string? selectedId = SelectedRoutine?.Id;
        var restored = Presets.RestoreFactoryPresets(SavedPipelines);
        SavedPipelines.Clear();
        foreach (var preset in restored) SavedPipelines.Add(preset);
        _persistence.SavePresets(new PresetsFile { Presets = [.. SavedPipelines] });
        SelectedRoutine = SavedPipelines.FirstOrDefault(p => p.Id == selectedId) ?? SavedPipelines.FirstOrDefault();
        CurrentStatusText = "Rutinas base de fábrica restauradas.";
    }

    public PresetDefinition BuildPresetFromStudio(string? overrideId = null)
    {
        ValidateLinearRoutine();
        string presetId = overrideId ?? (string.IsNullOrWhiteSpace(StudioPresetId) ? "routine_" + Guid.NewGuid().ToString("N")[..8] : StudioPresetId);
        var parameters = new Dictionary<string, JsonElement>();
        TriggerType type;
        void Set(string key, object value) => parameters[key] = JsonSerializer.SerializeToElement(value);
        switch (StudioTriggerTypeIndex)
        {
            case 0:
                type = TriggerType.ProcessExit;
                string process = StudioProcessWatchMode == 1 ? Path.GetFileName(StudioLaunchFilePath) : StudioSelectedWindowProcess.Split(' ')[0];
                if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process += ".exe";
                Set("processName", process);
                Set("debounceSeconds", (int)StudioProcessDebounceSeconds);
                if (StudioProcessWatchMode == 1) Set("launchFilePath", StudioLaunchFilePath);
                break;
            case 1:
                type = TriggerType.Countdown;
                decimal seconds = (StudioCountdownHours ?? 0) * 3600 + (StudioCountdownMinutes ?? 0) * 60 + (StudioCountdownSeconds ?? 0);
                if (seconds < 0 || seconds > int.MaxValue) throw new InvalidOperationException("Cuenta atrás fuera del rango permitido.");
                Set("durationSeconds", (int)seconds);
                break;
            case 2:
                type = TriggerType.ScheduledTime;
                if (StudioExactTime is null) throw new InvalidOperationException("Selecciona una hora válida.");
                Set("timeOfDay", StudioExactTime.Value.ToString(@"hh\:mm"));
                Set("daysOfWeek", StudioWeekdays.Where(day => day.IsSelected).Select(day => day.Day).ToArray());
                Set("repeat", StudioRepeatSchedule);
                RoutineSchedule.NextOccurrence(new RoutineTrigger { Type = type, Parameters = parameters }, DateTimeOffset.Now, TimeZoneInfo.Local);
                break;
            case 3:
                type = TriggerType.UserIdle;
                Set("idleMinutes", (int)Math.Max(1, StudioInactivityMinutes));
                break;
            case 4:
                type = TriggerType.AudioSilence;
                Set("silenceThresholdSeconds", (int)Math.Max(1, StudioAudioSilenceSeconds));
                Set("thresholdPeak", (double)StudioAudioSilencePeak);
                break;
            case 5:
                type = TriggerType.BatteryState;
                Set("onAcDisconnect", StudioBatteryOnAcDisconnect);
                Set("batteryLevelThreshold", StudioBatteryOnThreshold ? (int)StudioBatteryThresholdPercent : 0);
                break;
            case 6:
                type = TriggerType.NetworkIdle;
                Set("thresholdKBs", (double)Math.Max(0, StudioNetworkThresholdKBs));
                Set("durationSeconds", (int)Math.Max(1, StudioNetworkIdleSeconds));
                break;
            default:
                type = TriggerType.Manual;
                break;
        }
        return new PresetDefinition
        {
            Id = presetId,
            Name = string.IsNullOrWhiteSpace(StudioPresetName) ? "Rutina Personalizada" : StudioPresetName.Trim(),
            Description = StudioPresetDescription,
            Icon = SelectedRoutine?.Icon ?? "Moon",
            IsFavorite = SelectedRoutine?.IsFavorite ?? false,
            IsSystemPreset = SelectedRoutine?.Id == presetId && SelectedRoutine.IsSystemPreset,
            Trigger = new RoutineTrigger { Type = type, Parameters = parameters.Count == 0 ? null : parameters },
            Actions = new ObservableCollection<WorkflowActionItem>(StudioPipelineSteps.Select(step => WorkflowDefinition.Copy(step.ToDefinition()))),
            TerminalAction = new TerminalActionDefinition { Type = TerminalActionType.None }
        };
    }
    // ========================================================
    // --- CONTROL MANUAL: ACCIONES DE EJECUCIÓN --------------
    // ========================================================
    [RelayCommand(CanExecute = nameof(CanStartManualTask), AllowConcurrentExecutions = true)]
    public async Task StartManualTaskAsync()
    {
        if (!TryBuildManualTask(out var preset, out _, out var error))
        {
            CurrentStatusText = error;
            return;
        }
        CurrentStatusText = "Activando tarea manual...";
        await RunRoutineSafelyAsync(preset!);
    }

    private TriggerDefinition BuildCountdownTrigger()
    {
        int seconds = (int)((CountdownHours ?? 0) * 3600 + (CountdownMinutes ?? 0) * 60 + (CountdownSeconds ?? 0));
        return new() { Type = TriggerType.Countdown, Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(seconds) } };
    }

    private TriggerDefinition BuildFixedTimeTrigger()
    {
        DateTime now = DateTime.Now;
        TimeSpan time = ExactTime ?? now.TimeOfDay.Add(TimeSpan.FromHours(1));
        DateTime target = now.Date + time;
        if (target <= now) target = target.AddDays(1);
        int seconds = Math.Max(1, (int)(target - now).TotalSeconds);
        return new() { Type = TriggerType.Countdown, Parameters = new()
        {
            ["durationSeconds"] = JsonSerializer.SerializeToElement(seconds),
            ["isFixedTime"] = JsonSerializer.SerializeToElement(true),
            ["timeOfDay"] = JsonSerializer.SerializeToElement(time.ToString())
        } };
    }

    [RelayCommand]
    public void SetQuickCountdown(string minutesStr)
    {
        if (int.TryParse(minutesStr, out int m))
        {
            CountdownHours = m / 60;
            CountdownMinutes = m % 60;
            CountdownSeconds = 0;
            SelectedTriggerTypeIndex = 0;
        }
    }

    [RelayCommand]
    public void AddCountdownMinutes(string minutesStr)
    {
        if (int.TryParse(minutesStr, out int delta))
        {
            int current = (int)((CountdownHours ?? 0) * 60 + (CountdownMinutes ?? 0));
            int next = Math.Clamp(current + delta, 0, 99 * 60 + 59);
            CountdownHours = next / 60;
            CountdownMinutes = next % 60;
            SelectedTriggerTypeIndex = 0;
        }
    }

    [RelayCommand]
    public void ResetCountdown()
    {
        CountdownHours = 0;
        CountdownMinutes = 0;
        CountdownSeconds = 0;
        SelectedTriggerTypeIndex = 0;
    }

    // ========================================================
    // --- ACCESOS RÁPIDOS (PRESETS DE UN CLIC) ---------------
    // ========================================================
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
        _keepAliveCts = new CancellationTokenSource();
        IsKeepAliveActive = true;
        KeepAliveButtonText = "⏸ Pausar";
        CurrentStatusText = "Mantener equipo activo";

        Task.Run(async () =>
        {
            try
            {
                await _systemAdapter.RunKeepAliveLoopAsync(
                    KeepAliveMode.Mixed,
                    30,
                    90,
                    msg => Debug.WriteLine($"[KeepAlive] {msg}"),
                    _keepAliveCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsKeepAliveActive = false;
                    KeepAliveButtonText = "▶ Iniciar";
                    CurrentStatusText = "Listo";
                });
            }
        });
    }

    private void StopKeepAlive()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _keepAliveCts = null;
        IsKeepAliveActive = false;
        KeepAliveButtonText = "▶ Iniciar";
        CurrentStatusText = "Listo";
    }

    [RelayCommand]
    public async Task RunQuickSleepModeAsync()
    {
        var preset = new PresetDefinition
        {
            Id = "quick_sleep",
            Name = "Modo Dormir",
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
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(30),
                        ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(0)
                    }
                },
                new PipelineStepDefinition
                {
                    StepOrder = 2,
                    ActionType = ActionType.MediaControl
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

        CurrentStatusText = "Modo Dormir activado (45 min)...";
        await RunRoutineSafelyAsync(preset);
    }

    [RelayCommand]
    public async Task RunQuickRenderModeAsync()
    {
        var preset = new PresetDefinition
        {
            Id = "quick_render",
            Name = "Modo Render",
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
                    ActionType = ActionType.CaptureScreenshot
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

        CurrentStatusText = $"Modo Render activado: vigilando '{SelectedProcessName}'...";
        await RunRoutineSafelyAsync(preset);
    }

    [RelayCommand]
    public async Task RunQuickDownloadModeAsync()
    {
        var preset = new PresetDefinition
        {
            Id = "quick_download",
            Name = "Modo Descargas",
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.NetworkThroughput,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["thresholdKBs"] = JsonSerializer.SerializeToElement(50.0),
                    ["durationSeconds"] = JsonSerializer.SerializeToElement(60)
                }
            },
            Pipeline =
            [
                new PipelineStepDefinition
                {
                    StepOrder = 1,
                    ActionType = ActionType.TurnOffMonitors
                }
            ],
            TerminalAction = new TerminalActionDefinition
            {
                Type = TerminalActionType.Sleep,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(30),
                    ["forced"] = JsonSerializer.SerializeToElement(false)
                }
            }
        };

        CurrentStatusText = "Modo Descargas activado: vigilando tráfico de red...";
        await RunRoutineSafelyAsync(preset);
    }

    [RelayCommand(CanExecute = nameof(CanFinishTask))]
    public void FinishTask()
    {
        _workflowEngine.FinishAll();
        RefreshActiveRoutines();
    }
    [RelayCommand]
    public void PostponeTask(string minutesStr)
    {
        int minutes = int.TryParse(minutesStr, out int m) ? m : 10;
        if (_graceRoutineId is not null)
            _workflowEngine.PostponeRoutine(_graceRoutineId, TimeSpan.FromMinutes(minutes));
        else _workflowEngine.Postpone(TimeSpan.FromMinutes(minutes));
        RequestGraceOverlay?.Invoke(false, 0);
        CurrentStatusText = $"Pospuesto +{minutes} min.";
    }

    [RelayCommand]
    public async Task TurnOffMonitorsNow()
    {
        await _systemAdapter.SetDisplayPowerAsync(false);
    }

    // ========================================================
    // --- AJUSTES Y EVIDENCIAS: ACCIONES Y MANTENIMIENTO -----
    // ========================================================
    [RelayCommand]
    public void OpenEvidenceGallery()
    {
        EvidenceGalleryItems.Clear();
        string snapDir = _persistence.Storage.SnapshotsDirectory;
        if (Directory.Exists(snapDir))
        {
            var files = Directory.GetFiles(snapDir, "*.bmp")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc);

            foreach (var file in files)
            {
                EvidenceGalleryItems.Add(new EvidenceItem
                {
                    FilePath = file.FullName,
                    FileName = file.Name,
                    TimestampText = file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    FileSizeText = $"{file.Length / 1024.0 / 1024.0:F1} MB"
                });
            }
        }
        IsEvidenceGalleryOpen = true;
    }

    [RelayCommand]
    public void CloseEvidenceGallery()
    {
        IsEvidenceGalleryOpen = false;
    }

    [RelayCommand]
    public void CleanEvidence()
    {
        string snapDir = _persistence.Storage.SnapshotsDirectory;
        if (Directory.Exists(snapDir))
        {
            foreach (var f in Directory.GetFiles(snapDir, "*.bmp"))
            {
                try
                {
                    File.Delete(f);
                }
                catch { }
            }
        }
        EvidenceGalleryItems.Clear();
        CurrentStatusText = "Galería de evidencias purgada.";
    }

    [RelayCommand]
    public void OpenDataFolder()
    {
        string dataPath = _persistence.Storage.DataDirectory;
        if (Directory.Exists(dataPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dataPath,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    public void ResetSettings()
    {
        _panicHotkeyService?.Stop();
        _micMuteHotkeyService?.Stop();
        _audioMuteHotkeyService?.Stop();
        _config.Settings = new AppSettings();
        _persistence.SaveConfig(_config);
        LoadSettingsFromConfig();
        App.CurrentInstance?.SetLanguage("es");
        App.CurrentInstance?.SetTheme("Dark", ScheduleDayTime, ScheduleNightTime);
        CurrentStatusText = "Ajustes restablecidos a valores por defecto.";
    }

    [RelayCommand]
    private Task LockSessionNow() => _systemAdapter.SetPowerStateAsync(PowerAction.LockStation);

    public void Dispose()
    {
        DisposeRoutineEditor();
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        _panicHotkeyService?.Dispose();
        DisposeAudioControls();
        DisposeRemoteControls();
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _metricsTimer?.Dispose();
        _workflowEngine.Dispose();
    }
}
