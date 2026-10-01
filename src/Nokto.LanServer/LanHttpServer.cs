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
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private byte[]? _cachedPwaHtmlBytes;
    private bool _isDisposed;

    public bool IsRunning => _listener?.IsListening == true;
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

        _listener = new HttpListener();

        // Intenta prefijo genérico o específico según permisos
        try
        {
            _listener.Prefixes.Add($"http://*:{_settings.Port}/");
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Intento 2: con IP local + localhost + 127.0.0.1
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
                // Intento 3: fallback estricto a localhost y 127.0.0.1 (Windows no exige elevación ni urlacl)
                _listener.Close();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_settings.Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_settings.Port}/");
                _listener.Start();
            }
        }

        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
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
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
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
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.Address.ToString();
                        if (!ipStr.StartsWith("169.254")) // Omitir APIPA
                        {
                            return ipStr;
                        }
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
