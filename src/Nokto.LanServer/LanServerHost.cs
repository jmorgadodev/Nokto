using System.Diagnostics;

namespace Nokto.LanServer;

/// <summary>
/// Microservidor LAN embebido y utilidades de configuración de red y Firewall.
/// </summary>
public sealed class LanServerHost
{
    public const int DefaultPort = 4884;
    public const string FirewallRuleName = "Nokto LAN Remote";

    public bool IsRunning { get; private set; }

    public void Start(int port, string bindAddress)
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    /// <summary>
    /// Retorna el comando oficial netsh para habilitar el puerto en el Firewall de Windows.
    /// </summary>
    public static string GetFirewallCommand(int port = DefaultPort) =>
        $"netsh advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port}";

    /// <summary>
    /// Retorna el comando netsh para reservar la URL en http.sys (en caso de usar http://+:port/).
    /// </summary>
    public static string GetUrlAclCommand(int port = DefaultPort) =>
        $"netsh http add urlacl url=http://+:{port}/ user=Everyone";

    /// <summary>
    /// Intenta ejecutar la configuración del Firewall de Windows solicitando elevación si es necesario.
    /// </summary>
    public static bool TryConfigureFirewall(int port = DefaultPort)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
