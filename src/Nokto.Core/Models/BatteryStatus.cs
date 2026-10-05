namespace Nokto.Core.Models;

/// <summary>
/// Representa el estado de alimentación del sistema (batería y línea de corriente AC).
/// Compatible con System.Text.Json AOT sin reflexión.
/// </summary>
public record BatteryStatus
{
    public bool HasBattery { get; init; }
    public bool IsCharging { get; init; }
    public bool IsOnAcPower { get; init; }
    public bool IsAcConnected => IsOnAcPower;
    public int BatteryLifePercent { get; init; } = -1; // 0-100, o -1 si es desconocido
    public int BatteryLifeSecondsRemaining { get; init; } = -1; // Segundos restantes o -1
}
