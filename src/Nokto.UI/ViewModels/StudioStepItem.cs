using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Models;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Nokto.UI.ViewModels;

/// <summary>
/// Elemento visual interactivo que representa un paso intermedio de la tubería en Modo Studio.
/// </summary>
public partial class StudioStepItem : ObservableObject
{
    public Action<StudioStepItem>? MoveUpRequested { get; set; }
    public Action<StudioStepItem>? MoveDownRequested { get; set; }
    public Action<StudioStepItem>? RemoveRequested { get; set; }

    [RelayCommand]
    public void MoveUp() => MoveUpRequested?.Invoke(this);

    [RelayCommand]
    public void MoveDown() => MoveDownRequested?.Invoke(this);

    [RelayCommand]
    public void Remove() => RemoveRequested?.Invoke(this);

    public IRelayCommand DeleteCommand => RemoveCommand;

    [ObservableProperty]
    private int _stepOrder = 1;

    partial void OnStepOrderChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayOrderText));
    }

    public string DisplayOrderText => $"#{StepOrder}";

    [ObservableProperty]
    private ActionType _actionType = ActionType.TurnOffMonitors;

    [ObservableProperty]
    private string _actionTitle = "Apagar Monitores";

    [ObservableProperty]
    private string _actionDescription = "Corte instantáneo de señal de vídeo";

    [ObservableProperty]
    private bool _ignoreFailure = true;

    // Parámetros específicos editables:
    [ObservableProperty]
    private int _fadeDurationSeconds = 15;

    [ObservableProperty]
    private int _targetVolumePercentage = 0;

    [ObservableProperty]
    private string _commandExecutable = "cmd.exe";

    [ObservableProperty]
    private string _commandArguments = "/c echo Finalizando tarea...";

    [ObservableProperty]
    private int _commandTimeoutSeconds = 30;

    [ObservableProperty]
    private int _expectedExitCode = 0;

    [ObservableProperty]
    private int _waitDelaySeconds = 10;

    private Dictionary<string, JsonElement> _originalParameters = [];
    private IReadOnlyList<InstalledAppInfo> _catalog = [];
    public ObservableCollection<InstalledAppInfo> FilteredInstalledApps { get; } = [];
    public Func<Task<string?>>? BrowseFileRequested { get; set; }
    [ObservableProperty] private string _appSearchText = "";
    [ObservableProperty] private InstalledAppInfo? _selectedInstalledApp;
    [ObservableProperty] private string _launchExecutablePath = "";
    [ObservableProperty] private string _launchArguments = "";
    [ObservableProperty] private string? _launchWorkingDirectory;
    [ObservableProperty] private int _launchModeIndex;
    [ObservableProperty] private bool _skipIfAlreadyRunning = true;
    [ObservableProperty] private int _audioVolumePercent = 50;
    [ObservableProperty] private bool _muteOutput;
    [ObservableProperty] private bool _muteMicrophone;
    [ObservableProperty] private int _mediaModeIndex;
    [ObservableProperty] private decimal _delayAmount = 10;
    [ObservableProperty] private int _delayUnitIndex;
    [ObservableProperty] private int _powerActionIndex;
    [ObservableProperty] private decimal _gracePeriodSeconds = 30;
    [ObservableProperty] private bool _forceClose;
    [ObservableProperty] private decimal _keepAliveHours = 8;
    public bool IsLaunchApp => ActionType == ActionType.LaunchApp;
    public bool IsAudioConfig => ActionType == ActionType.AudioConfig;
    public bool IsPowerAction => ActionType == ActionType.PowerAction;

    partial void OnAppSearchTextChanged(string value) => FilterApps();
    partial void OnLaunchExecutablePathChanged(string value)
    {
        if (SelectedInstalledApp is not null && !string.Equals(value, SelectedInstalledApp.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            SelectedInstalledApp = null;
        LaunchArguments = "";
        LaunchWorkingDirectory = null;
    }
    partial void OnSelectedInstalledAppChanged(InstalledAppInfo? value)
    {
        if (value is null) return;
        LaunchExecutablePath = value.ExecutablePath;
        LaunchArguments = value.Arguments;
        LaunchWorkingDirectory = value.WorkingDirectory;
    }

    public void SetInstalledApps(IReadOnlyList<InstalledAppInfo> apps)
    {
        _catalog = apps;
        FilterApps();
        SelectedInstalledApp = apps.FirstOrDefault(app => string.Equals(app.ExecutablePath, LaunchExecutablePath, StringComparison.OrdinalIgnoreCase) && app.Arguments == LaunchArguments);
    }

    private void FilterApps()
    {
        var selected = SelectedInstalledApp;
        FilteredInstalledApps.Clear();
        foreach (var app in _catalog.Where(app => app.Name.Contains(AppSearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            FilteredInstalledApps.Add(app);
        if (selected is not null && FilteredInstalledApps.Contains(selected)) SelectedInstalledApp = selected;
    }

    [RelayCommand]
    public async Task BrowseAppAsync()
    {
        if (BrowseFileRequested is null) return;
        string? path = await BrowseFileRequested();
        if (string.IsNullOrWhiteSpace(path)) return;
        SelectedInstalledApp = null;
        LaunchExecutablePath = path;
        LaunchArguments = "";
        LaunchWorkingDirectory = null;
    }

    public bool IsScreenshot => ActionType == ActionType.CaptureScreenshot;
    public bool IsAudioFade => ActionType == ActionType.AudioFadeOut;
    public bool IsMediaControl => ActionType == ActionType.MediaControl;
    public bool IsCommand => ActionType == ActionType.ExecuteCommand;
    public bool IsMonitorsOff => ActionType == ActionType.TurnOffMonitors;
    public bool IsWaitDelay => ActionType == ActionType.WaitDelay;
    public bool IsKeepAlive => ActionType == ActionType.KeepAliveEngine;

    public string DisplayName => ActionType switch
    {
        ActionType.CaptureScreenshot => "Captura de pantalla",
        ActionType.AudioFadeOut => "Bajar volumen gradualmente",
        ActionType.MediaControl => "Pausar música",
        ActionType.WaitDelay => "Esperar tiempo",
        ActionType.ExecuteCommand => "Ejecutar comando/script",
        ActionType.TurnOffMonitors => "Apagar monitores",
        ActionType.MuteAudio => "Silenciar audio",
        ActionType.KeepAliveEngine => "Mantener equipo activo",
        ActionType.LaunchApp => "Ejecutar programa",
        ActionType.AudioConfig => "Configurar audio",
        ActionType.PowerAction => "Energía / sesión",
        ActionType.LockWorkstation => "Bloquear sesión",
        ActionType.MuteMicrophone => "Silenciar micrófono",
        ActionType.CloseForegroundApplications => "Cerrar aplicación en primer plano",
        _ => ActionType.ToString()
    };

    partial void OnActionTypeChanged(ActionType value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsScreenshot));
        OnPropertyChanged(nameof(IsAudioFade));
        OnPropertyChanged(nameof(IsMediaControl));
        OnPropertyChanged(nameof(IsCommand));
        OnPropertyChanged(nameof(IsMonitorsOff));
        OnPropertyChanged(nameof(IsWaitDelay));
        OnPropertyChanged(nameof(IsKeepAlive));
        OnPropertyChanged(nameof(IsLaunchApp));
        OnPropertyChanged(nameof(IsAudioConfig));
        OnPropertyChanged(nameof(IsPowerAction));

        ActionTitle = value switch
        {
            ActionType.CaptureScreenshot => "Captura de Pantalla Multi-Monitor",
            ActionType.AudioFadeOut => "Bajar volumen gradualmente",
            ActionType.MediaControl => "Pausar Multimedia Activa",
            ActionType.ExecuteCommand => "Ejecutar Comando / Script",
            ActionType.TurnOffMonitors => "Apagar Señal de Monitores",
            ActionType.MuteAudio => "Silenciar Canal Maestro",
            ActionType.WaitDelay => "Esperar Tiempo",
            ActionType.KeepAliveEngine => "Mantener equipo activo",
            _ => value.ToString()
        };

        ActionDescription = value switch
        {
            ActionType.CaptureScreenshot => "Guarda evidencia con sellado de tiempo en ./data/snapshots/",
            ActionType.AudioFadeOut => $"Atenuación logarítmica ({FadeDurationSeconds}s hasta {TargetVolumePercentage}%)",
            ActionType.MediaControl => "Envía señal nativa de pausa de reproducción multimedia",
            ActionType.ExecuteCommand => "Ejecuta proceso con validación estricta de código de salida",
            ActionType.TurnOffMonitors => "Apaga las pantallas sin suspender el equipo",
            ActionType.MuteAudio => "Activa el silencio en el dispositivo de audio maestro",
            ActionType.WaitDelay => $"Pausa la ejecución durante {WaitDelaySeconds} segundos",
            ActionType.KeepAliveEngine => "Evita que el equipo entre en reposo o marque el estado Ausente",
            _ => ""
        };
    }

    public static StudioStepItem FromDefinition(PipelineStepDefinition step)
    {
        var item = new StudioStepItem
        {
            StepOrder = step.StepOrder,
            IgnoreFailure = step.IgnoreFailure
        };

        if (step.Parameters != null)
        {
            if (step.Parameters.TryGetValue("durationSeconds", out var dur) && dur.TryGetInt32(out int d))
                item.FadeDurationSeconds = d;
            if (step.Parameters.TryGetValue("targetVolumePercentage", out var vol) && vol.TryGetInt32(out int v))
                item.TargetVolumePercentage = v;
            if (step.Parameters.TryGetValue("executablePath", out var exe))
                item.CommandExecutable = exe.GetString() ?? "cmd.exe";
            if (step.Parameters.TryGetValue("arguments", out var args))
                item.CommandArguments = args.GetString() ?? "";
            if (step.Parameters.TryGetValue("timeoutSeconds", out var tout) && tout.TryGetInt32(out int to))
                item.CommandTimeoutSeconds = to;
            if (step.Parameters.TryGetValue("expectedExitCode", out var code) && code.TryGetInt32(out int c))
                item.ExpectedExitCode = c;
            if (step.Parameters.TryGetValue("delaySeconds", out var wdel) && wdel.TryGetInt32(out int wd))
                item.WaitDelaySeconds = wd;
        }

        item.ActionType = step.ActionType;
        item._originalParameters = WorkflowDefinition.CloneParameters(step.Parameters) ?? [];
        var parameters = item._originalParameters;
        string Text(string key, string fallback = "") => parameters.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
        int Number(string key, int fallback) => parameters.TryGetValue(key, out var value) && value.TryGetInt32(out int number) ? number : fallback;
        bool Flag(string key, bool fallback) => parameters.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
        item.LaunchExecutablePath = Text("executablePath");
        item.LaunchArguments = Text("arguments");
        item.LaunchWorkingDirectory = Text("workingDirectory");
        item.LaunchModeIndex = Enum.TryParse<AppLaunchMode>(Text("launchMode"), out var mode) ? (int)mode : 0;
        item.SkipIfAlreadyRunning = Flag("skipIfAlreadyRunning", true);
        item.AudioVolumePercent = Number("volumePercent", 50);
        item.MuteOutput = Flag("muteOutput", false);
        item.MuteMicrophone = Flag("muteMicrophone", false);
        item.MediaModeIndex = Flag("pauseOnly", true) ? 0 : 1;
        item.DelayAmount = item.WaitDelaySeconds;
        item.GracePeriodSeconds = Math.Clamp(Number("gracePeriodSeconds", 30), 0, 300);
        item.ForceClose = Flag("forced", false);
        item.KeepAliveHours = parameters.TryGetValue("delayHours", out var hours) && hours.TryGetDecimal(out var hourValue) ? hourValue : 8;
        item.PowerActionIndex = Text("powerAction", "Shutdown") switch { "Sleep" => 1, "Hibernate" => 2, "Restart" => 3, "LockStation" => 4, "Logoff" => 5, "TurnOffMonitors" => 6, _ => 0 };
        return item;
    }

    public PipelineStepDefinition ToDefinition()
    {
        var dict = WorkflowDefinition.CloneParameters(_originalParameters) ?? [];
        switch (ActionType)
        {
            case ActionType.AudioFadeOut:
                dict["durationSeconds"] = System.Text.Json.JsonSerializer.SerializeToElement(FadeDurationSeconds);
                dict["targetVolumePercentage"] = System.Text.Json.JsonSerializer.SerializeToElement(TargetVolumePercentage);
                break;
            case ActionType.ExecuteCommand:
                dict["executablePath"] = System.Text.Json.JsonSerializer.SerializeToElement(CommandExecutable);
                dict["arguments"] = System.Text.Json.JsonSerializer.SerializeToElement(CommandArguments);
                dict["timeoutSeconds"] = System.Text.Json.JsonSerializer.SerializeToElement(CommandTimeoutSeconds);
                dict["expectedExitCode"] = System.Text.Json.JsonSerializer.SerializeToElement(ExpectedExitCode);
                break;
            case ActionType.MediaControl:
                dict["pauseOnly"] = JsonSerializer.SerializeToElement(MediaModeIndex == 0);
                break;
            case ActionType.WaitDelay:
                dict["delaySeconds"] = JsonSerializer.SerializeToElement(checked((int)(DelayAmount * (DelayUnitIndex == 1 ? 60 : 1))));
                break;
            case ActionType.LaunchApp:
                dict["executablePath"] = JsonSerializer.SerializeToElement(LaunchExecutablePath);
                dict["arguments"] = JsonSerializer.SerializeToElement(LaunchArguments);
                dict["workingDirectory"] = JsonSerializer.SerializeToElement(LaunchWorkingDirectory);
                dict["launchMode"] = JsonSerializer.SerializeToElement(((AppLaunchMode)LaunchModeIndex).ToString());
                dict["skipIfAlreadyRunning"] = JsonSerializer.SerializeToElement(SkipIfAlreadyRunning);
                break;
            case ActionType.AudioConfig:
                dict["volumePercent"] = JsonSerializer.SerializeToElement(AudioVolumePercent);
                dict["muteOutput"] = JsonSerializer.SerializeToElement(MuteOutput);
                dict["muteMicrophone"] = JsonSerializer.SerializeToElement(MuteMicrophone);
                break;
            case ActionType.PowerAction:
                dict["powerAction"] = JsonSerializer.SerializeToElement(PowerActionIndex switch { 1 => "Sleep", 2 => "Hibernate", 3 => "Restart", 4 => "LockStation", 5 => "Logoff", 6 => "TurnOffMonitors", _ => "Shutdown" });
                dict["gracePeriodSeconds"] = JsonSerializer.SerializeToElement((int)GracePeriodSeconds);
                dict["forced"] = JsonSerializer.SerializeToElement(ForceClose);
                break;
            case ActionType.KeepAliveEngine:
                dict["delayHours"] = JsonSerializer.SerializeToElement(KeepAliveHours);
                break;
        }

        return new PipelineStepDefinition
        {
            StepOrder = StepOrder,
            ActionType = ActionType,
            Parameters = dict.Count > 0 ? dict : null,
            IgnoreFailure = IgnoreFailure
        };
    }
}
