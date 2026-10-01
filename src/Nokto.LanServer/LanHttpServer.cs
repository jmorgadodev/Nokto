using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Serialization;

namespace Nokto.LanServer;

public sealed class LanHttpServer : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly IWorkflowEngine _engine;
    private readonly ISystemAdapter _systemAdapter;
    private readonly LanServerSettings _settings;
    private HttpListener? _listener;
    private TcpListener? _tcpBridge;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private Task? _tcpBridgeTask;
    private byte[]? _cachedPwaHtmlBytes;
    private bool _isDisposed;
    private DateTime _lastClientActivity = DateTime.UtcNow;

    public event Action? ServerStateChanged;

    public bool IsRunning => (_listener?.IsListening == true) || (_tcpBridge != null);
    public int Port => _settings.Port;
    public string AuthToken => _settings.AuthToken;

    public LanHttpServer(IWorkflowEngine engine, ISystemAdapter systemAdapter, LanServerSettings? settings = null)
    {
        _engine = engine;
        _systemAdapter = systemAdapter;
        _settings = settings ?? new LanServerSettings();
        LoadEmbeddedPwa();
    }

    private void LoadEmbeddedPwa()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("Nokto.LanServer.Embedded.pwa.html");
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _cachedPwaHtmlBytes = ms.ToArray();
            }
        }
        catch
        {
            _cachedPwaHtmlBytes = null;
        }

        if (_cachedPwaHtmlBytes == null || _cachedPwaHtmlBytes.Length == 0)
        {
            string fallback = "<!DOCTYPE html><html><body><h1>Nokto Remote</h1></body></html>";
            _cachedPwaHtmlBytes = Utf8NoBom.GetBytes(fallback);
        }
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            _listener = new HttpListener();

            // Intento 1: Prefijo comodín universal "http://+:{port}/" (requiere urlacl o ejecución como Administrador)
            try
            {
                _listener.Prefixes.Add($"http://+:{_settings.Port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Intento 2: Enlace explícito a IP local + Loopback (en caso de que la IP local esté autorizada)
                _listener.Close();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_settings.Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_settings.Port}/");

                string localIp = GetLocalIpAddress();
                if (localIp != "127.0.0.1")
                {
                    _listener.Prefixes.Add($"http://{localIp}:{_settings.Port}/");
                }

                try
                {
                    _listener.Start();
                }
                catch (HttpListenerException)
                {
                    // Intento 3: Modo puente determinista no-elevado (Winsock / TcpListener en 0.0.0.0 + HttpListener en Loopback)
                    // En Windows sin urlacl, http.sys deniega el enlace a la IP local para usuarios estándar.
                    // Para que los teléfonos móviles en la LAN puedan conectarse sin requerir ejecutar 'netsh' como Administrador,
                    // iniciamos un puente TCP de alto rendimiento que reenvía el tráfico entrante en 0.0.0.0:Port hacia 127.0.0.1:InternalPort.
                    int internalPort = _settings.Port == 65535 ? 4883 : _settings.Port + 1;
                    _listener.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{internalPort}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{internalPort}/");

                    try
                    {
                        _listener.Start();
                        _tcpBridgeTask = Task.Run(() => StartTcpBridgeAsync(_settings.Port, internalPort, _cts.Token));
                    }
                    catch (Exception)
                    {
                        // Fallback de emergencia a Loopback estándar en el puerto configurado
                        try { _tcpBridge?.Stop(); } catch { }
                        _tcpBridge = null;
                        _listener.Close();
                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://localhost:{_settings.Port}/");
                        _listener.Prefixes.Add($"http://127.0.0.1:{_settings.Port}/");
                        _listener.Start();
                    }
                }
            }

            _lastClientActivity = DateTime.UtcNow;
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            _ = Task.Run(() => MonitorInactivityAsync(_cts.Token));
            ServerStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudo iniciar el microservidor LAN: {ex.Message}");
            try { _listener?.Close(); } catch { }
            _listener = null;
            ServerStateChanged?.Invoke();
        }
    }

    private async Task MonitorInactivityAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                if (DateTime.UtcNow - _lastClientActivity > TimeSpan.FromMinutes(5))
                {
                    Debug.WriteLine("[LAN Server] Auto-detención por inactividad de clientes móviles (5 minutos transcurridos).");
                    Stop();
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task StartTcpBridgeAsync(int publicPort, int targetPort, CancellationToken ct)
    {
        try
        {
            _tcpBridge = new TcpListener(IPAddress.Any, publicPort);
            _tcpBridge.Start();

            while (!ct.IsCancellationRequested)
            {
                var client = await _tcpBridge.AcceptTcpClientAsync(ct);
                _lastClientActivity = DateTime.UtcNow;
                _ = Task.Run(async () =>
                {
                    using (client)
                    using (var localClient = new TcpClient())
                    {
                        try
                        {
                            await localClient.ConnectAsync(IPAddress.Loopback, targetPort, ct);
                            using var clientStream = client.GetStream();
                            using var localStream = localClient.GetStream();

                            var copyToLocal = clientStream.CopyToAsync(localStream, ct);
                            var copyToClient = localStream.CopyToAsync(clientStream, ct);

                            await Task.WhenAny(copyToLocal, copyToClient);
                        }
                        catch
                        {
                        }
                    }
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LAN TCP Bridge] Detenido: {ex.Message}");
        }
        finally
        {
            try { _tcpBridge?.Stop(); } catch { }
            _tcpBridge = null;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        try
        {
            _tcpBridge?.Stop();
        }
        catch
        {
        }
        finally
        {
            _tcpBridge = null;
        }

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
        }
        finally
        {
            _listener = null;
        }

        ServerStateChanged?.Invoke();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _lastClientActivity = DateTime.UtcNow;
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Continue listening
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Cabeceras CORS estándar
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Nokto-Auth");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        string rawPath = request.Url?.AbsolutePath ?? "/";

        // 1. Servir PWA Single-Page (GET /)
        if (rawPath == "/" || rawPath == "/index.html")
        {
            response.ContentType = "text/html; charset=utf-8";
            response.StatusCode = 200;
            if (_cachedPwaHtmlBytes != null)
            {
                await response.OutputStream.WriteAsync(_cachedPwaHtmlBytes);
            }
            response.Close();
            return;
        }

        // 2. Validación de Autenticación para rutas /api/
        if (rawPath.StartsWith("/api/"))
        {
            if (_settings.RequireAuth && !IsAuthorized(request))
            {
                response.StatusCode = 401;
                response.ContentType = "application/json";
                byte[] unauthBytes = Utf8NoBom.GetBytes("{}");
                await response.OutputStream.WriteAsync(unauthBytes);
                response.Close();
                return;
            }

            try
            {
                await RouteApiRequestAsync(rawPath, request, response);
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                byte[] errBytes = Utf8NoBom.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
                await response.OutputStream.WriteAsync(errBytes);
            }
            finally
            {
                response.Close();
            }
            return;
        }

        // 404 para cualquier otra ruta
        response.StatusCode = 404;
        response.Close();
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        string? headerAuth = request.Headers["X-Nokto-Auth"];
        string? queryAuth = request.QueryString["auth"];

        string token = headerAuth ?? queryAuth ?? "";
        return string.Equals(token, _settings.AuthToken, StringComparison.Ordinal);
    }

    private async Task RouteApiRequestAsync(string path, HttpListenerRequest req, HttpListenerResponse res)
    {
        switch (path)
        {
            case "/api/status":
                if (req.HttpMethod != "GET") { res.StatusCode = 405; return; }
                var status = _engine.GetStatusSnapshot();
                byte[] statusBytes = JsonSerializer.SerializeToUtf8Bytes(status, NoktoJsonContext.Default.SystemStatusState);
                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(statusBytes);
                break;

            case "/api/screen-preview":
                if (req.HttpMethod != "GET") { res.StatusCode = 405; return; }
                byte[] screenBytes = await _systemAdapter.CaptureScreenAsync();
                res.ContentType = "image/bmp";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(screenBytes);
                break;

            case "/api/action/abort":
                if (req.HttpMethod != "POST") { res.StatusCode = 405; return; }
                _engine.Abort();
                res.ContentType = "application/json";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(Utf8NoBom.GetBytes("{\"success\":true}"));
                break;

            case "/api/action/postpone":
                if (req.HttpMethod != "POST") { res.StatusCode = 405; return; }
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    string body = await reader.ReadToEndAsync();
                    var postponeReq = JsonSerializer.Deserialize(body, NoktoJsonContext.Default.PostponeRequest);
                    int seconds = postponeReq?.Seconds ?? 600;
                    _engine.Postpone(TimeSpan.FromSeconds(seconds));
                }
                res.ContentType = "application/json";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(Utf8NoBom.GetBytes("{\"success\":true}"));
                break;

            case "/api/action/quick-power":
                if (req.HttpMethod != "POST") { res.StatusCode = 405; return; }
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    string body = await reader.ReadToEndAsync();
                    var powerReq = JsonSerializer.Deserialize(body, NoktoJsonContext.Default.QuickPowerRequest);
                    string action = powerReq?.Action ?? "TurnOffMonitors";
                    bool force = powerReq?.Force ?? false;

                    switch (action)
                    {
                        case "TurnOffMonitors":
                            await _systemAdapter.SetDisplayPowerAsync(false);
                            break;
                        case "Shutdown":
                            await _systemAdapter.SetPowerStateAsync(PowerAction.Shutdown, force);
                            break;
                        case "Sleep":
                            await _systemAdapter.SetPowerStateAsync(PowerAction.Sleep, force);
                            break;
                        case "Restart":
                            await _systemAdapter.SetPowerStateAsync(PowerAction.Restart, force);
                            break;
                        case "LockStation":
                        case "LockWorkStation":
                        case "Lock":
                            await _systemAdapter.SetPowerStateAsync(PowerAction.LockStation, force);
                            break;
                        default:
                            res.StatusCode = 400;
                            return;
                    }
                }
                res.ContentType = "application/json";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(Utf8NoBom.GetBytes("{\"success\":true}"));
                break;

            default:
                res.StatusCode = 404;
                break;
        }
    }

    public static string GetLocalIpAddress()
    {
        // 1. Filtrado riguroso de interfaces físicas y descarte de adaptadores virtuales / WSL / Docker
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var candidates = new List<(IPAddress Address, bool HasGateway, bool IsPhysical)>();

            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                string name = ni.Name.ToLowerInvariant();
                string desc = ni.Description.ToLowerInvariant();

                // Descartar adaptadores virtuales, contenedores, emuladores y VPNs
                if (name.Contains("vethernet") || desc.Contains("vethernet") ||
                    name.Contains("wsl") || desc.Contains("wsl") ||
                    name.Contains("docker") || desc.Contains("docker") ||
                    name.Contains("hyper-v") || desc.Contains("hyper-v") ||
                    name.Contains("virtual") || desc.Contains("virtual") ||
                    name.Contains("vmware") || desc.Contains("vmware") ||
                    name.Contains("virtualbox") || desc.Contains("virtualbox") ||
                    name.Contains("tailscale") || desc.Contains("tailscale") ||
                    name.Contains("zerotier") || desc.Contains("zerotier") ||
                    name.Contains("bluetooth") || desc.Contains("bluetooth") ||
                    name.Contains("npcap") || desc.Contains("npcap") ||
                    name.Contains("tap-") || desc.Contains("tap-") ||
                    name.Contains("vpn") || desc.Contains("vpn"))
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                bool hasGateway = ipProps.GatewayAddresses.Any(g => g.Address != null &&
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    g.Address.ToString() != "0.0.0.0");

                bool isPhysical = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                  ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet;

                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = unicast.Address.ToString();
                        if (!IPAddress.IsLoopback(unicast.Address) && !ipStr.StartsWith("169.254."))
                        {
                            candidates.Add((unicast.Address, hasGateway, isPhysical));
                        }
                    }
                }
            }

            var best = candidates
                .OrderByDescending(c => c.HasGateway)
                .ThenByDescending(c => c.IsPhysical)
                .FirstOrDefault();

            if (best.Address != null)
            {
                return best.Address.ToString();
            }
        }
        catch
        {
        }

        // 2. Consulta determinista de enrutamiento al kernel de Windows (UDP connectionless query)
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint &&
                !IPAddress.IsLoopback(endPoint.Address) &&
                !endPoint.Address.ToString().StartsWith("169.254."))
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
        }

        // 3. Fallback a cualquier interfaz no-loopback activa
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address) &&
                        !ip.Address.ToString().StartsWith("169.254."))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch
        {
        }

        return "127.0.0.1";
    }

    public string GetConnectionUrl()
    {
        string ip = GetLocalIpAddress();
        return $"http://{ip}:{_settings.Port}/?auth={_settings.AuthToken}";
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _cts?.Dispose();
            _isDisposed = true;
        }
    }
}
