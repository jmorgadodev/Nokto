using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nokto.Core.Models;

public record RoutineTrigger : TriggerDefinition;
public record WorkflowActionItem : PipelineStepDefinition;

[JsonConverter(typeof(JsonStringEnumConverter<AppLaunchMode>))]
public enum AppLaunchMode { Normal, Maximized, Minimized }

public record LaunchAppOptions(string ExecutablePath, string Arguments = "", string? WorkingDirectory = null,
    AppLaunchMode LaunchMode = AppLaunchMode.Normal, bool SkipIfAlreadyRunning = true);

/// <summary>Native catalog data; UI owns the disposable bitmap created from 32×32 BGRA pixels.</summary>
public record InstalledApplication(string Name, string ExecutablePath, string Arguments = "",
    string? WorkingDirectory = null, byte[]? IconPixels = null);

public static class WorkflowDefinition
{
    public static ObservableCollection<WorkflowActionItem> GetActions(PresetDefinition routine)
    {
        if (routine.Actions is not null) return new(routine.Actions.Select(Copy));
        var actions = new ObservableCollection<WorkflowActionItem>(routine.Pipeline.OrderBy(s => s.StepOrder).Select(Copy));
        foreach (var action in actions.Where(a => a.ActionType == ActionType.KeepAliveEngine))
            if (routine.TerminalAction.Parameters?.TryGetValue("delayHours", out var hours) == true)
            {
                int index = actions.IndexOf(action);
                var parameters = action.Parameters ?? [];
                parameters["delayHours"] = hours.Clone();
                actions[index] = action with { Parameters = parameters };
                break;
            }
        var terminal = routine.TerminalAction;
        if (terminal.Type != TerminalActionType.None)
        {
            var parameters = CloneParameters(terminal.Parameters) ?? [];
            parameters["powerAction"] = JsonSerializer.SerializeToElement(terminal.Type.ToString());
            actions.Add(new() { ActionType = ActionType.PowerAction, Parameters = parameters });
        }
        else if (terminal.Parameters?.TryGetValue("subType", out var subType) == true && subType.GetString() == "MonitorsOff")
            actions.Add(new() { ActionType = ActionType.TurnOffMonitors });
        for (int i = 0; i < actions.Count; i++) actions[i] = actions[i] with { StepOrder = i + 1 };
        return actions;
    }

    public static WorkflowActionItem Copy(PipelineStepDefinition step) => new()
    {
        StepOrder = step.StepOrder, ActionType = step.ActionType,
        Parameters = CloneParameters(step.Parameters), IgnoreFailure = step.IgnoreFailure
    };

    public static Dictionary<string, JsonElement>? CloneParameters(Dictionary<string, JsonElement>? parameters) =>
        parameters?.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal);
}
