using System.Diagnostics;
using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Core.Persistence;

namespace Nokto.Core.Engine;

/// <summary>
/// Máquina de estados determinista y ejecutor de flujos encadenados de Nokto.
/// Totalmente reactivo, sin busy-waiting, compatible con AOT.
/// </summary>
internal sealed class WorkflowRunner : IDisposable
{
    private readonly ISystemAdapter _systemAdapter;
    private readonly PersistenceService _persistence;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _activeWorkflowCts;
    private PresetDefinition? _activePreset;
    private EngineState _currentState = EngineState.Idle;
    private string? _currentPhase;
    private double _progressPercentage;
    private int _timeRemainingSeconds;
    private bool _gracePeriodActive;
    private int _gracePeriodRemainingSeconds;
    private MonitoredProcessInfo? _monitoredProcess;
    private string? _lastGeneratedSnapshotPath;
    private Stopwatch? _executionStopwatch;
    private bool _isDisposed;
    private DateOnly? _lastScheduledDate;

    public EngineState CurrentState
    {
        get { lock (_stateLock) return _currentState; }
        private set { lock (_stateLock) _currentState = value; }
    }

    public bool IsDryRunMode
    {
        get => _systemAdapter.IsDryRunMode;
        set => _systemAdapter.IsDryRunMode = value;
    }

    public event Action<SystemStatusState>? StatusChanged;
    public event Action<string>? LogMessageReceived;
    public event Action<int>? GracePeriodTick;

    public WorkflowRunner(ISystemAdapter systemAdapter, PersistenceService persistence)
    {
        _systemAdapter = systemAdapter;
        _persistence = persistence;
    }

    public SystemStatusState GetStatusSnapshot()
    {
        lock (_stateLock)
        {
            var metrics = _systemAdapter.GetCurrentMetrics();
            return new SystemStatusState
            {
                Timestamp = DateTimeOffset.UtcNow,
                EngineState = _currentState,
                ActivePresetId = _activePreset?.Id,
                ActivePresetName = _activePreset?.Name,
                CurrentPhase = _currentPhase,
                ProgressPercentage = Math.Round(_progressPercentage, 1),
                TimeRemainingSeconds = _timeRemainingSeconds,
                GracePeriodActive = _gracePeriodActive,
                GracePeriodRemainingSeconds = _gracePeriodRemainingSeconds,
                Metrics = metrics,
                MonitoredProcess = _monitoredProcess,
                KeepAliveActive = _currentPhase == "KeepAlive"
            };
        }
    }

    private void NotifyStateChanged()
    {
        var snapshot = GetStatusSnapshot();
        StatusChanged?.Invoke(snapshot);
    }

