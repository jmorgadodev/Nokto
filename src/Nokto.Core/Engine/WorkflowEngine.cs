using System.Collections.Concurrent;
using System.Diagnostics;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Core.Persistence;

namespace Nokto.Core.Engine;

/// <summary>Coordinates independent executions; shared system actions remain local to Windows.</summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly ISystemAdapter _systemAdapter;
    private readonly PersistenceService _persistence;
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentDictionary<string, RunningWorkflowContext> _activeWorkflows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, RunningWorkflowContext> _executions = new();
    private SystemStatusState _lastStatus = new();
    private bool _isDisposed;

    private sealed class RunningWorkflowContext(PresetDefinition routine, WorkflowRunner runner, CancellationTokenSource cancellation)
    {
        public Guid ExecutionId { get; } = Guid.NewGuid();
        public PresetDefinition Routine { get; } = routine;
        public WorkflowRunner Runner { get; } = runner;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public Stopwatch Elapsed { get; } = Stopwatch.StartNew();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SystemStatusState Status = new()
        {
            ActivePresetId = routine.Id, ActivePresetName = routine.Name,
            EngineState = EngineState.WaitingTrigger, CurrentPhase = "TriggerEvaluation",
            TimeRemainingSeconds = routine.Trigger.Type == TriggerType.Countdown
                ? routine.Trigger.Parameters?.TryGetValue("durationSeconds", out var duration) == true && duration.ValueKind == System.Text.Json.JsonValueKind.Number && duration.TryGetInt32(out var seconds) ? seconds : 60
                : 0
        };
    }

    public WorkflowEngine(ISystemAdapter systemAdapter, PersistenceService persistence)
    {
        _systemAdapter = systemAdapter;
        _persistence = persistence;
    }

    public EngineState CurrentState => GetStatusSnapshot().EngineState;
    public bool IsDryRunMode { get => _systemAdapter.IsDryRunMode; set => _systemAdapter.IsDryRunMode = value; }
    public event Action<SystemStatusState>? StatusChanged;
    public event Action<string>? LogMessageReceived;
    public event Action<int>? GracePeriodTick;

    public Task StartPresetAsync(PresetDefinition preset, CancellationToken cancellationToken = default) => StartRoutine(preset, cancellationToken);

    // Repeated starts of the same ID join the existing execution, never run it twice.
    public Task StartRoutine(PresetDefinition routine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routine);
        ArgumentException.ThrowIfNullOrWhiteSpace(routine.Id);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        RunningWorkflowContext context;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_activeWorkflows.TryGetValue(routine.Id, out var existing)) return existing.Completion.Task;
            var frozen = routine with
            {
                Trigger = routine.Trigger with { Parameters = CopyParameters(routine.Trigger.Parameters) },
                Actions = routine.Actions is null ? null : new(routine.Actions.Select(WorkflowDefinition.Copy)),
                Pipeline = routine.Pipeline.Select(s => s with { Parameters = CopyParameters(s.Parameters) }).ToList(),
                TerminalAction = routine.TerminalAction with { Parameters = CopyParameters(routine.TerminalAction.Parameters) }
            };
            var runner = new WorkflowRunner(_systemAdapter, _persistence);
            context = new(frozen, runner, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _activeWorkflows.TryAdd(frozen.Id, context);
            _executions.TryAdd(context.ExecutionId, context);
            runner.LogMessageReceived += message => LogMessageReceived?.Invoke($"[{frozen.Name}] {message}");
            runner.StatusChanged += status =>
            {
                Volatile.Write(ref context.Status, status);
                if (IsCurrent(context)) NotifyStateChanged();
            };
            runner.GracePeriodTick += seconds => { if (IsCurrent(context)) GracePeriodTick?.Invoke(seconds); };
            _ = Task.Run(() => RunRoutineAsync(context));
        }
        NotifyStateChanged();
        return context.Completion.Task;
    }

    private static Dictionary<string, System.Text.Json.JsonElement>? CopyParameters(Dictionary<string, System.Text.Json.JsonElement>? values) =>
        values?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

    private bool IsCurrent(RunningWorkflowContext context) =>
        _activeWorkflows.TryGetValue(context.Routine.Id, out var current) && ReferenceEquals(current, context);

    private async Task RunRoutineAsync(RunningWorkflowContext context)
    {
        Exception? error = null;
        try { await context.Runner.StartPresetAsync(context.Routine, context.Cancellation.Token); }
        catch (Exception ex) { error = ex; }
        finally
        {
            context.Elapsed.Stop();
            lock (_lifecycleLock)
            {
                // A stopped execution may finish after a replacement with the same ID starts.
                if (IsCurrent(context))
                {
                    _activeWorkflows.TryRemove(context.Routine.Id, out _);
                    Volatile.Write(ref _lastStatus, Volatile.Read(ref context.Status));
                }
                context.Runner.Dispose();
                context.Cancellation.Dispose();
                _executions.TryRemove(context.ExecutionId, out _);
            }
            NotifyStateChanged();
            if (error is null) context.Completion.TrySetResult();
            else context.Completion.TrySetException(error);
        }
    }

    public bool IsRoutineRunning(string routineId) => _activeWorkflows.ContainsKey(routineId);

    public void StopRoutine(string routineId)
    {
        lock (_lifecycleLock)
        {
            if (_activeWorkflows.TryRemove(routineId, out var context))
            {
                context.Cancellation.Cancel();
                Volatile.Write(ref _lastStatus, new SystemStatusState());
            }
        }
        NotifyStateChanged();
    }

    public void FinishAll()
    {
        lock (_lifecycleLock)
        {
            _activeWorkflows.Clear();
            foreach (var context in _executions.Values) context.Cancellation.Cancel();
            Volatile.Write(ref _lastStatus, new SystemStatusState());
        }
        NotifyStateChanged();
    }

    public IReadOnlyList<RunningWorkflowInfo> GetActiveWorkflows() => _activeWorkflows.Values
        .OrderBy(c => c.StartedAt).Select(context =>
        {
            var status = Volatile.Read(ref context.Status);
            bool countdown = status.GracePeriodActive ||
                status.EngineState == EngineState.WaitingTrigger &&
                context.Routine.Trigger.Type is TriggerType.Countdown or TriggerType.FixedTime or TriggerType.Schedule or TriggerType.ScheduledTime;
            return new RunningWorkflowInfo
            {
                RoutineId = context.Routine.Id, Name = context.Routine.Name, Description = context.Routine.Description, StartedAt = context.StartedAt,
                Elapsed = context.Elapsed.Elapsed, State = status.EngineState, Phase = status.CurrentPhase, TriggerType = context.Routine.Trigger.Type,
                RemainingSeconds = status.GracePeriodActive ? status.GracePeriodRemainingSeconds : countdown ? status.TimeRemainingSeconds : null,
                ProgressPercentage = status.ProgressPercentage, GracePeriodActive = status.GracePeriodActive,
                GracePeriodRemainingSeconds = status.GracePeriodRemainingSeconds, KeepAliveActive = status.KeepAliveActive
            };
        }).ToArray();

    private static RunningWorkflowInfo? Focus(IReadOnlyList<RunningWorkflowInfo> active) =>
        active.Where(info => info.GracePeriodActive).OrderBy(info => info.GracePeriodRemainingSeconds).FirstOrDefault() ?? active.FirstOrDefault();

    public SystemStatusState GetStatusSnapshot()
    {
        var active = GetActiveWorkflows();
        var focus = Focus(active);
        var result = focus is null ? Volatile.Read(ref _lastStatus) : new SystemStatusState
        {
            EngineState = focus.State, ActivePresetId = focus.RoutineId, ActivePresetName = focus.Name,
            CurrentPhase = focus.Phase, ProgressPercentage = focus.ProgressPercentage,
            TimeRemainingSeconds = focus.RemainingSeconds ?? 0, GracePeriodActive = focus.GracePeriodActive,
            GracePeriodRemainingSeconds = focus.GracePeriodRemainingSeconds,
            KeepAliveActive = active.Any(info => info.KeepAliveActive)
        };
        return result with { Timestamp = DateTimeOffset.UtcNow, ActiveWorkflows = active, Metrics = _systemAdapter.GetCurrentMetrics() };
    }

    private void NotifyStateChanged()
    {
        if (!_isDisposed) StatusChanged?.Invoke(GetStatusSnapshot());
    }

    public Task StartQuickCountdownAsync(string title, TimeSpan duration, TerminalActionType terminalAction, CancellationToken cancellationToken = default)
    {
        var routine = new PresetDefinition
        {
            Id = "preset_quick_" + Guid.NewGuid().ToString("N"), Name = title,
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.Countdown,
                Parameters = new() { ["durationSeconds"] = System.Text.Json.JsonSerializer.SerializeToElement((int)duration.TotalSeconds) }
            },
            TerminalAction = new TerminalActionDefinition
            {
                Type = terminalAction,
                Parameters = new() { ["gracePeriodSeconds"] = System.Text.Json.JsonSerializer.SerializeToElement(30) }
            }
        };
        return StartRoutine(routine, cancellationToken);
    }

    public void PostponeRoutine(string routineId, TimeSpan extraTime)
    {
        lock (_lifecycleLock)
            if (_activeWorkflows.TryGetValue(routineId, out var context)) context.Runner.Postpone(extraTime);
    }
    public void Postpone(TimeSpan extraTime)
    {
        var focus = Focus(GetActiveWorkflows());
        if (focus is not null) PostponeRoutine(focus.RoutineId, extraTime);
    }
    public void Pause() { var focus = Focus(GetActiveWorkflows()); if (focus is not null && _activeWorkflows.TryGetValue(focus.RoutineId, out var c)) c.Runner.Pause(); }
    public void Resume() { var focus = Focus(GetActiveWorkflows()); if (focus is not null && _activeWorkflows.TryGetValue(focus.RoutineId, out var c)) c.Runner.Resume(); }

    public void Dispose()
    {
        Task[] pending;
        lock (_lifecycleLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            pending = _executions.Values.Select(c => c.Completion.Task).ToArray();
            FinishAll();
        }
        try { Task.WhenAll(pending).Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }
}
