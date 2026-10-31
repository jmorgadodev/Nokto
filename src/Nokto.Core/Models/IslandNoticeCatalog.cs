namespace Nokto.Core.Models;

public sealed record IslandNoticeDefinition(string Id, string Title, string Description, string Group, bool Available = true);

public static class IslandNoticeCatalog
{
    public static IReadOnlyList<IslandNoticeDefinition> All { get; } = [
        new("volume", "Volumen y silencio", "Nivel y estado de la salida de audio.", "system"),
        new("brightness", "Brillo de pantalla", "En pantallas que entregan este evento a Windows.", "system"),
        new("capsLock", "Bloq Mayús", "Al activar o desactivar las mayúsculas.", "keys"),
        new("keyboardLayout", "Idioma del teclado", "Al cambiar la distribución de escritura.", "keys"),
        new("network", "Conexión a Internet", "Pérdida y recuperación de la conexión.", "connection"),
        new("power", "Cargador", "Al conectar o desconectar la alimentación.", "connection"),
        new("audioDevice", "Auriculares y salida de audio", "Cambio de salida o dispositivo de sonido conectado.", "connection"),
        new("microphone", "Silencio del micrófono", "Solo el estado de silencio; nunca escucha ni graba.", "extra"),
        new("numLock", "Bloq Num", "Estado del teclado numérico.", "extra"),
        new("battery", "Batería baja o completa", "Al cruzar 20 %, 10 % o completar la carga.", "extra"),
        new("energySaver", "Ahorro de batería", "Al activar o desactivar el ahorro de energía.", "extra"),
        new("bluetooth", "Bluetooth", "Conexión y desconexión de dispositivos ya emparejados.", "extra"),
        new("storage", "Unidades externas", "Montaje o retirada de una unidad; no examina archivos.", "extra")
    ];

    public static bool IsSensitive(string id) => id is "network" or "power" or "audioDevice" or "bluetooth" or "storage" or "apps" or "mail" or "connection";
}
