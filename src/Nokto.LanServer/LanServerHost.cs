namespace Nokto.LanServer;

/// <summary>
/// Microservidor LAN embebido basado en HttpListener (preparado para Fase 4).
/// </summary>
public sealed class LanServerHost
{
    public bool IsRunning { get; private set; }

    public void Start(int port, string bindAddress)
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }
}
