namespace Nokto.Core.Models;

/// <summary>Local hardware identity, captured once without periodic polling.</summary>
public sealed record HardwareProfile
{
    public string OperatingSystemName { get; init; } = "No disponible";
    public string ProcessorName { get; init; } = "No disponible";
    public double InstalledMemoryGigabytes { get; init; }
    public string GraphicsAdapterName { get; init; } = "No disponible";
    public int MonitorCount { get; init; }
    public int PrimaryScreenWidth { get; init; }
    public int PrimaryScreenHeight { get; init; }
    public int PrimaryScreenRefreshRateHz { get; init; }

    public string OperatingSystemDisplayText => OperatingSystemName.Split(" (Build ", 2, StringSplitOptions.None)[0]
        .Replace("Home Single Language", "Home", StringComparison.OrdinalIgnoreCase)
        .Replace("HomeSingleLanguage", "Home", StringComparison.OrdinalIgnoreCase);
    public string GraphicsDisplayText => GraphicsAdapterName.Replace("GeForce ", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Laptop GPU", "", StringComparison.OrdinalIgnoreCase)
        .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Graphics", "", StringComparison.OrdinalIgnoreCase);

    public string MemoryText => InstalledMemoryGigabytes > 0 ? $"{InstalledMemoryGigabytes:0.#} GB Instalados" : "No disponible";
    public string MonitorsText => MonitorCount > 0 && PrimaryScreenWidth > 0 && PrimaryScreenHeight > 0
        ? $"{MonitorCount} {(MonitorCount == 1 ? "Pantalla" : "Pantallas")}: {ResolutionName} ({PrimaryScreenWidth}×{PrimaryScreenHeight}{(PrimaryScreenRefreshRateHz > 0 ? $" @ {PrimaryScreenRefreshRateHz}Hz" : string.Empty)})"
        : "No disponible";
    public string ResolutionName => (PrimaryScreenWidth, PrimaryScreenHeight) switch
    {
        (>= 3840, >= 2160) => "4K UHD",
        (>= 2560, >= 1440) => "QHD",
        (>= 1920, >= 1080) => "FHD",
        _ => ""
    };
}
