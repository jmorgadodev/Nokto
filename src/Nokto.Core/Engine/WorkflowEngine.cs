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
public sealed class WorkflowEngine : IWorkflowEngine
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

    public WorkflowEngine(ISystemAdapter systemAdapter, PersistenceService persistence)
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
                KeepAliveActive = _currentState == EngineState.WaitingTrigger && _activePreset?.Pipeline.Any(p => p.ActionType == ActionType.KeepAliveEngine) == true
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
        Abort();

        _activeWorkflowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _activeWorkflowCts.Token;

        _activePreset = preset;
        _executionStopwatch = Stopwatch.StartNew();
        _lastGeneratedSnapshotPath = null;

        Log($"Iniciando flujo: '{preset.Name}' (ID: {preset.Id})");

        try
        {
            // 1. FASE DE EVALUACIÓN DE DISPARADOR
            CurrentState = EngineState.WaitingTrigger;
            _currentPhase = "TriggerEvaluation";
            NotifyStateChanged();

            await EvaluateTriggerAsync(preset.Trigger, ct);

            // 2. FASE DE EJECUCIÓN DE ACCIONES INTERMEDIAS
            CurrentState = EngineState.ExecutingActions;
            _currentPhase = "PipelineExecution";
            NotifyStateChanged();

            await ExecutePipelineAsync(preset.Pipeline, ct);

            // 3. FASE DE PERIODO DE GRACIA (SI APLICA)
            int graceSeconds = ExtractGraceSeconds(preset.TerminalAction);
            if (graceSeconds > 0 && preset.TerminalAction.Type != TerminalActionType.None)
            {
                CurrentState = EngineState.GracePeriod;
                _currentPhase = "GracePeriod";
                _gracePeriodActive = true;
                _gracePeriodRemainingSeconds = graceSeconds;
                NotifyStateChanged();

                await RunGracePeriodCountdownAsync(graceSeconds, ct);
            }

            // 4. ACCIÓN TERMINAL
            _currentPhase = "TerminalAction";
            NotifyStateChanged();

            await ExecuteTerminalActionAsync(preset.TerminalAction, ct);

            CurrentState = EngineState.Completed;
            _currentPhase = "Completed";
            NotifyStateChanged();

            _executionStopwatch.Stop();
            RecordAudit(preset.Id, "Success", (int)_executionStopwatch.Elapsed.TotalSeconds,
                $"Trigger:{preset.Trigger.Type}", preset.TerminalAction.Type.ToString(),
                _lastGeneratedSnapshotPath, "All pipeline steps completed gracefully.");

            Log($"Flujo '{preset.Name}' completado exitosamente.");
        }
        catch (OperationCanceledException)
        {
            CurrentState = EngineState.Idle;
            _currentPhase = "Aborted";
            NotifyStateChanged();

            _executionStopwatch?.Stop();
            int elapsed = (int)(_executionStopwatch?.Elapsed.TotalSeconds ?? 0);
            RecordAudit(preset.Id, "Aborted", elapsed, $"Trigger:{preset.Trigger.Type}", "None", null, "Operación abortada por el usuario o timeout.");
            Log($"Flujo '{preset.Name}' abortado por el usuario.");
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
            case TriggerType.Countdown:
                int durationSeconds = GetIntParam(trigger.Parameters, "durationSeconds", 60);
                await RunCountdownAsync(durationSeconds, ct);
                break;

            case TriggerType.ProcessExit:
                string processName = GetStringParam(trigger.Parameters, "processName", "notepad.exe");
                int debounceSeconds = GetIntParam(trigger.Parameters, "debounceSeconds", 5);
                await WaitForProcessExitAsync(processName, debounceSeconds, ct);
                break;

            case TriggerType.SustainedLoad:
                double threshold = GetDoubleParam(trigger.Parameters, "thresholdPercentage", 8.0);
                int durationSec = GetIntParam(trigger.Parameters, "durationSeconds", 60);
                await WaitForSustainedLoadAsync(threshold, durationSec, ct);
                break;

            case TriggerType.NetworkThroughput:
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
            default:
                // Countdown de seguridad si no se especifican parámetros
                await RunCountdownAsync(10, ct);
                break;
        }

        Log($"Disparador {trigger.Type} satisfecho.");
    }

    private async Task RunCountdownAsync(int seconds, CancellationToken ct)
    {
        _timeRemainingSeconds = seconds;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        int total = seconds;
        for (int i = seconds; i > 0; i--)
        {
            _timeRemainingSeconds = i;
            _progressPercentage = Math.Clamp(((double)(total - i) / total) * 100.0, 0, 100);
            NotifyStateChanged();

            await timer.WaitForNextTickAsync(ct);
        }

        _timeRemainingSeconds = 0;
        _progressPercentage = 100.0;
        NotifyStateChanged();
    }

    private async Task WaitForProcessExitAsync(string processName, int debounceSeconds, CancellationToken ct)
    {
        string cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
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

            bool isRunning = Process.GetProcessesByName(cleanName).Length > 0;

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

    private async Task ExecutePipelineAsync(List<PipelineStepDefinition> pipeline, CancellationToken ct)
    {
        var sortedSteps = pipeline.OrderBy(s => s.StepOrder).ToList();

        for (int i = 0; i < sortedSteps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = sortedSteps[i];

            Log($"Ejecutando paso {step.StepOrder}: {step.ActionType}");

            try
            {
                switch (step.ActionType)
                {
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

                    case ActionType.TurnOffMonitors:
                        await _systemAdapter.SetDisplayPowerAsync(false, ct);
                        break;

                    case ActionType.ExecuteCommand:
                        await ExecuteCommandStepAsync(step, ct);
                        break;

                    case ActionType.KeepAliveEngine:
                        _systemAdapter.SimulateKeepAlivePulse(KeepAliveMode.Mixed);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Error en paso {step.StepOrder} ({step.ActionType}): {ex.Message}");
                if (!step.IgnoreFailure)
                {
                    throw; // Aborta inmediatamente el pipeline y el apagado
                }
            }
        }
    }

    private async Task ExecuteScreenshotStepAsync(PipelineStepDefinition step, CancellationToken ct)
    {
        byte[] imageBytes = await _systemAdapter.CaptureScreenAsync(stampMetadata: true, label: _activePreset?.Name, cancellationToken: ct);
        if (imageBytes.Length > 0)
        {
            string outputDir = _persistence.Storage.SnapshotsDirectory;
            string fileName = $"snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bmp";
            string fullPath = Path.Combine(outputDir, fileName);

            await File.WriteAllBytesAsync(fullPath, imageBytes, ct);
            _lastGeneratedSnapshotPath = fullPath;
            Log($"Captura guardada en: {fullPath}");
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

        for (int i = seconds; i > 0; i--)
        {
            _gracePeriodRemainingSeconds = i;
            _timeRemainingSeconds = i;
            _progressPercentage = 100.0;
            GracePeriodTick?.Invoke(i);
            NotifyStateChanged();

            await timer.WaitForNextTickAsync(ct);
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

    public void Abort()
    {
        if (_activeWorkflowCts != null)
        {
            _activeWorkflowCts.Cancel();
            _activeWorkflowCts.Dispose();
            _activeWorkflowCts = null;
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
        _timeRemainingSeconds += extraSec;
        if (_gracePeriodActive)
        {
            _gracePeriodRemainingSeconds += extraSec;
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
            Abort();
            _isDisposed = true;
        }
    }
}
