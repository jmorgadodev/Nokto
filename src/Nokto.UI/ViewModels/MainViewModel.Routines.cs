using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Models;
using System.Collections.ObjectModel;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<RoutineItem> RoutineCards { get; } = [];
    public ObservableCollection<ActiveRoutineItem> ActiveRoutines { get; } = [];
    [ObservableProperty] private RoutineItem? _selectedRoutineItem;
    [ObservableProperty] private string _footerTaskText = "Ninguna tarea en curso";
    [ObservableProperty] private string _footerTaskToolTip = "Sin rutinas en curso";
    [ObservableProperty] private string _graceRoutineName = "";
    private string? _graceRoutineId;

    public bool HasActiveRoutines => ActiveRoutines.Count > 0;
    public bool IsSingleRoutineActive => ActiveRoutines.Count == 1;
    public int ActiveRoutineCount => ActiveRoutines.Count;
    public string ActiveTasksHeaderText => $"⚡ TAREAS Y RUTINAS EN EJECUCIÓN ({ActiveRoutineCount} activas)";
    public bool IsSelectedRoutineRunning => !string.IsNullOrWhiteSpace(StudioPresetId) && _workflowEngine.IsRoutineRunning(StudioPresetId);
    public bool CanStartSelectedRoutine => !string.IsNullOrWhiteSpace(StudioPresetId) && !IsSelectedRoutineRunning;

    partial void OnSelectedRoutineItemChanged(RoutineItem? value) => SelectedRoutine = value?.Definition;
    partial void OnStudioPresetIdChanged(string value) => RefreshSelectedRoutineCommands();

    private void RefreshRoutineCards()
    {
        string? selectedId = SelectedRoutine?.Id;
        RoutineCards.Clear();
        foreach (var preset in SavedPipelines)
            RoutineCards.Add(new(preset) { IsRunning = _workflowEngine.IsRoutineRunning(preset.Id) });
        SelectedRoutineItem = RoutineCards.FirstOrDefault(item => item.Id == selectedId);
    }

    private void RefreshSelectedRoutineCommands()
    {
        OnPropertyChanged(nameof(IsSelectedRoutineRunning));
        OnPropertyChanged(nameof(CanStartSelectedRoutine));
        OnPropertyChanged(nameof(CanDeleteSelectedRoutine));
        RunCurrentStudioPipelineCommand.NotifyCanExecuteChanged();
        FinishSelectedRoutineCommand.NotifyCanExecuteChanged();
        DeleteSelectedPipelineCommand.NotifyCanExecuteChanged();
    }

    private void RefreshActiveRoutines()
    {
        if (_disposalCts.IsCancellationRequested) return;
        var active = _workflowEngine.GetActiveWorkflows();
        var activeIds = active.Select(info => info.RoutineId).ToHashSet(StringComparer.Ordinal);
        foreach (var old in ActiveRoutines.Where(item => !activeIds.Contains(item.Info.RoutineId)).ToArray())
            ActiveRoutines.Remove(old);
        foreach (var info in active)
        {
            var item = ActiveRoutines.FirstOrDefault(item => item.Info.RoutineId == info.RoutineId);
            if (item is null) ActiveRoutines.Add(new(info, FinishRoutine));
            else item.Info = info;
        }
        foreach (var item in RoutineCards) item.IsRunning = activeIds.Contains(item.Id);
        IsTaskRunning = active.Count > 0;
        OnPropertyChanged(nameof(HasActiveRoutines));
        OnPropertyChanged(nameof(IsSingleRoutineActive));
        OnPropertyChanged(nameof(ActiveRoutineCount));
        OnPropertyChanged(nameof(ActiveTasksHeaderText));
        OnPropertyChanged(nameof(CanStartManualTask));
        OnPropertyChanged(nameof(CanFinishManualTask));
        OnPropertyChanged(nameof(ManualStartButtonText));
        StartManualTaskCommand.NotifyCanExecuteChanged();
        FinishManualTaskCommand.NotifyCanExecuteChanged();
        RefreshSelectedRoutineCommands();
        WorkModeStatusText = IsKeepAliveActive || active.Any(info => info.KeepAliveActive) ? "Activo (Jitter F15)" : "Inactivo";
        FooterTaskText = active.Count switch
        {
            0 => "Ninguna tarea en curso",
            1 => active[0].Name,
            _ => $"{active.Count} rutinas activas"
        };
        FooterTaskToolTip = active.Count == 0 ? "Sin rutinas en curso" : string.Join(Environment.NewLine, active.Select(info => $"{info.Name} • {info.StatusText}"));
        var grace = active.Where(info => info.GracePeriodActive).OrderBy(info => info.GracePeriodRemainingSeconds).FirstOrDefault();
        _graceRoutineId = grace?.RoutineId;
        GraceRoutineName = grace?.Name ?? "";
        RequestGraceOverlay?.Invoke(grace is not null, grace?.GracePeriodRemainingSeconds ?? 0);
        var focus = grace ?? active.FirstOrDefault();
        ActiveTaskTitle = focus?.Name ?? "Ninguna tarea en curso";
        TimeRemainingText = focus?.TimeText ?? "";
        SecondsRemaining = focus?.RemainingSeconds ?? 0;
        ProgressPercentage = focus?.ProgressPercentage ?? 0;
        CurrentStatusText = grace is not null ? $"Aviso previo: {grace.Name} ({grace.GracePeriodRemainingSeconds}s)"
            : active.Count > 0 ? $"{active.Count} rutina(s) en curso" : "Listo";
    }

    private async Task RunRoutineSafelyAsync(PresetDefinition routine)
    {
        string? error = null;
        try
        {
            var execution = _workflowEngine.StartRoutine(routine, _disposalCts.Token);
            RefreshActiveRoutines();
            await execution;
        }
        catch (Exception ex) { error = $"Rutina '{routine.Name}' finalizada por error: {ex.Message}"; }
        if (!_disposalCts.IsCancellationRequested)
        {
            RefreshActiveRoutines();
            if (error is not null) CurrentStatusText = error;
        }
    }

    [RelayCommand]
    public void FinishRoutine(string routineId)
    {
        _workflowEngine.StopRoutine(routineId);
        RefreshActiveRoutines();
    }

    [RelayCommand(CanExecute = nameof(IsSelectedRoutineRunning))]
    public void FinishSelectedRoutine() => FinishRoutine(StudioPresetId);

    [RelayCommand(CanExecute = nameof(CanFinishManualTask))]
    public void FinishManualTask()
    {
        var latestManual = ActiveRoutines.LastOrDefault(item => item.IsManual);
        if (latestManual is not null) FinishRoutine(latestManual.Info.RoutineId);
    }

    public void FinishGraceRoutine()
    {
        if (_graceRoutineId is not null) FinishRoutine(_graceRoutineId);
    }
}
