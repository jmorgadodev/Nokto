namespace Nokto.Core.Models;

public record AppConfig
{
    public string? Schema { get; init; } = "https://raw.githubusercontent.com/nokto/schemas/v1/config.schema.json";
    public int Version { get; init; } = 1;
    public AppSettings App { get; init; } = new();
    public KeepAliveSettings KeepAlive { get; init; } = new();
    public LanServerSettings LanServer { get; init; } = new();
}

public record AppSettings
{
    public string Theme { get; init; } = "Night";
    public bool MinimizeToTrayOnClose { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public int GracePeriodSeconds { get; init; } = 60;
    public string PanicHotkey { get; init; } = "Control+Shift+F12";
    public int MetricsPollingIntervalMs { get; init; } = 2000;
}

public record KeepAliveSettings
{
    public KeepAliveMode DefaultMode { get; init; } = KeepAliveMode.InputSimulation;
    public int JitterMinSeconds { get; init; } = 45;
    public int JitterMaxSeconds { get; init; } = 105;
    public string SimulatedKey { get; init; } = "VK_F15";
    public int MouseDeltaPixels { get; init; } = 1;
}

public record LanServerSettings
{
    public bool Enabled { get; init; } = false;
    public int Port { get; init; } = 4884;
    public string BindAddress { get; init; } = "0.0.0.0";
    public bool RequireAuth { get; init; } = true;
    public string AuthToken { get; init; } = "a9f82d1c6e4b8a73";
    public bool AllowScreenPreview { get; init; } = true;
    public int ScreenPreviewQuality { get; init; } = 60;
}
