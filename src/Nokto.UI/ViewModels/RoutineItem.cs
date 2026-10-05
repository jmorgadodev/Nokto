using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Models;

namespace Nokto.UI.ViewModels;

public partial class RoutineItem(PresetDefinition definition) : ObservableObject
{
    public PresetDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string DisplayDescription => Definition.DisplayDescription;
    public bool IsSystemPreset => Definition.IsSystemPreset;
    [ObservableProperty] private bool _isRunning;
}

public partial class ActiveRoutineItem : ObservableObject
{
    [ObservableProperty] private RunningWorkflowInfo _info;
    public bool IsManual => Info.RoutineId.StartsWith("manual_", StringComparison.Ordinal);
    public string IconText => IsManual ? "⏱" : "↻";
    public IRelayCommand FinishCommand { get; }
    public ActiveRoutineItem(RunningWorkflowInfo info, Action<string> finish)
    {
        _info = info;
        FinishCommand = new RelayCommand(() => finish(Info.RoutineId));
    }

    partial void OnInfoChanged(RunningWorkflowInfo value)
    {
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IconText));
    }
}
