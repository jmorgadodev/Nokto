namespace Nokto.Core.Models;

public record AuditLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string PresetId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ExecutionDurationSeconds { get; init; }
    public string TriggerFired { get; init; } = string.Empty;
    public string TerminalActionExecuted { get; init; } = string.Empty;
    public string? SnapshotFile { get; init; }
    public string? ExitNotes { get; init; }
}
