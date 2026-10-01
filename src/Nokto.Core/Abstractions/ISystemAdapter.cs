using Nokto.Core.Models;

namespace Nokto.Core.Abstractions;

/// <summary>
/// Contrato abstracto para interacción nativa con el sistema operativo anfitrión.
/// Permite desacoplar completamente la lógica de negocio y UI de las APIs de Windows o macOS.
/// </summary>
public interface ISystemAdapter : IDisposable
{
    /// <summary>
    /// Modifica el estado de energía o sesión del sistema operativo.
    /// </summary>
    Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Controla la señal de alimentación de los monitores conectados sin suspender el equipo.
    /// </summary>
    Task SetDisplayPowerAsync(bool turnOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Modifica gradualmente el volumen maestro del sistema siguiendo una curva logarítmica/perceptual suave.
    /// </summary>
    Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el nivel de volumen maestro actual en rango [0.0f, 1.0f].
    /// </summary>
    Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Establece inmediatamente el nivel de volumen maestro en rango [0.0f, 1.0f].
    /// </summary>
    Task SetMasterVolumeAsync(float volume, CancellationToken cancellationToken = default);

    /// <summary>
    /// Silencia o desactiva el silencio del canal de audio maestro.
    /// </summary>
    Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta si el audio maestro se encuentra actualmente silenciado.
    /// </summary>
    Task<bool> GetMuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite una única señal atómica de Keep-Alive (pulsación de tecla virtual o microdesplazamiento de ratón).
    /// </summary>
    void SimulateKeepAlivePulse(KeepAliveMode mode);

    /// <summary>
    /// Ejecuta el bucle de mantenimiento de actividad con PeriodicTimer y jitter pseudoaleatorio.
    /// </summary>
    Task RunKeepAliveLoopAsync(
        KeepAliveMode mode,
        int jitterMinSeconds,
        int jitterMaxSeconds,
        Action<string>? onActivityLogged,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un snapshot instantáneo de métricas pasivas del sistema (CPU, red, RAM, inactividad).
    /// </summary>
    SystemMetrics GetCurrentMetrics();

    /// <summary>
    /// Modo de prueba seguro (Dry-Run): evita la ejecución de llamadas reales destructivas de energía.
    /// </summary>
    bool IsDryRunMode { get; set; }

    /// <summary>
    /// Obtiene el nivel de pico maestro de audio actual en rango [0.0f, 1.0f] mediante WASAPI metering.
    /// </summary>
    float GetMasterPeakValue();

    /// <summary>
    /// Obtiene el estado actual de la batería y la fuente de alimentación AC.
    /// </summary>
    BatteryStatus GetBatteryStatus();

    /// <summary>
    /// Captura el contenido visual del escritorio completo (multi-monitor).
    /// </summary>
    Task<byte[]> CaptureScreenAsync(bool stampMetadata = false, string? label = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía una señal de control multimedia nativa (reproducir/pausar o detener) al sistema operativo.
    /// </summary>
    void SendMediaControl(bool pauseOnly = true);
}
