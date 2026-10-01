namespace Nokto.Core.Models;

public record QuickPowerRequest
{
    public string Action { get; init; } = "TurnOffMonitors";
    public bool Force { get; init; }
}

public record PostponeRequest
{
    public int Seconds { get; init; } = 300;
}
