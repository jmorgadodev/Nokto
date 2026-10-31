using Nokto.Platform.Windows.Media;

var emitted = 0;
using (var service = new WindowsMediaSessionService())
{
    service.Updated += snapshot =>
    {
        Interlocked.Increment(ref emitted);
        Console.WriteLine($"[EVENT] Sesión disponible={snapshot.Available}; reproduce={snapshot.IsPlaying}; duración={snapshot.Duration.TotalSeconds:0}s");
    };
    await service.StartAsync();
    await Task.Delay(450);
    Console.WriteLine($"[PASS] Servicio GSMTC inicializado; sesión presente: {service.Snapshot.Available}; eventos: {emitted}; carátula recibida: {service.Snapshot.Artwork?.Length ?? 0} bytes");
}
Console.WriteLine("[PASS] Servicio desconectado sin modificar reproducción ni volumen.");