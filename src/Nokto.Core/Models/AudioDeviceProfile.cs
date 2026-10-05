namespace Nokto.Core.Models;

/// <summary>Passive identity, volume and mute state of the default audio endpoints.</summary>
public sealed record AudioDeviceProfile
{
    public string OutputName { get; init; } = "Sin dispositivo activo";
    public string InputName { get; init; } = "Sin dispositivo activo";
    public string? OutputId { get; init; }
    public string? InputId { get; init; }
    public int? OutputVolumePercent { get; init; }
    public bool? OutputMuted { get; init; }
    public bool? InputMuted { get; init; }

    public string OutputText => FormatOutput(OutputName);
    public string OutputCompactText => FormatOutput(OutputName.Split(" (", 2, StringSplitOptions.None)[0]);
    public string InputDisplayName => ShortenInputName(InputName);
    public string InputText => InputName == "Sin dispositivo activo" ? InputName
        : $"{InputDisplayName} — {InputMuted switch { true => "🔴 Silenciado", false => "🟢 Listo", null => "Estado no disponible" }}";

    private string FormatOutput(string name) => OutputName == "Sin dispositivo activo" ? OutputName
        : OutputMuted == true ? $"{name} — [Silenciado]"
        : OutputVolumePercent is int percent ? $"{name} — {Math.Clamp(percent, 0, 100)}%" : name;

    private static string ShortenInputName(string name)
    {
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) && name.Contains("Smart Sound", StringComparison.OrdinalIgnoreCase))
            return "Intel® Smart Sound Mic";
        return name.Length <= 32 ? name : name[..31].TrimEnd() + "…";
    }
}
