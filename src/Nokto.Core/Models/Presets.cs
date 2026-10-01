using System.Text.Json;

namespace Nokto.Core.Models;

public record PresetsFile
{
    public string? Schema { get; init; } = "https://raw.githubusercontent.com/nokto/schemas/v1/presets.schema.json";
    public int Version { get; init; } = 1;
    public List<PresetDefinition> Presets { get; init; } = [];
}

public record PresetDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsFavorite { get; init; }
    public string Icon { get; init; } = "Moon";
    public TriggerDefinition Trigger { get; init; } = new();
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
