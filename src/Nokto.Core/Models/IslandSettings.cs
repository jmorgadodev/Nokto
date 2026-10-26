namespace Nokto.Core.Models;

public sealed class IslandSettings
{
    public bool Enabled { get; set; } = false;
    public string Style { get; set; } = "Eyes";
    // Dashboard (legacy default), Island, Both, Tray.
    public string StartupMode { get; set; } = "Dashboard";
    // Los usuarios anteriores conservan su ajuste de inicio hasta elegir un modo nuevo.
    public bool StartupModeConfigured { get; set; } = false;
    public bool ShowClock { get; set; } = true;
    public bool ExpandOnHover { get; set; } = true;
    public bool ReducedMotion { get; set; } = false;
    // Auto usa la frecuencia actual del monitor; valores manuales: 60,120,144,165,240.
    public string AnimationRateMode { get; set; } = "Auto";
}