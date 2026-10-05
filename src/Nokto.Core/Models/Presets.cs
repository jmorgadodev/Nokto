using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nokto.Core.Models;

public record PresetsFile
{
    public string? Schema { get; init; } = "https://raw.githubusercontent.com/nokto/schemas/v1/presets.schema.json";
    public int Version { get; init; } = 1;
    public List<PresetDefinition> Presets { get; init; } = [];

    public static List<PresetDefinition> GetDefaultPresets()
    {
        return
        [
            // 1. "Jornada Laboral Anti-Ausente": Disparador Inactividad -> Mantener Teams/PC activo (F15) -> Bloquear sesión tras 8h.
            new PresetDefinition
            {
                Id = "preset_workday_keepalive",
                Name = "Jornada Laboral Anti-Ausente",
                Description = "Tras 3 minutos de inactividad, mantiene el equipo activo y bloquea la sesión al completar 8 horas de jornada.",
                IsFavorite = true,
                Icon = "Sun",
                IsSystemPreset = true,
                Trigger = new TriggerDefinition
                {
                    Type = TriggerType.UserIdle,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["idleMinutes"] = JsonSerializer.SerializeToElement(3m)
                    }
                },
                Pipeline =
                [
                    new PipelineStepDefinition
                    {
                        StepOrder = 1,
                        ActionType = ActionType.KeepAliveEngine,
                        IgnoreFailure = false
                    }
                ],
                TerminalAction = new TerminalActionDefinition
                {
                    Type = TerminalActionType.LockStation,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["delayHours"] = JsonSerializer.SerializeToElement(8),
                        ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(30)
                    }
                }
            },

            // 2. "Render Nocturno Blender": Disparador Fin de Proceso -> Captura de evidencia -> Apagar PC con gracia 60s.
            new PresetDefinition
            {
                Id = "preset_blender_night_render",
                Name = "Render Nocturno Blender",
                Description = "Al terminar Blender, toma una captura y muestra un aviso durante 60 segundos antes de apagar el equipo.",
                IsFavorite = true,
                Icon = "Movie",
                IsSystemPreset = true,
                Trigger = new TriggerDefinition
                {
                    Type = TriggerType.ProcessExit,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["processName"] = JsonSerializer.SerializeToElement("blender.exe"),
                        ["debounceSeconds"] = JsonSerializer.SerializeToElement(5)
                    }
                },
                Pipeline =
                [
                    new PipelineStepDefinition
                    {
                        StepOrder = 1,
                        ActionType = ActionType.CaptureScreenshot,
                        IgnoreFailure = true
                    }
                ],
                TerminalAction = new TerminalActionDefinition
                {
                    Type = TerminalActionType.Shutdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60)
                    }
                }
            },

            // 3. "Modo Dormir Multimedia": Disparador Cuenta atrás 45m -> Desvanecer audio WASAPI (15s), pausar música -> Suspender PC.
            new PresetDefinition
            {
                Id = "preset_sleep_multimedia",
                Name = "Modo Dormir Multimedia",
                Description = "Después de 45 minutos, baja el volumen gradualmente durante 15 segundos, pausa la música y suspende el equipo.",
                IsFavorite = true,
                Icon = "Moon",
                IsSystemPreset = true,
                Trigger = new TriggerDefinition
                {
                    Type = TriggerType.Countdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(45 * 60)
                    }
                },
                Pipeline =
                [
                    new PipelineStepDefinition
                    {
                        StepOrder = 1,
                        ActionType = ActionType.AudioFadeOut,
                        Parameters = new Dictionary<string, JsonElement>
                        {
                            ["durationSeconds"] = JsonSerializer.SerializeToElement(15),
                            ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(0)
                        },
                        IgnoreFailure = true
                    },
                    new PipelineStepDefinition
                    {
                        StepOrder = 2,
                        ActionType = ActionType.MediaControl,
                        IgnoreFailure = true
                    }
                ],
                TerminalAction = new TerminalActionDefinition
                {
                    Type = TerminalActionType.Sleep,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(15)
                    }
                }
            },

            // 4. "Fin de Descarga Pesada": Disparador Red inactiva (<50 KB/s por 3m) -> Captura evidencia -> Apagar PC.
            new PresetDefinition
            {
                Id = "preset_download_finished",
                Name = "Fin de Descarga Pesada",
                Description = "Red inactiva (<50 KB/s por 3m): captura de evidencia y apagado del PC.",
                IsFavorite = false,
                Icon = "CloudDownload",
                IsSystemPreset = true,
                Trigger = new TriggerDefinition
                {
                    Type = TriggerType.NetworkThroughput,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["thresholdKBs"] = JsonSerializer.SerializeToElement(50.0),
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(180)
                    }
                },
                Pipeline =
                [
                    new PipelineStepDefinition
                    {
                        StepOrder = 1,
                        ActionType = ActionType.CaptureScreenshot,
                        IgnoreFailure = true
                    }
                ],
                TerminalAction = new TerminalActionDefinition
                {
                    Type = TerminalActionType.Shutdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60)
                    }
                }
            },

            // 5. "Pausa Activa / Pomodoro": Disparador Tiempo de trabajo (50m) -> Aviso flotante -> Bloquear sesión.
            new PresetDefinition
            {
                Id = "preset_pomodoro_break",
                Name = "Pausa Activa / Pomodoro",
                Description = "Tiempo de trabajo (50m): aviso flotante previo y bloqueo de sesión para descanso activo.",
                IsFavorite = false,
                Icon = "Timer",
                IsSystemPreset = true,
                Trigger = new TriggerDefinition
                {
                    Type = TriggerType.Countdown,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(50 * 60)
                    }
                },
                Pipeline = [],
                TerminalAction = new TerminalActionDefinition
                {
                    Type = TerminalActionType.LockStation,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60)
                    }
                }
            }
        ];
    }

    public static PresetsFile CreateDefault()
    {
        return new PresetsFile
        {
            Version = 1,
            Presets = GetDefaultPresets()
        };
    }
}