    private void Log(string message)
    {
        LogMessageReceived?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public async Task StartPresetAsync(PresetDefinition preset, CancellationToken cancellationToken = default)
    {
        _activeWorkflowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _activeWorkflowCts.Token;

        _activePreset = preset;
        _executionStopwatch = Stopwatch.StartNew();
        _lastGeneratedSnapshotPath = null;
        _timeRemainingSeconds = preset.Trigger.Type == TriggerType.Countdown
            ? GetIntParam(preset.Trigger.Parameters, "durationSeconds", 60) : 0;

        Log($"Iniciando flujo: '{preset.Name}' (ID: {preset.Id})");

        try
        {
            bool recurring = preset.Trigger.Type == TriggerType.ScheduledTime && GetBoolParam(preset.Trigger.Parameters, "repeat", true);
            do
            {
            // 1. FASE DE EVALUACIÓN DE DISPARADOR
            CurrentState = EngineState.WaitingTrigger;
            _currentPhase = "TriggerEvaluation";
            NotifyStateChanged();

            await EvaluateTriggerAsync(preset.Trigger, ct);
            ct.ThrowIfCancellationRequested();

            // A manual multi-action plan can show its grace notice before any selected action.
            int graceSeconds = ExtractGraceSeconds(preset.TerminalAction);
            bool graceBeforeActions = GetBoolParam(preset.TerminalAction.Parameters, "graceBeforeActions", false);
            if (preset.Actions is null && graceBeforeActions && graceSeconds > 0)
                await RunGracePeriodAsync(graceSeconds, ct);

            // 2. FASE DE EJECUCIÓN DE ACCIONES INTERMEDIAS
            CurrentState = EngineState.ExecutingActions;
            _currentPhase = "PipelineExecution";
            NotifyStateChanged();

            await ExecutePipelineAsync(preset.Actions is null ? preset.Pipeline : preset.Actions.Cast<PipelineStepDefinition>().ToList(), ct,
                preserveOrder: preset.Actions is not null);
            ct.ThrowIfCancellationRequested();

            // 3. FASE DE PERIODO DE GRACIA (SI APLICA)
            if (preset.Actions is null && !graceBeforeActions && graceSeconds > 0 && preset.TerminalAction.Type != TerminalActionType.None)
                await RunGracePeriodAsync(graceSeconds, ct);

            // 4. ACCIÓN TERMINAL
            _currentPhase = "TerminalAction";
            NotifyStateChanged();
            ct.ThrowIfCancellationRequested();
            if (preset.Actions is null) await ExecuteTerminalActionAsync(preset.TerminalAction, ct);

            CurrentState = EngineState.Completed;
            _currentPhase = "Completed";
            NotifyStateChanged();

            RecordAudit(preset.Id, "Success", (int)_executionStopwatch.Elapsed.TotalSeconds,
                $"Trigger:{preset.Trigger.Type}", preset.TerminalAction.Type.ToString(),
                _lastGeneratedSnapshotPath, "All pipeline steps completed gracefully.");

            Log($"Flujo '{preset.Name}' completado exitosamente.");
            }
            while (recurring && !ct.IsCancellationRequested);
            ct.ThrowIfCancellationRequested();
            _executionStopwatch.Stop();
        }
        catch (OperationCanceledException)
        {
            CurrentState = EngineState.Idle;
            _currentPhase = "Finalized";
            NotifyStateChanged();

            _executionStopwatch?.Stop();
            int elapsed = (int)(_executionStopwatch?.Elapsed.TotalSeconds ?? 0);
            RecordAudit(preset.Id, "Finalized", elapsed, $"Trigger:{preset.Trigger.Type}", "None", null, "Operación finalizada por el usuario o timeout.");
            Log($"Flujo '{preset.Name}' finalizado por el usuario.");
        }
        catch (Exception ex)
        {
            CurrentState = EngineState.Failed;
            _currentPhase = "Failed";
            NotifyStateChanged();

            _executionStopwatch?.Stop();
            int elapsed = (int)(_executionStopwatch?.Elapsed.TotalSeconds ?? 0);
            RecordAudit(preset.Id, "Failed", elapsed, $"Trigger:{preset.Trigger.Type}", "None", null, $"Error: {ex.Message}");
            Log($"[ERROR EN FLUJO]: {ex.Message}");
            throw;
        }
        finally
        {
            _activeWorkflowCts?.Dispose();
            _activeWorkflowCts = null;
            _gracePeriodActive = false;
            _gracePeriodRemainingSeconds = 0;
            _monitoredProcess = null;
        }
    }

    public Task StartQuickCountdownAsync(string title, TimeSpan duration, TerminalActionType terminalAction, CancellationToken cancellationToken = default)
    {
        var quickPreset = new PresetDefinition
        {
            Id = "preset_quick_" + Guid.NewGuid().ToString("N")[..8],
            Name = title,
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.Countdown,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["durationSeconds"] = JsonSerializer.SerializeToElement((int)duration.TotalSeconds)
                }
            },
            Pipeline = [],
            TerminalAction = new TerminalActionDefinition
            {
                Type = terminalAction,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(30)
                }
            }
        };

        return StartPresetAsync(quickPreset, cancellationToken);
    }

    private async Task EvaluateTriggerAsync(TriggerDefinition trigger, CancellationToken ct)
    {
        Log($"Evaluando disparador: {trigger.Type}");

        switch (trigger.Type)
        {
            case TriggerType.Manual:
                ct.ThrowIfCancellationRequested();
                break;
            case TriggerType.Countdown:
                int durationSeconds = GetIntParam(trigger.Parameters, "durationSeconds", 60);
                await RunCountdownAsync(durationSeconds, ct);
                break;

            case TriggerType.ProcessExit:
                string processName = GetStringParam(trigger.Parameters, "processName", "notepad.exe");
                int debounceSeconds = GetIntParam(trigger.Parameters, "debounceSeconds", 5);
                string launchFilePath = GetStringParam(trigger.Parameters, "launchFilePath", "");
                await WaitForProcessExitAsync(processName, debounceSeconds, launchFilePath, ct);
                break;

            case TriggerType.SustainedLoad:
                double threshold = GetDoubleParam(trigger.Parameters, "thresholdPercentage", 8.0);
                int durationSec = GetIntParam(trigger.Parameters, "durationSeconds", 60);
                await WaitForSustainedLoadAsync(threshold, durationSec, ct);
                break;

            case TriggerType.NetworkThroughput:
            case TriggerType.NetworkIdle:
                double netThresholdKBs = GetDoubleParam(trigger.Parameters, "thresholdKBs", 50.0);
                int netDurationSec = GetIntParam(trigger.Parameters, "durationSeconds", 60);
                await WaitForNetworkThroughputAsync(netThresholdKBs, netDurationSec, ct);
                break;

            case TriggerType.UserIdle:
                int idleMinutes = GetIntParam(trigger.Parameters, "idleMinutes", 15);
                await WaitForUserIdleAsync(idleMinutes, ct);
                break;

            case TriggerType.AudioSilence:
                int silenceThresholdSec = GetIntParam(trigger.Parameters, "silenceThresholdSeconds", 30);
                double thresholdPeak = GetDoubleParam(trigger.Parameters, "thresholdPeak", 0.001);
                await WaitForAudioSilenceAsync(silenceThresholdSec, (float)thresholdPeak, ct);
                break;

            case TriggerType.BatteryState:
                bool onAcDisconnect = GetBoolParam(trigger.Parameters, "onAcDisconnect", true);
                int batteryLevelThreshold = GetIntParam(trigger.Parameters, "batteryLevelThreshold", 0);
                await WaitForBatteryStateAsync(onAcDisconnect, batteryLevelThreshold, ct);
                break;

            case TriggerType.FixedTime:
            case TriggerType.Schedule:
            case TriggerType.ScheduledTime:
                await WaitForScheduledTimeAsync(trigger, ct);
                break;
            default:
                // Countdown de seguridad si no se especifican parámetros
                await RunCountdownAsync(10, ct);
                break;
        }

        Log($"Disparador {trigger.Type} satisfecho.");
    }

    private async Task WaitForScheduledTimeAsync(TriggerDefinition trigger, CancellationToken ct)
    {
        DateTimeOffset target = RoutineSchedule.NextOccurrence(trigger, DateTimeOffset.Now, TimeZoneInfo.Local, _lastScheduledDate);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (DateTimeOffset.Now < target)
        {
            _timeRemainingSeconds = (int)Math.Ceiling((target - DateTimeOffset.Now).TotalSeconds);
            _progressPercentage = 0;
            NotifyStateChanged();
            await timer.WaitForNextTickAsync(ct);
        }
        ct.ThrowIfCancellationRequested();
        _lastScheduledDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(target, TimeZoneInfo.Local).DateTime);
    }

    private async Task RunCountdownAsync(int seconds, CancellationToken ct)
    {
        _timeRemainingSeconds = seconds;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        int total = seconds;
        while (_timeRemainingSeconds > 0)
        {
            total = Math.Max(total, _timeRemainingSeconds);
            _progressPercentage = Math.Clamp(((double)(total - _timeRemainingSeconds) / total) * 100.0, 0, 100);
            NotifyStateChanged();

            await timer.WaitForNextTickAsync(ct);
            Interlocked.Decrement(ref _timeRemainingSeconds);
        }

        _timeRemainingSeconds = 0;
        _progressPercentage = 100.0;
        NotifyStateChanged();
    }

    private static bool IsProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private async Task WaitForProcessExitAsync(string processName, int debounceSeconds, string launchFilePath, CancellationToken ct)
    {
        string cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(launchFilePath) && File.Exists(launchFilePath))
        {
            try
            {
                if (!IsProcessRunning(cleanName))
                {
                    using var launched = Process.Start(new ProcessStartInfo
                    {
                        FileName = launchFilePath,
                        UseShellExecute = true
                    });
                    Log($"Archivo lanzado con éxito: '{launchFilePath}'");
                    await Task.Delay(2000, ct); // Dar tiempo al proceso para arrancar
                }
            }
            catch (Exception ex)
            {
                Log($"Error al lanzar archivo '{launchFilePath}': {ex.Message}");
            }
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        bool observedRunning = false;
        int absenceCounter = 0;

        _monitoredProcess = new MonitoredProcessInfo
        {
            Name = processName,
            IsRunning = true,
            LastSeenSecondsAgo = 0
        };

        Log($"Esperando a que el proceso '{processName}' finalice (Debounce: {debounceSeconds}s)...");

        while (!ct.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(ct);

            bool isRunning = IsProcessRunning(cleanName);

            if (isRunning)
            {
                observedRunning = true;
                absenceCounter = 0;
                _monitoredProcess = new MonitoredProcessInfo
                {
                    Name = processName,
                    IsRunning = true,
                    LastSeenSecondsAgo = 0
                };
            }
            else
            {
                if (observedRunning)
                {
                    absenceCounter += 2;
                    _monitoredProcess = new MonitoredProcessInfo
                    {
                        Name = processName,
                        IsRunning = false,
                        LastSeenSecondsAgo = absenceCounter
                    };

                    Log($"Proceso '{cleanName}' ausente durante {absenceCounter}/{debounceSeconds}s...");

                    if (absenceCounter >= debounceSeconds)
                    {
                        Log($"Confirmado cierre del proceso '{processName}' tras periodo de debounce.");
                        break;
                    }
                }
                else
                {
                    // Si el proceso aún no ha arrancado, seguimos esperando
                    Log($"Esperando arranque inicial de '{cleanName}'...");
                }
            }

            NotifyStateChanged();
        }
    }

    private async Task WaitForSustainedLoadAsync(double thresholdPercentage, int durationSeconds, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        int sustainedSeconds = 0;

        Log($"Supervisando carga sostenida < {thresholdPercentage}% durante {durationSeconds}s...");

        while (!ct.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(ct);

            var metrics = _systemAdapter.GetCurrentMetrics();
            if (metrics.CpuUsagePercentage <= thresholdPercentage)
            {
                sustainedSeconds += 2;
                _progressPercentage = Math.Clamp(((double)sustainedSeconds / durationSeconds) * 100.0, 0, 100);
                if (sustainedSeconds >= durationSeconds)
                {
                    Log($"Carga de CPU sostenida bajo {thresholdPercentage}% durante {durationSeconds}s cumplida.");
                    break;
                }
            }
            else
            {
                sustainedSeconds = 0;
                _progressPercentage = 0;
            }

            NotifyStateChanged();
        }
    }

    private async Task WaitForNetworkThroughputAsync(double thresholdKBs, int durationSeconds, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        int sustainedSeconds = 0;

        while (!ct.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(ct);

            var metrics = _systemAdapter.GetCurrentMetrics();
            double combinedSpeed = metrics.NetworkDownKBs + metrics.NetworkUpKBs;

            if (combinedSpeed <= thresholdKBs)
            {
                sustainedSeconds += 2;
                _progressPercentage = Math.Clamp(((double)sustainedSeconds / durationSeconds) * 100.0, 0, 100);
                if (sustainedSeconds >= durationSeconds)
                {
                    Log($"Tráfico de red sostenido bajo {thresholdKBs} KB/s cumplido.");
                    break;
                }
            }
            else
            {
                sustainedSeconds = 0;
                _progressPercentage = 0;
            }

            NotifyStateChanged();
        }
    }

    private async Task WaitForUserIdleAsync(int idleMinutes, CancellationToken ct)
    {
        int targetSeconds = idleMinutes * 60;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (!ct.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(ct);

            var metrics = _systemAdapter.GetCurrentMetrics();
            if (metrics.UserIdleSeconds >= targetSeconds)
            {
                Log($"Inactividad de usuario alcanzada: {metrics.UserIdleSeconds}s >= {targetSeconds}s.");
                break;
            }

            _progressPercentage = Math.Clamp(((double)metrics.UserIdleSeconds / targetSeconds) * 100.0, 0, 100);
            NotifyStateChanged();
        }
    }

    private async Task WaitForAudioSilenceAsync(int silenceThresholdSeconds, float thresholdPeak, CancellationToken ct)
    {
        int continuousSilenceSeconds = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _timeRemainingSeconds = silenceThresholdSeconds;

        while (!ct.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(ct);

            float peak = _systemAdapter.GetMasterPeakValue();

            if (peak <= thresholdPeak)
            {
                continuousSilenceSeconds++;
                Log($"Silencio de audio detectado: {continuousSilenceSeconds}/{silenceThresholdSeconds}s (Pico: {peak:F4})");
            }
            else
            {
                if (continuousSilenceSeconds > 0)
                {
                    Log($"Sonido detectado (Pico: {peak:F4}). Reiniciando contador de silencio.");
                }
                continuousSilenceSeconds = 0;
            }

            _timeRemainingSeconds = Math.Max(0, silenceThresholdSeconds - continuousSilenceSeconds);
            _progressPercentage = Math.Clamp(((double)continuousSilenceSeconds / silenceThresholdSeconds) * 100.0, 0, 100);
            NotifyStateChanged();

            if (continuousSilenceSeconds >= silenceThresholdSeconds)
            {
                Log($"Umbral de silencio de audio alcanzado: {silenceThresholdSeconds}s continuos.");
                break;
            }
        }
    }

    private async Task WaitForBatteryStateAsync(bool onAcDisconnect, int batteryLevelThreshold, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (!ct.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(ct);

            var status = _systemAdapter.GetBatteryStatus();

            bool conditionMet = false;
            string reason = "";

            if (onAcDisconnect && !status.IsOnAcPower)
            {
                conditionMet = true;
                reason = "Desconexión de corriente alterna (AC Offline / En Batería)";
            }
            else if (batteryLevelThreshold > 0 && status.BatteryLifePercent >= 0 && status.BatteryLifePercent <= batteryLevelThreshold)
            {
                conditionMet = true;
                reason = $"Nivel de batería crítico ({status.BatteryLifePercent}% <= {batteryLevelThreshold}%)";
            }

            if (conditionMet)
            {
                Log($"Disparador de batería satisfecho: {reason}.");
                _progressPercentage = 100.0;
                _timeRemainingSeconds = 0;
                NotifyStateChanged();
                break;
            }

            if (status.BatteryLifePercent >= 0)
            {
                _progressPercentage = status.BatteryLifePercent;
                _timeRemainingSeconds = status.BatteryLifeSecondsRemaining > 0 ? status.BatteryLifeSecondsRemaining : 0;
            }
            else
            {
                _progressPercentage = 0;
                _timeRemainingSeconds = 0;
            }

            NotifyStateChanged();
        }
    }

    private async Task ExecutePipelineAsync(List<PipelineStepDefinition> pipeline, CancellationToken ct, bool preserveOrder = false)
    {
        var sortedSteps = preserveOrder ? pipeline : pipeline.OrderBy(s => s.StepOrder).ToList();

        for (int i = 0; i < sortedSteps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = sortedSteps[i];

            Log($"Ejecutando paso {step.StepOrder}: {step.ActionType}");

            try
            {
                switch (step.ActionType)
                {
                    case ActionType.LaunchApp:
                        string path = GetStringParam(step.Parameters, "executablePath", "");
                        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Selecciona una aplicación o ruta para ejecutar.");
                        bool skipRunning = GetBoolParam(step.Parameters, "skipIfAlreadyRunning", true);
                        if (skipRunning && _systemAdapter.IsAppRunning(path))
                        {
                            Log($"Programa ya abierto; se omite: {path}");
                            break;
                        }
                        if (!Enum.TryParse<AppLaunchMode>(GetStringParam(step.Parameters, "launchMode", "Normal"), true, out var launchMode) || !Enum.IsDefined(launchMode))
                            throw new InvalidOperationException("Modo de apertura no válido.");
                        await _systemAdapter.LaunchAppAsync(new(path, GetStringParam(step.Parameters, "arguments", ""),
                            GetStringParam(step.Parameters, "workingDirectory", ""), launchMode, skipRunning), ct);
                        break;
                    case ActionType.AudioConfig:
                        int volume = GetIntParam(step.Parameters, "volumePercent", 50);
                        if (volume is < 0 or > 100) throw new InvalidOperationException("El volumen debe estar entre 0 y 100.");
                        await _systemAdapter.SetMasterVolumeAsync(volume / 100f, ct);
                        await _systemAdapter.SetMuteAsync(GetBoolParam(step.Parameters, "muteOutput", false), ct);
                        if (!_systemAdapter.SetInputMute(GetBoolParam(step.Parameters, "muteMicrophone", false)))
                            throw new InvalidOperationException("Micrófono no disponible para configurar el silencio.");
                        break;
                    case ActionType.PowerAction:
                        if (!Enum.TryParse<PowerAction>(GetStringParam(step.Parameters, "powerAction", ""), true, out var power) || !Enum.IsDefined(power))
                            throw new InvalidOperationException("Acción de energía no válida.");
                        int powerGrace = Math.Clamp(GetIntParam(step.Parameters, "gracePeriodSeconds", 30), 0, 300);
                        if (powerGrace > 0) await RunGracePeriodAsync(powerGrace, ct);
                        ct.ThrowIfCancellationRequested();
                        await _systemAdapter.SetPowerStateAsync(power, GetBoolParam(step.Parameters, "forced", false), ct);
                        CurrentState = EngineState.ExecutingActions;
                        _currentPhase = "PipelineExecution";
                        NotifyStateChanged();
                        break;
                    case ActionType.CaptureScreenshot:
                        await ExecuteScreenshotStepAsync(step, ct);
                        break;

                    case ActionType.AudioFadeOut:
                        int fadeDuration = GetIntParam(step.Parameters, "durationSeconds", 15);
                        int targetVolPct = GetIntParam(step.Parameters, "targetVolumePercentage", 0);
                        await _systemAdapter.SetMasterVolumeFadeAsync(targetVolPct / 100f, TimeSpan.FromSeconds(fadeDuration), ct);
                        break;

                    case ActionType.MuteAudio:
                        bool muted = GetBoolParam(step.Parameters, "muted", true);
                        await _systemAdapter.SetMuteAsync(muted, ct);
                        break;

                    case ActionType.MuteMicrophone:
                        if (_systemAdapter.GetInputMute() != true) _systemAdapter.SetInputMute(true);
                        break;

                    case ActionType.LockWorkstation:
                        await _systemAdapter.SetPowerStateAsync(PowerAction.LockStation, cancellationToken: ct);
                        break;

                    case ActionType.CloseForegroundApplications:
                        _systemAdapter.CloseForegroundApplication();
                        break;

                    case ActionType.CloseSelectedApplications:
                        var targets = step.Parameters is not null && step.Parameters.TryGetValue("targets", out var targetJson)
                            ? targetJson.Deserialize(Nokto.Core.Serialization.NoktoJsonContext.Default.ApplicationCloseTargetArray) ?? [] : [];
                        await _systemAdapter.CloseApplicationsAsync(targets,
                            GetBoolParam(step.Parameters, "foregroundAtExecution", false), ct);
                        break;

                    case ActionType.TurnOffMonitors:
                        await _systemAdapter.SetDisplayPowerAsync(false, ct);
                        break;

                    case ActionType.ExecuteCommand:
                        await ExecuteCommandStepAsync(step, ct);
                        break;

                    case ActionType.MediaControl:
                        _systemAdapter.SendMediaControl(pauseOnly: GetBoolParam(step.Parameters, "pauseOnly", true));
                        break;

                    case ActionType.KeepAliveEngine:
                        double hours = GetDoubleParam(step.Parameters, "delayHours", GetDoubleParam(_activePreset?.TerminalAction.Parameters, "delayHours", 0));
                        if (hours > 0)
                        {
                            _currentPhase = "KeepAlive";
                            NotifyStateChanged();
                            var remaining = TimeSpan.FromHours(hours) - (_activePreset?.Actions is null ? _executionStopwatch?.Elapsed ?? TimeSpan.Zero : TimeSpan.Zero);
                            if (remaining > TimeSpan.Zero)
                            {
                                using var durationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                durationCts.CancelAfter(remaining);
                                try
                                {
                                    await _systemAdapter.RunKeepAliveLoopAsync(KeepAliveMode.Mixed, 30, 60, Log, durationCts.Token);
                                }
                                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                                ct.ThrowIfCancellationRequested();
                            }
                        }
                        else _systemAdapter.SimulateKeepAlivePulse(KeepAliveMode.Mixed);
                        break;

                    case ActionType.WaitDelay:
                        int waitSeconds = GetIntParam(step.Parameters, "delaySeconds", GetIntParam(step.Parameters, "durationSeconds", 10));
                        if (waitSeconds is < 0 or > 86400) throw new InvalidOperationException("La espera debe estar entre 0 y 86400 segundos.");
                        Log($"Paso {step.StepOrder}: Esperando {waitSeconds} segundos...");
                        await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
                        break;
                    default:
                        throw new NotSupportedException($"Acción no disponible: {step.ActionType}.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log($"Error en paso {step.StepOrder} ({step.ActionType}): {ex.Message}");
                if (!step.IgnoreFailure)
                {
                    throw; // Finaliza inmediatamente el pipeline y el apagado
                }
            }
        }
    }

    private async Task RunGracePeriodAsync(int graceSeconds, CancellationToken ct)
    {
        CurrentState = EngineState.GracePeriod;
        _currentPhase = "GracePeriod";
        _gracePeriodActive = true;
        _gracePeriodRemainingSeconds = graceSeconds;
        NotifyStateChanged();
        await RunGracePeriodCountdownAsync(graceSeconds, ct);
    }

    private async Task ExecuteScreenshotStepAsync(PipelineStepDefinition step, CancellationToken ct)
    {
        var config = _persistence.LoadConfig();
        if (!config.Settings.EvidenceScreenshotsEnabled)
        {
            Log("Capturas de evidencia desactivadas en Ajustes.");
            return;
        }

        byte[] imageBytes = await _systemAdapter.CaptureScreenAsync(stampMetadata: true, label: _activePreset?.Name, cancellationToken: ct);
        if (imageBytes.Length > 0)
        {
            string outputDir = _persistence.Storage.SnapshotsDirectory;
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
            string fileName = $"snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bmp";
            string fullPath = Path.Combine(outputDir, fileName);

            await File.WriteAllBytesAsync(fullPath, imageBytes, ct);
            _lastGeneratedSnapshotPath = fullPath;
            Log($"Captura guardada en: {fullPath}");

            // Purga automática si supera MaxEvidenceRetention (default: 20)
            try
            {
                var files = Directory.GetFiles(outputDir, "*.bmp")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                int maxRetention = Math.Max(1, config.Settings.MaxEvidenceRetention);
                if (files.Count > maxRetention)
                {
                    foreach (var oldFile in files.Skip(maxRetention))
                    {
                        oldFile.Delete();
                    }
                }
            }
            catch
            {
            }
        }
    }

    private static async Task ExecuteCommandStepAsync(PipelineStepDefinition step, CancellationToken ct)
    {
        string executable = GetStringParam(step.Parameters, "executablePath", "cmd.exe");
        string arguments = GetStringParam(step.Parameters, "arguments", "");
        int timeoutSeconds = GetIntParam(step.Parameters, "timeoutSeconds", 30);
        int expectedExitCode = GetIntParam(step.Parameters, "expectedExitCode", 0);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await process.WaitForExitAsync(linked.Token);

        if (process.ExitCode != expectedExitCode)
        {
            throw new InvalidOperationException($"El comando '{executable}' finalizó con código de salida {process.ExitCode} (Esperado: {expectedExitCode}).");
        }
    }

    private async Task RunGracePeriodCountdownAsync(int seconds, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        Log($"Periodo de gracia iniciado ({seconds}s). Presione Esc para cancelar.");

        _gracePeriodRemainingSeconds = seconds;
        _timeRemainingSeconds = seconds;

        while (_gracePeriodRemainingSeconds > 0)
        {
            _timeRemainingSeconds = _gracePeriodRemainingSeconds;
            _progressPercentage = 100.0;
            GracePeriodTick?.Invoke(_gracePeriodRemainingSeconds);
            NotifyStateChanged();

            await timer.WaitForNextTickAsync(ct);
            Interlocked.Decrement(ref _gracePeriodRemainingSeconds);
        }

        _gracePeriodActive = false;
        _gracePeriodRemainingSeconds = 0;
        NotifyStateChanged();
    }

    private async Task ExecuteTerminalActionAsync(TerminalActionDefinition terminalAction, CancellationToken ct)
    {
        bool force = GetBoolParam(terminalAction.Parameters, "forced", false);
        Log($"Ejecutando acción terminal: {terminalAction.Type} (Force: {force})");

        if (IsDryRunMode)
        {
            switch (terminalAction.Type)
            {
                case TerminalActionType.Shutdown:
                case TerminalActionType.Sleep:
                case TerminalActionType.Hibernate:
                case TerminalActionType.Restart:
                    string dryRunMsg = $"[DRY-RUN] Acción de energía simulada con éxito: {terminalAction.Type} (Forzado: {force})";
                    Console.WriteLine(dryRunMsg);
                    Log(dryRunMsg);
                    await _systemAdapter.SetPowerStateAsync(PowerAction.Shutdown, force, ct);
                    return;
            }
        }

        switch (terminalAction.Type)
        {
            case TerminalActionType.Shutdown:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Shutdown, force, ct);
                break;

            case TerminalActionType.Sleep:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Sleep, force, ct);
                break;

            case TerminalActionType.Hibernate:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Hibernate, force, ct);
                break;

            case TerminalActionType.Restart:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Restart, force, ct);
                break;

            case TerminalActionType.LockStation:
                await _systemAdapter.SetPowerStateAsync(PowerAction.LockStation, force, ct);
                break;

            case TerminalActionType.Logoff:
                await _systemAdapter.SetPowerStateAsync(PowerAction.Logoff, force, ct);
                break;

            case TerminalActionType.None:
            default:
                Log("Acción terminal 'None': el sistema no cambiará de estado.");
                break;
        }
    }

    private void RecordAudit(string presetId, string status, int duration, string triggerFired, string terminalAction, string? snapshot, string notes)
    {
        try
        {
            string finalNotes = notes;
            if (IsDryRunMode && (terminalAction == "Shutdown" || terminalAction == "Sleep" || terminalAction == "Hibernate" || terminalAction == "Restart"))
            {
                finalNotes = $"[DRY-RUN] Acción de energía simulada con éxito: {terminalAction} (Forzado: False)";
            }

            var entry = new AuditLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                PresetId = presetId,
                Status = status,
                ExecutionDurationSeconds = duration,
                TriggerFired = triggerFired,
                TerminalActionExecuted = terminalAction,
                SnapshotFile = snapshot,
                ExitNotes = finalNotes
            };

            _persistence.AppendAuditLog(entry);
        }
        catch
        {
            // Silently prevent audit logging failures from crashing engine
        }
    }

    private static int ExtractGraceSeconds(TerminalActionDefinition action)
    {
        return GetIntParam(action.Parameters, "gracePeriodSeconds", 60);
    }

    public void Finish()
    {
        if (_activeWorkflowCts != null)
        {
            _activeWorkflowCts.Cancel();
        }

        CurrentState = EngineState.Idle;
        _currentPhase = null;
        _progressPercentage = 0;
        _timeRemainingSeconds = 0;
        _gracePeriodActive = false;
        _gracePeriodRemainingSeconds = 0;
        _monitoredProcess = null;

        NotifyStateChanged();
    }

    public void Postpone(TimeSpan extraTime)
    {
        int extraSec = (int)extraTime.TotalSeconds;
        Interlocked.Add(ref _timeRemainingSeconds, extraSec);
        if (_gracePeriodActive)
        {
            Interlocked.Add(ref _gracePeriodRemainingSeconds, extraSec);
        }
        Log($"Flujo pospuesto +{extraTime.TotalMinutes} minutos.");
        NotifyStateChanged();
    }

    public void Pause()
    {
        if (CurrentState != EngineState.Idle && CurrentState != EngineState.Completed && CurrentState != EngineState.Failed)
        {
            CurrentState = EngineState.Paused;
            NotifyStateChanged();
        }
    }

    public void Resume()
    {
        if (CurrentState == EngineState.Paused)
        {
            CurrentState = EngineState.WaitingTrigger;
            NotifyStateChanged();
        }
    }

    private static int GetIntParam(Dictionary<string, JsonElement>? dict, string key, int defaultValue)
    {
        if (dict != null && dict.TryGetValue(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int val)) return val;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out int sVal)) return sVal;
        }
        return defaultValue;
    }

    private static double GetDoubleParam(Dictionary<string, JsonElement>? dict, string key, double defaultValue)
    {
        if (dict != null && dict.TryGetValue(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double val)) return val;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out double sVal)) return sVal;
        }
        return defaultValue;
    }

    private static string GetStringParam(Dictionary<string, JsonElement>? dict, string key, string defaultValue)
    {
        if (dict != null && dict.TryGetValue(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? defaultValue;
            return el.ToString();
        }
        return defaultValue;
    }

    private static bool GetBoolParam(Dictionary<string, JsonElement>? dict, string key, bool defaultValue)
    {
        if (dict != null && dict.TryGetValue(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out bool b)) return b;
        }
        return defaultValue;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Finish();
            _isDisposed = true;
        }
    }
}
