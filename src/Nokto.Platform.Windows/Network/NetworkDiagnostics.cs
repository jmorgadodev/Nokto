using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows.Networking.Connectivity;

namespace Nokto.Platform.Windows.Network;

public record NetworkInfoSnapshot(
    string IpAddress,
    string NetworkNameAndType,
    bool IsVpnActive,
    string VpnStatusText
)
{
    public string ConnectedNetworkName { get; init; } = "Desconectado";
    public string InterfaceNameAndType { get; init; } = "Desconectado";
}

/// <summary>
/// Diagnóstico pasivo y 100% offline de conectividad LAN y estado de VPN.
/// No abre sockets, no realiza peticiones HTTP, cero alertas de Firewall de Windows.
/// </summary>
public static class NetworkDiagnostics
{
    private static readonly string[] VpnKeywords =
    [
        "wireguard", "tailscale", "openvpn", "tap", "tun", "anyconnect",
        "fortinet", "nordvpn", "surfshark", "proton", "zerotier", "wintun", "vpn"
    ];

    public static NetworkInfoSnapshot GetSnapshot()
    {
        string ipAddress = "127.0.0.1";
        string networkNameAndType = "Desconectado";
        string connectedNetworkName = "Desconectado";
        string interfaceNameAndType = "Desconectado";
        bool isVpnActive = false;
        string vpnStatusText = "VPN: Desconectada (Tráfico directo)";

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            // 1. Detección pasiva de VPN
            foreach (var iface in interfaces)
            {
                string desc = iface.Description.ToLowerInvariant();
                string name = iface.Name.ToLowerInvariant();

                bool matchesVpn = iface.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                                  VpnKeywords.Any(k => desc.Contains(k) || name.Contains(k));

                if (matchesVpn)
                {
                    isVpnActive = true;
                    vpnStatusText = $"VPN: Conectada ({iface.Description})";
                    break;
                }
            }

            // 2. Obtención de interfaz física principal y su IPv4
            Guid? internetAdapterId = GetInternetAdapterId();
            var physicalCandidate = interfaces
                .Where(i => !IsVirtualOrVpn(i))
                .OrderByDescending(i => Guid.TryParse(i.Id, out var id) && id == internetAdapterId)
                .ThenByDescending(i => i.Speed)
                .FirstOrDefault();

            if (physicalCandidate == null)
            {
                physicalCandidate = interfaces.FirstOrDefault(i => !IsLoopback(i));
            }

            if (physicalCandidate != null)
            {
                // Buscar IPv4 válida
                var ipProps = physicalCandidate.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                         !IPAddress.IsLoopback(a.Address) &&
                                         !a.Address.ToString().StartsWith("169.254.") &&
                                         !a.Address.ToString().StartsWith("127."));

                if (ipv4 != null)
                {
                    ipAddress = ipv4.Address.ToString();
                }

                // El nombre de la red pertenece al perfil activo del mismo adaptador,
                // nunca a una red guardada ni al alias genérico "Wi-Fi".
                bool isWireless = physicalCandidate.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                string interfaceType = isWireless ? "Wi-Fi" : physicalCandidate.NetworkInterfaceType.ToString();
                interfaceNameAndType = $"{physicalCandidate.Name} ({interfaceType})";
                string? profileName = GetConnectedNetworkName(physicalCandidate.Id);
                connectedNetworkName = profileName ?? "Nombre no disponible";
                networkNameAndType = profileName is null ? interfaceNameAndType : $"{interfaceType} \"{profileName}\"";
            }
        }
        catch
        {
            // Protección ante entornos restringidos
        }

        return new NetworkInfoSnapshot(ipAddress, networkNameAndType, isVpnActive, vpnStatusText)
        {
            ConnectedNetworkName = connectedNetworkName,
            InterfaceNameAndType = interfaceNameAndType
        };
    }

    private static bool IsVirtualOrVpn(NetworkInterface iface)
    {
        string desc = iface.Description.ToLowerInvariant();
        string name = iface.Name.ToLowerInvariant();

        return iface.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
               VpnKeywords.Any(k => desc.Contains(k) || name.Contains(k)) ||
               desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vethernet") ||
               desc.Contains("vmware") || desc.Contains("virtualbox") || desc.Contains("wsl");
    }

    private static bool IsLoopback(NetworkInterface iface)
    {
        return iface.NetworkInterfaceType == NetworkInterfaceType.Loopback;
    }

    private static Guid? GetInternetAdapterId()
    {
        try
        {
            // Consulta el estado almacenado por Windows; no prueba acceso a Internet.
            return NetworkInformation.GetInternetConnectionProfile()?.NetworkAdapter?.NetworkAdapterId;
        }
        catch { }
        return null;
    }

    private static string? GetConnectedNetworkName(string interfaceId)
    {
        if (!Guid.TryParse(interfaceId, out var adapterId)) return null;
        try
        {
            var profile = NetworkInformation.GetConnectionProfiles().FirstOrDefault(p =>
                p.NetworkAdapter?.NetworkAdapterId == adapterId &&
                p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
            if (profile is null) return null;

            // En Windows con ubicación restringida, el SSID puede no ser accesible.
            // El perfil conectado sigue proporcionando el nombre local de la red.
            if (profile.IsWlanConnectionProfile)
            {
                try
                {
                    string ssid = profile.WlanConnectionProfileDetails.GetConnectedSsid();
                    if (!string.IsNullOrWhiteSpace(ssid)) return ssid;
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Runtime.InteropServices.COMException) { }
            }
            return string.IsNullOrWhiteSpace(profile.ProfileName) ? null : profile.ProfileName.Trim();
        }
        catch { return null; }
    }
}