public static class Presets
{
    public static List<PresetDefinition> GetDefaultPresets() => PresetsFile.GetDefaultPresets();

    public static List<PresetDefinition> RestoreFactoryPresets(IEnumerable<PresetDefinition> existing)
    {
        var defaults = GetDefaultPresets();
        var systemIds = defaults.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        return [.. existing.Where(p => !systemIds.Contains(p.Id)), .. defaults];
    }
}

public record PresetDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    [JsonIgnore]
    public string DisplayDescription => IsSystemPreset
        ? Description.Replace("desvanece audio WASAPI", "baja el volumen gradualmente", StringComparison.OrdinalIgnoreCase)
            .Replace("WASAPI", "audio", StringComparison.OrdinalIgnoreCase)
            .Replace(" (F15)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("VK_F15", "actividad automática", StringComparison.OrdinalIgnoreCase)
            .Replace("KeepAlive", "mantener equipo activo", StringComparison.OrdinalIgnoreCase)
            .Replace("con gracia de", "con aviso previo de", StringComparison.OrdinalIgnoreCase)
        : Description;
    public bool IsFavorite { get; init; }
    public string Icon { get; init; } = "Moon";
    public bool IsSystemPreset { get; init; }
    public TriggerDefinition Trigger { get; init; } = new();
    public System.Collections.ObjectModel.ObservableCollection<WorkflowActionItem>? Actions { get; init; }
    // Legacy fields remain readable for existing portable configurations.
    public List<PipelineStepDefinition> Pipeline { get; init; } = [];
    public TerminalActionDefinition TerminalAction { get; init; } = new();
}

public record TriggerDefinition
{
    public TriggerType Type { get; init; } = TriggerType.Countdown;
    public Dictionary<string, JsonElement>? Parameters { get; init; }
}

public record PipelineStepDefinition
{
    public int StepOrder { get; init; }
    public ActionType ActionType { get; init; } = ActionType.TurnOffMonitors;
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public bool IgnoreFailure { get; init; }
}

public record TerminalActionDefinition
{
    public TerminalActionType Type { get; init; } = TerminalActionType.None;
    public Dictionary<string, JsonElement>? Parameters { get; init; }
}
