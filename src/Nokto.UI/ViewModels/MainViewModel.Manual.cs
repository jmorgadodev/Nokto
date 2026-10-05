using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Models;

namespace Nokto.UI.ViewModels;

public sealed record ManualLaunchSelection(string Name, LaunchAppOptions Options);
public sealed record ManualCloseSelection(ApplicationWindow Window, bool AllApplicationWindows)
{
    public string Label => AllApplicationWindows ? $"Todas las ventanas de {Window.ApplicationName}" : Window.DisplayName;
}

public partial class MainViewModel
{
    public ObservableCollection<InstalledAppInfo> ManualFilteredApps { get; } = [];
    public ObservableCollection<ManualLaunchSelection> ManualLaunchApplications { get; } = [];
    public ObservableCollection<ApplicationWindow> ManualAvailableWindows { get; } = [];
    public ObservableCollection<ManualCloseSelection> ManualCloseApplications { get; } = [];
    [ObservableProperty] private string _manualAppSearch = "";
    [ObservableProperty] private InstalledAppInfo? _selectedManualInstalledApp;
    [ObservableProperty] private string _manualExecutablePath = "";
    [ObservableProperty] private string _manualAppArguments = "";
    [ObservableProperty] private string? _manualAppWorkingDirectory;
    [ObservableProperty] private int _manualLaunchModeIndex;
    [ObservableProperty] private bool _manualSkipIfAlreadyRunning = true;
    [ObservableProperty] private ApplicationWindow? _selectedManualWindow;
    [ObservableProperty] private bool _manualCloseAllWindows;
    public IReadOnlyList<string> ManualLaunchModes { get; } = ["Normal", "Maximizada", "Minimizada"];
    public IReadOnlyList<string> ManualOtherConditions { get; } = ["Proceso terminado", "Silencio de audio", "Batería"];
    public bool IsOtherManualCondition => SelectedTriggerTypeIndex is >= 3 and <= 5;
    public int ManualOtherConditionIndex
    {
        get => IsOtherManualCondition ? SelectedTriggerTypeIndex - 3 : -1;
        set { if (value is >= 0 and <= 2) SelectedTriggerTypeIndex = value + 3; }
    }
    public bool IsManualForcedCloseAvailable => ManualPowerActionIndex is 1 or 4;
    public string ManualConditionLabel => "Condición: " + ManualTriggerOptions[Math.Clamp(SelectedTriggerTypeIndex, 0, 6)];
    public string ManualSummaryText => TryBuildManualTask(out _, out var summary, out var error) ? summary : error;
    public string ManualActivationWarning => ManualLaunchApplications.Count > 0 && ManualPowerActionIndex is 1 or 4
        ? "Las aplicaciones se abrirán antes del apagado o reinicio seleccionado." : "";
    public string ManualScreenshotStatus => _config.Settings.EvidenceScreenshotsEnabled
        ? "Las capturas se guardan en la carpeta local de evidencias." : "Las capturas están desactivadas en Ajustes. Actívalas para usar esta acción.";

