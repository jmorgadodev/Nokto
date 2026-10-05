namespace Nokto.Core.Models;

/// <summary>Immutable, local snapshot of one execution. Unknown remaining time is null.</summary>
public sealed record RunningWorkflowInfo
{
    public required string RoutineId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int? RemainingSeconds { get; init; }
    public EngineState State { get; init; }
    public TriggerType TriggerType { get; init; }
    public string? Phase { get; init; }
    public double ProgressPercentage { get; init; }
    public bool GracePeriodActive { get; init; }
    public int GracePeriodRemainingSeconds { get; init; }
    public bool KeepAliveActive { get; init; }
    public string TimeText => RemainingSeconds is int seconds
        ? $"Restante: {FormatTime(TimeSpan.FromSeconds(Math.Max(0, seconds)))}"
        : $"Activa: {FormatTime(Elapsed)}";
    public string StatusText => State switch
    {
        EngineState.GracePeriod => $"Aviso previo • {TimeText}",
        EngineState.ExecutingActions when KeepAliveActive => $"Manteniendo equipo activo • {TimeText}",
        EngineState.ExecutingActions => $"Ejecutando acciones • {TimeText}",
        EngineState.Paused => $"En pausa • {TimeText}",
        EngineState.WaitingTrigger when TriggerType == TriggerType.UserIdle => $"Vigilando inactividad • {TimeText}",
        EngineState.WaitingTrigger when TriggerType == TriggerType.ProcessExit => $"Vigilando proceso • {TimeText}",
        _ => TimeText
    };
    public static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
}
