namespace Nokto.Core.Models;

/// <summary>
/// Métricas instantáneas de telemetría pasiva del sistema operativo.
/// Diseñado para cero allocations periódicas y serialización AOT directa.
/// </summary>
public record SystemMetrics
{
    public double CpuUsagePercentage { get; init; }
    public double GpuUsagePercentage { get; init; }
    public double RamUsedMb { get; init; }
    public double RamTotalMb { get; init; }
    public double NetworkDownKBs { get; init; }
    public double NetworkUpKBs { get; init; }
    public int AudioSilenceDurationSeconds { get; init; }
    public int UserIdleSeconds { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