    private void InitializeManualControls()
    {
        InstalledApps.CollectionChanged += (_, _) => FilterManualApps();
        ManualLaunchApplications.CollectionChanged += (_, _) => RefreshManualPreview();
        ManualCloseApplications.CollectionChanged += (_, _) => RefreshManualPreview();
        RefreshManualPreview();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is "SelectedTriggerTypeIndex" or "CountdownHours" or "CountdownMinutes" or "CountdownSeconds" or
            "ExactTime" or "InactivityMinutes" or "SelectedProcessName" or "DebounceSeconds" or "AudioSilenceSeconds" or
            "BatteryTriggerOnAcDisconnect" or "BatteryTriggerOnThreshold" or "BatteryThresholdPercent" or
            "ManualPowerActionIndex" or "ManualForceClose" or "ManualMuteOutput" or "ManualMuteMicrophone" or
            "ManualPauseMedia" or "ManualTurnOffMonitors" or "ManualLockSession" or "ManualCloseForegroundApps" or
            "ManualGracePeriodEnabled" or "ManualGraceSeconds" or "ManualAudioFadeEnabled" or "ManualScreenshotEnabled")
            RefreshManualPreview();
    }

    private void RefreshManualPreview()
    {
        OnPropertyChanged(nameof(ManualSummaryText));
        OnPropertyChanged(nameof(ManualConditionLabel));
        OnPropertyChanged(nameof(CanStartManualTask));
        OnPropertyChanged(nameof(ManualStartButtonText));
        OnPropertyChanged(nameof(IsManualForcedCloseAvailable));
        OnPropertyChanged(nameof(ManualActivationWarning));
        StartManualTaskCommand.NotifyCanExecuteChanged();
    }

    partial void OnManualAppSearchChanged(string value) => FilterManualApps();
    private void FilterManualApps()
    {
        var selected = SelectedManualInstalledApp;
        ManualFilteredApps.Clear();
        foreach (var app in InstalledApps.Where(app => app.Name.Contains(ManualAppSearch.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            ManualFilteredApps.Add(app);
        if (selected is not null && ManualFilteredApps.Contains(selected)) SelectedManualInstalledApp = selected;
    }
    partial void OnManualExecutablePathChanged(string value)
    {
        if (SelectedManualInstalledApp is { } app && !string.Equals(value, app.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            SelectedManualInstalledApp = null;
        ManualAppArguments = "";
        ManualAppWorkingDirectory = null;
    }
    partial void OnSelectedManualInstalledAppChanged(InstalledAppInfo? value)
    {
        if (value is null) return;
        ManualExecutablePath = value.ExecutablePath;
        ManualAppArguments = value.Arguments;
        ManualAppWorkingDirectory = value.WorkingDirectory;
    }

    [RelayCommand]
    private void ChooseManualCondition(string index)
    {
        if (int.TryParse(index, out var value) && value is >= 0 and <= 6) SelectedTriggerTypeIndex = value;
    }

    [RelayCommand] private void RefreshManualProcesses() => RefreshAvailableProcesses();

    [RelayCommand]
    private async Task BrowseManualAppAsync()
    {
        var path = RequestFilePicker is null ? null : await RequestFilePicker();
        if (path is not null) { SelectedManualInstalledApp = null; ManualExecutablePath = path; }
    }

    [RelayCommand]
    private void AddManualApp()
    {
        if (!IsLaunchPathValid(ManualExecutablePath))
        { CurrentStatusText = "Elige un ejecutable o script existente antes de añadirlo."; return; }
        var options = new LaunchAppOptions(ManualExecutablePath, ManualAppArguments, ManualAppWorkingDirectory,
            (AppLaunchMode)ManualLaunchModeIndex, ManualSkipIfAlreadyRunning);
        if (ManualLaunchApplications.Any(app => app.Options == options)) return;
        var name = SelectedManualInstalledApp?.ExecutablePath == ManualExecutablePath ? SelectedManualInstalledApp.Name : Path.GetFileNameWithoutExtension(ManualExecutablePath);
        ManualLaunchApplications.Add(new(name, options));
    }

    [RelayCommand] private void RemoveManualApp(ManualLaunchSelection app) => ManualLaunchApplications.Remove(app);

    [RelayCommand]
    private void RefreshManualWindows()
    {
        try
        {
            var selected = SelectedManualWindow;
            var windows = _systemAdapter.GetApplicationWindows();
            ManualAvailableWindows.Clear();
            foreach (var window in windows) ManualAvailableWindows.Add(window);
            SelectedManualWindow = windows.FirstOrDefault(window => window == selected);
            CurrentStatusText = windows.Count == 0 ? "No hay ventanas disponibles para seleccionar." : "Ventanas actualizadas.";
        }
        catch (Exception ex) { CurrentStatusText = $"No se pudieron leer las ventanas: {ex.Message}"; }
    }

    [RelayCommand]
    private void AddManualCloseTarget()
    {
        if (SelectedManualWindow is not { } window) { CurrentStatusText = "Selecciona una ventana para cerrar."; return; }
        var target = new ManualCloseSelection(window, ManualCloseAllWindows);
        if (!ManualCloseApplications.Contains(target)) ManualCloseApplications.Add(target);
    }
    [RelayCommand] private void RemoveManualCloseTarget(ManualCloseSelection target) => ManualCloseApplications.Remove(target);

    private static bool IsLaunchPathValid(string path) => File.Exists(path) &&
        new[] { ".exe", ".bat", ".cmd", ".ps1" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>One immutable snapshot drives verification, preview and execution. No saved routine is edited.</summary>
    public bool TryBuildManualTask(out PresetDefinition? preset, out string summary, out string error)
    {
        preset = null; summary = ""; error = "";
        try
        {
            if (SelectedTriggerTypeIndex is < 0 or > 6) throw new InvalidOperationException("Selecciona cuándo activar la tarea.");
            if (IsTriggerCountdown && ((CountdownHours ?? 0) is < 0 or > 99 || (CountdownMinutes ?? 0) is < 0 or > 59 ||
                (CountdownSeconds ?? 0) is < 0 or > 59 || (CountdownHours ?? 0) * 3600 + (CountdownMinutes ?? 0) * 60 + (CountdownSeconds ?? 0) <= 0 ||
                new[] { CountdownHours ?? 0, CountdownMinutes ?? 0, CountdownSeconds ?? 0 }.Any(v => v != decimal.Truncate(v))))
                throw new InvalidOperationException("Indica un tiempo mayor que cero o selecciona «Ahora».");
            if (IsTriggerExactTime && (ExactTime is null || ExactTime < TimeSpan.Zero || ExactTime >= TimeSpan.FromDays(1)))
                throw new InvalidOperationException("Selecciona una hora concreta válida.");
            if (IsTriggerInactivity && InactivityMinutes is < 1 or > 1440) throw new InvalidOperationException("La inactividad debe estar entre 1 y 1440 minutos.");
            if (IsTriggerProcessExit && (string.IsNullOrWhiteSpace(SelectedProcessName) || DebounceSeconds is < 0 or > 60))
                throw new InvalidOperationException("Selecciona un proceso y un margen entre 0 y 60 segundos.");
            if (IsTriggerAudioSilence && AudioSilenceSeconds is < 1 or > 3600) throw new InvalidOperationException("El silencio debe estar entre 1 y 3600 segundos.");
            if (IsTriggerBatteryState && (!BatteryTriggerOnAcDisconnect && !BatteryTriggerOnThreshold || BatteryTriggerOnThreshold && BatteryThresholdPercent is < 1 or > 100))
                throw new InvalidOperationException("Elige una condición de batería y un porcentaje válido.");
            if (ManualPowerActionIndex is < 0 or > 4) throw new InvalidOperationException("Selecciona una acción de energía válida.");
            if (ManualGracePeriodEnabled && ManualGraceSeconds is < 1 or > 300) throw new InvalidOperationException("El aviso previo debe durar entre 1 y 300 segundos.");
            if (ManualScreenshotEnabled && !_config.Settings.EvidenceScreenshotsEnabled)
                throw new InvalidOperationException("Activa las capturas de evidencia en Ajustes o desmarca la captura de esta tarea.");
            var trigger = SelectedTriggerTypeIndex switch
            {
                1 => BuildFixedTimeTrigger(),
                2 => new TriggerDefinition { Type = TriggerType.UserIdle, Parameters = new() { ["idleMinutes"] = JsonSerializer.SerializeToElement((int)InactivityMinutes) } },
                3 => new() { Type = TriggerType.ProcessExit, Parameters = new() { ["processName"] = JsonSerializer.SerializeToElement(SelectedProcessName), ["debounceSeconds"] = JsonSerializer.SerializeToElement((int)DebounceSeconds) } },
                4 => new() { Type = TriggerType.AudioSilence, Parameters = new() { ["silenceThresholdSeconds"] = JsonSerializer.SerializeToElement((int)AudioSilenceSeconds), ["thresholdPeak"] = JsonSerializer.SerializeToElement(0.001) } },
                5 => new() { Type = TriggerType.BatteryState, Parameters = new() { ["onAcDisconnect"] = JsonSerializer.SerializeToElement(BatteryTriggerOnAcDisconnect), ["batteryLevelThreshold"] = JsonSerializer.SerializeToElement(BatteryTriggerOnThreshold ? (int)BatteryThresholdPercent : 0) } },
                6 => new() { Type = TriggerType.Manual },
                _ => BuildCountdownTrigger()
            };
            var steps = new List<PipelineStepDefinition>();
            var labels = new List<string>();
            void Add(ActionType action, string label, Dictionary<string, JsonElement>? parameters = null)
            { steps.Add(new() { StepOrder = steps.Count + 1, ActionType = action, Parameters = parameters }); labels.Add(label); }
            if (ManualScreenshotEnabled) Add(ActionType.CaptureScreenshot, "tomar captura de pantalla");
            if (ManualCloseApplications.Count > 0 || ManualCloseForegroundApps)
                Add(ActionType.CloseSelectedApplications, string.Join(" y ", ManualCloseApplications.Select(t => "cerrar " + t.Label)
                    .Concat(ManualCloseForegroundApps ? new[] { "cerrar la aplicación en primer plano al ejecutar" } : [])), new()
                {
                    ["targets"] = JsonSerializer.SerializeToElement(ManualCloseApplications.Select(t => new ApplicationCloseTarget(t.Window, t.AllApplicationWindows)).ToArray(),
                        Nokto.Core.Serialization.NoktoJsonContext.Default.ApplicationCloseTargetArray),
                    ["foregroundAtExecution"] = JsonSerializer.SerializeToElement(ManualCloseForegroundApps)
                });
            foreach (var app in ManualLaunchApplications)
            {
                if (!IsLaunchPathValid(app.Options.ExecutablePath)) throw new InvalidOperationException($"Ya no existe la aplicación seleccionada: {app.Name}.");
                Add(ActionType.LaunchApp, "abrir " + app.Name, new()
                {
                    ["executablePath"] = JsonSerializer.SerializeToElement(app.Options.ExecutablePath),
                    ["arguments"] = JsonSerializer.SerializeToElement(app.Options.Arguments),
                    ["workingDirectory"] = JsonSerializer.SerializeToElement(app.Options.WorkingDirectory ?? ""),
                    ["launchMode"] = JsonSerializer.SerializeToElement(app.Options.LaunchMode.ToString()),
                    ["skipIfAlreadyRunning"] = JsonSerializer.SerializeToElement(app.Options.SkipIfAlreadyRunning)
                });
            }
            if (ManualAudioFadeEnabled) Add(ActionType.AudioFadeOut, "bajar volumen a 0% en 15 segundos", new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(15), ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(0) });
            if (ManualMuteOutput) Add(ActionType.MuteAudio, "silenciar salida de audio", new() { ["muted"] = JsonSerializer.SerializeToElement(true) });
            if (ManualMuteMicrophone) Add(ActionType.MuteMicrophone, "silenciar micrófono");
            if (ManualPauseMedia) Add(ActionType.MediaControl, "pausar multimedia");
            if (ManualTurnOffMonitors) Add(ActionType.TurnOffMonitors, "apagar monitores");
            if (ManualLockSession) Add(ActionType.LockWorkstation, "bloquear sesión");
            var terminal = ManualPowerActionIndex switch { 1 => TerminalActionType.Shutdown, 2 => TerminalActionType.Sleep, 3 => TerminalActionType.Hibernate, 4 => TerminalActionType.Restart, _ => TerminalActionType.None };
            if (terminal != TerminalActionType.None) labels.Add(ManualPowerActionOptions[ManualPowerActionIndex].ToLowerInvariant());
            if (labels.Count == 0) throw new InvalidOperationException("Selecciona al menos una acción para activar la tarea.");
            int grace = ManualGracePeriodEnabled ? (int)ManualGraceSeconds : 0;
            string when = SelectedTriggerTypeIndex switch
            {
                0 => $"Dentro de {CountdownHours ?? 0:0} h {CountdownMinutes ?? 0:0} min {CountdownSeconds ?? 0:0} s",
                1 => ExactTimeSummaryText.Split(" (", StringSplitOptions.None)[0],
                2 => $"Tras {InactivityMinutes:0} minutos de inactividad",
                3 => $"Cuando termine {SelectedProcessName} (margen: {DebounceSeconds:0} s)",
                4 => $"Tras {AudioSilenceSeconds:0} segundos de silencio",
                5 => "Cuando " + string.Join(" o ", (BatteryTriggerOnAcDisconnect ? new[] { "se desconecte la corriente" } : Array.Empty<string>()).Concat(BatteryTriggerOnThreshold ? new[] { $"la batería sea ≤ {BatteryThresholdPercent:0}%" } : [])),
                _ => "Ahora"
            };
            summary = $"{when}: {string.Join("; ", labels)}. " + (grace > 0 ? $"Después de cumplirse la condición, aviso previo: {grace} segundos." : "Sin aviso previo.") +
                (ManualForceClose && IsManualForcedCloseAvailable ? " Cierre forzado permitido durante el apagado o reinicio." : "");
            preset = new()
            {
                Id = "manual_" + Guid.NewGuid().ToString("N"), Name = "Tarea manual " + DateTime.Now.ToString("HH:mm:ss"),
                Description = summary, Trigger = trigger, Pipeline = steps,
                TerminalAction = new() { Type = terminal, Parameters = new()
                {
                    ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(grace),
                    ["graceBeforeActions"] = JsonSerializer.SerializeToElement(true),
                    ["forced"] = JsonSerializer.SerializeToElement(ManualForceClose && IsManualForcedCloseAvailable)
                } }
            };
            return true;
        }
        catch (InvalidOperationException ex) { error = ex.Message; return false; }
    }

    [RelayCommand]
    private void VerifyManualTask() => CurrentStatusText = TryBuildManualTask(out _, out var summary, out var error)
        ? "Tarea comprobada. " + summary : error;
}
