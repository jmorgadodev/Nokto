using CommunityToolkit.Mvvm.ComponentModel;
using Nokto.Core.Models;

namespace Nokto.UI.ViewModels;

/// <summary>
/// Elemento visual interactivo que representa un paso intermedio de la tubería en Modo Studio.
/// </summary>
public partial class StudioStepItem : ObservableObject
{
    [ObservableProperty]
    private int _stepOrder = 1;

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

    public bool IsScreenshot => ActionType == ActionType.CaptureScreenshot;
    public bool IsAudioFade => ActionType == ActionType.AudioFadeOut;
    public bool IsMediaControl => ActionType == ActionType.MediaControl;
    public bool IsCommand => ActionType == ActionType.ExecuteCommand;
    public bool IsMonitorsOff => ActionType == ActionType.TurnOffMonitors;

    partial void OnActionTypeChanged(ActionType value)
    {
        OnPropertyChanged(nameof(IsScreenshot));
        OnPropertyChanged(nameof(IsAudioFade));
        OnPropertyChanged(nameof(IsMediaControl));
        OnPropertyChanged(nameof(IsCommand));
        OnPropertyChanged(nameof(IsMonitorsOff));

        ActionTitle = value switch
        {
            ActionType.CaptureScreenshot => "Captura de Pantalla Multi-Monitor",
            ActionType.AudioFadeOut => "Desvanecimiento Progresivo WASAPI",
            ActionType.MediaControl => "Pausar Multimedia Activa",
            ActionType.ExecuteCommand => "Ejecutar Comando / Script",
            ActionType.TurnOffMonitors => "Apagar Señal de Monitores",
            ActionType.MuteAudio => "Silenciar Canal Maestro",
            _ => value.ToString()
        };

        ActionDescription = value switch
        {
            ActionType.CaptureScreenshot => "Guarda evidencia con sellado de tiempo en ./data/snapshots/",
            ActionType.AudioFadeOut => $"Atenuación logarítmica ({FadeDurationSeconds}s hasta {TargetVolumePercentage}%)",
            ActionType.MediaControl => "Envía señal nativa de pausa de reproducción multimedia",
            ActionType.ExecuteCommand => "Ejecuta proceso con validación estricta de código de salida",
            ActionType.TurnOffMonitors => "Desconecta la señal de vídeo mediante SC_MONITORPOWER",
            ActionType.MuteAudio => "Activa el silencio en el dispositivo de audio maestro",
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
        }

        item.ActionType = step.ActionType;
        return item;
    }

    public PipelineStepDefinition ToDefinition()
    {
        var dict = new Dictionary<string, System.Text.Json.JsonElement>();
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
                dict["pauseOnly"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
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
