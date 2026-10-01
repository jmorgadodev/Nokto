using System.Text.Json.Serialization;

namespace Nokto.Core.Models;

/// <summary>
/// Acciones del sistema para gestión de energía y sesión.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PowerAction>))]
public enum PowerAction
{
    Shutdown,
    Restart,
    Sleep,
    Hibernate,
    LockStation,
    Logoff,
    TurnOffMonitors
}

/// <summary>
/// Modos de prevención de estado ausente (Keep-Alive).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<KeepAliveMode>))]
public enum KeepAliveMode
{
    None,
    InputSimulation,
    ThreadExecutionState,
    Mixed
}

/// <summary>
/// Estados de la máquina de estados reactiva de Nokto.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EngineState>))]
public enum EngineState
{
    Idle,
    WaitingTrigger,
    ExecutingActions,
    GracePeriod,
    Paused,
    Completed,
    Failed
}

/// <summary>
/// Tipos de disparadores soportados por el motor de automatización.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TriggerType>))]
public enum TriggerType
{
    Countdown,
    FixedTime,
    Schedule,
    ProcessExit,
    SustainedLoad,
    NetworkThroughput,
    AudioSilence,
    UserIdle
}

/// <summary>
/// Tipos de acciones intermedias en una tubería (pipeline).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
public enum ActionType
{
    CaptureScreenshot,
    AudioFadeOut,
    MuteAudio,
    MediaControl,
    TurnOffMonitors,
    ExecuteCommand,
    KeepAliveEngine
}

/// <summary>
/// Tipos de acciones terminales de energía al concluir un flujo.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminalActionType>))]
public enum TerminalActionType
{
    Shutdown,
    Sleep,
    Hibernate,
    Restart,
    LockStation,
    Logoff,
    None
}
