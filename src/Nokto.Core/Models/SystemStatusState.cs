namespace Nokto.Core.Models;

public record MonitoredProcessInfo
{
    public string Name { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public int LastSeenSecondsAgo { get; init; }
}

public record SystemStatusState
{
    public IReadOnlyList<RunningWorkflowInfo> ActiveWorkflows { get; init; } = [];
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public EngineState EngineState { get; init; } = EngineState.Idle;
    public string? ActivePresetId { get; init; }
    public string? ActivePresetName { get; init; }
    public string? CurrentPhase { get; init; }
    public double ProgressPercentage { get; init; }
    public int TimeRemainingSeconds { get; init; }
    public bool GracePeriodActive { get; init; }
    public int GracePeriodRemainingSeconds { get; init; }
    public SystemMetrics Metrics { get; init; } = new();
    public MonitoredProcessInfo? MonitoredProcess { get; init; }
    public bool KeepAliveActive { get; init; }
}
