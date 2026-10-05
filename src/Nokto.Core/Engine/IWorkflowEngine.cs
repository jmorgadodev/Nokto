using Nokto.Core.Models;

namespace Nokto.Core.Engine;

public interface IWorkflowEngine : IDisposable
{
    EngineState CurrentState { get; }
    bool IsDryRunMode { get; set; }
    SystemStatusState GetStatusSnapshot();

    event Action<SystemStatusState>? StatusChanged;
    event Action<string>? LogMessageReceived;
    event Action<int>? GracePeriodTick; // remaining seconds

    Task StartPresetAsync(PresetDefinition preset, CancellationToken cancellationToken = default);
    Task StartQuickCountdownAsync(string title, TimeSpan duration, TerminalActionType terminalAction, CancellationToken cancellationToken = default);
    Task StartRoutine(PresetDefinition routine, CancellationToken cancellationToken = default);
    void StopRoutine(string routineId);
    bool IsRoutineRunning(string routineId);
    IReadOnlyList<RunningWorkflowInfo> GetActiveWorkflows();
    void FinishAll();
    void PostponeRoutine(string routineId, TimeSpan extraTime);
    void Postpone(TimeSpan extraTime);
    void Pause();
    void Resume();
}
