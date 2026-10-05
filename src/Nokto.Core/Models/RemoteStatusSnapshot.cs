namespace Nokto.Core.Models;

/// <summary>Allowlisted local telemetry. Configuration and credentials never leave the desktop.</summary>
public sealed record RemoteStatusSnapshot
{
    public SystemMetrics Metrics { get; init; } = new();
    public long UptimeSeconds { get; init; }
    public BatteryStatus Battery { get; init; } = new();
    public AudioDeviceProfile Audio { get; init; } = new();
    public IReadOnlyList<RunningWorkflowInfo> Tasks { get; init; } = [];
}

public sealed record RemoteCommandResult(bool Success, string Message);
