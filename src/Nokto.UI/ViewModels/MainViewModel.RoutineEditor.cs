using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Applications;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel
{
    public string ApplicationVersionText => $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";
    public string ExecutableLocationText => Environment.ProcessPath ?? AppContext.BaseDirectory;
    public ObservableCollection<InstalledAppInfo> InstalledApps { get; } = [];
    public ObservableCollection<WeekdaySelectionItem> StudioWeekdays { get; } =
    [new("L", "Lunes", 1), new("M", "Martes", 2), new("X", "Miércoles", 3), new("J", "Jueves", 4), new("V", "Viernes", 5), new("S", "Sábado", 6), new("D", "Domingo", 0)];
    [ObservableProperty] private bool _studioRepeatSchedule = true;
    [ObservableProperty] private decimal _studioNetworkThresholdKBs = 50;
    [ObservableProperty] private decimal _studioNetworkIdleSeconds = 60;
    [ObservableProperty] private string _installedAppsStatusText = "Buscando aplicaciones del Menú Inicio…";
    private bool _routineEditorDisposed;
    public bool IsStudioTriggerNetworkIdle => StudioTriggerTypeIndex == 6;
    public bool IsStudioTriggerManual => StudioTriggerTypeIndex == 7;

    private async Task LoadInstalledAppsAsync(IInstalledAppsService? service)
    {
        try
        {
            var apps = await (service ?? new InstalledAppsService()).ScanAsync(_disposalCts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_routineEditorDisposed) return;
                foreach (var app in apps) InstalledApps.Add(InstalledAppInfo.FromApplication(app));
                foreach (var step in StudioPipelineSteps) step.SetInstalledApps(InstalledApps);
                InstalledAppsStatusText = $"{InstalledApps.Count} aplicaciones del Menú Inicio · catálogo local";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_routineEditorDisposed) InstalledAppsStatusText = $"Catálogo no disponible ({ex.GetType().Name}). Puedes elegir una ruta manual.";
            });
        }
    }

    private void DisposeRoutineEditor()
    {
        _routineEditorDisposed = true;
        foreach (var app in InstalledApps) app.Icon?.Dispose();
    }

    private void LoadScheduleFields(TriggerDefinition trigger)
    {
        StudioRepeatSchedule = true;
        foreach (var day in StudioWeekdays) day.IsSelected = true;
        var parameters = trigger.Parameters;
        if (parameters is null) return;
        if (parameters.TryGetValue("timeOfDay", out var time) && TimeSpan.TryParse(time.GetString(), out var parsed)) StudioExactTime = parsed;
        if (parameters.TryGetValue("daysOfWeek", out var days) && days.ValueKind == JsonValueKind.Array)
        {
            var selected = days.EnumerateArray().Select(d => d.GetInt32()).ToHashSet();
            foreach (var day in StudioWeekdays) day.IsSelected = selected.Contains(day.Day);
        }
        if (parameters.TryGetValue("repeat", out var repeat)) StudioRepeatSchedule = repeat.GetBoolean();
    }

    private void ValidateLinearRoutine()
    {
        if (IsStudioTriggerProcess && string.IsNullOrWhiteSpace(StudioProcessWatchMode == 1 ? StudioLaunchFilePath : StudioSelectedWindowProcess))
            throw new InvalidOperationException("Selecciona un programa para vigilar.");
        if (IsStudioTriggerProcess && StudioProcessWatchMode == 1 && !File.Exists(StudioLaunchFilePath))
            throw new InvalidOperationException("No existe el archivo del disparador.");
        foreach (var step in StudioPipelineSteps)
        {
            if (step.IsLaunchApp && (!File.Exists(step.LaunchExecutablePath) || !new[] { ".exe", ".bat", ".cmd", ".ps1" }.Contains(Path.GetExtension(step.LaunchExecutablePath), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Acción {step.DisplayOrderText}: selecciona un ejecutable o script válido.");
            if (step.IsCommand && string.IsNullOrWhiteSpace(step.CommandExecutable)) throw new InvalidOperationException($"Acción {step.DisplayOrderText}: falta el ejecutable del comando.");
            if (step.IsWaitDelay && (step.DelayAmount < 0 || step.DelayAmount * (step.DelayUnitIndex == 1 ? 60 : 1) > 86400))
                throw new InvalidOperationException($"Acción {step.DisplayOrderText}: la espera debe estar entre 0 y 24 horas.");
        }
    }

    private void ShowRoutineValidationError(Exception exception)
    {
        RoutineCheckStatusMessage = exception.Message;
        RoutineCheckStatusColor = "#FFB300";
        IsRoutineCheckVisible = true;
        CurrentStatusText = "Revisa la configuración de la rutina.";
    }
}
