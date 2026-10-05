using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Serialization;

namespace Nokto.Core.Remote;

/// <summary>Offline LAN control, paired by a per-start QR key. HTTP.sys is preferred; Kestrel handles portable non-admin hosts.</summary>
public sealed class LocalRemoteServerService(ISystemAdapter adapter, IWorkflowEngine engine,
    Func<CancellationToken, Task<byte[]>> capturePreview) : IAsyncDisposable
{
    private const int MaxBodyBytes = 4096;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(8, 8);
    private readonly HashSet<string> _localHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Task> _requests = new();
    private CancellationTokenSource? _lifetime;
    private HttpListener? _listener;
    private WebApplication? _fallback;
    private Task? _acceptLoop;
    private long _requestId;
    public string SessionKey { get; private set; } = "";
    public int Port { get; private set; }
    public bool IsRunning => _lifetime is not null && !_lifetime.IsCancellationRequested;
    public bool AllowScreenPreview { get; set; } = true;
    public string Transport { get; private set; } = "";

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (IsRunning) throw new InvalidOperationException("El control remoto ya está activo.");
        Port = port;
        SessionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _localHosts.Clear();
        _localHosts.UnionWith(["localhost", "127.0.0.1", "[::1]", "::1"]);
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var address in network.GetIPProperties().UnicastAddresses)
                if (IsLanAddress(address.Address)) _localHosts.Add(address.Address.ToString());
        _lifetime = new CancellationTokenSource();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        try
        {
            listener.Start();
            _listener = listener;
            Transport = "HTTP.sys";
            _acceptLoop = AcceptAsync(_lifetime.Token);
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            listener.Close();
            try
            {
                // HTTP.sys wildcard registrations require a URL ACL. This local transport does not require elevation.
                var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
                {
                    Args = [], ApplicationName = typeof(LocalRemoteServerService).Assembly.FullName,
                    ContentRootPath = AppContext.BaseDirectory
                });
                builder.Configuration.Sources.Clear();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Listen(IPAddress.Any, port);
                    options.Limits.MaxRequestBodySize = MaxBodyBytes;
                    options.Limits.MaxConcurrentConnections = 32;
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
                });
                _fallback = builder.Build();
                var serverToken = _lifetime.Token;
                _fallback.Run(async context =>
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, serverToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    var input = new RequestInput(context.Connection.RemoteIpAddress, context.Request.Method,
                        context.Request.Path.Value ?? "/", context.Request.Host.Value, context.Request.Headers.Origin.ToString(),
                        context.Request.Headers["X-Nokto-Key"].ToString(), context.Request.ContentLength);
                    var reply = await HandleAsync(input, context.Request.Body, timeout.Token);
                    context.Response.StatusCode = reply.Status;
                    context.Response.ContentType = reply.ContentType;
                    context.Response.ContentLength = reply.Body.Length;
                    if (reply.Status == 413) context.Response.Headers.Connection = "close";
                    foreach (var header in SecurityHeaders(reply)) context.Response.Headers[header.Key] = header.Value;
                    await context.Response.Body.WriteAsync(reply.Body, context.RequestAborted);
                });
                await _fallback.StartAsync(cancellationToken).ConfigureAwait(false);
                Transport = "Kestrel";
            }
            catch { await StopAsync().ConfigureAwait(false); throw; }
        }
        catch { listener.Close(); await StopAsync().ConfigureAwait(false); throw; }
    }

    public string GetPairingUrl(string address) => $"http://{address}:{Port}/#key={SessionKey}";

    public static bool IsLanAddress(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private async Task AcceptAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _listener!.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                long id = Interlocked.Increment(ref _requestId);
                var request = ServeListenerAsync(context, ct);
                _requests[id] = request;
                _ = request.ContinueWith(completed => _requests.TryRemove(id, out _), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
    }

    private async Task ServeListenerAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var request = context.Request;
            var input = new RequestInput(request.RemoteEndPoint?.Address, request.HttpMethod, request.Url?.AbsolutePath ?? "/",
                request.Headers["Host"] ?? "", request.Headers["Origin"] ?? "", request.Headers["X-Nokto-Key"] ?? "",
                request.ContentLength64 < 0 ? null : request.ContentLength64);
            var reply = await HandleAsync(input, request.InputStream, timeout.Token);
            context.Response.StatusCode = reply.Status;
            context.Response.ContentType = reply.ContentType;
            context.Response.ContentLength64 = reply.Body.Length;
            if (reply.Status == 413) context.Response.KeepAlive = false;
            foreach (var header in SecurityHeaders(reply)) context.Response.Headers[header.Key] = header.Value;
            await context.Response.OutputStream.WriteAsync(reply.Body, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            try { context.Response.Close(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        }
    }

    private sealed record RequestInput(IPAddress? Address, string Method, string Path, string Host, string Origin,
        string Key, long? Length);
    private sealed record Reply(int Status, string ContentType, byte[] Body, string? Nonce = null);
    private static Reply JsonReply(int status, bool success, string message) =>
        new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(
            new RemoteCommandResult(success, message), NoktoJsonContext.Default.RemoteCommandResult));

    private static Dictionary<string, string> SecurityHeaders(Reply reply) => new()
    {
        ["Cache-Control"] = "no-store, no-cache, max-age=0",
        ["Pragma"] = "no-cache",
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Referrer-Policy"] = "no-referrer",
        ["Content-Security-Policy"] = reply.Nonce is { } nonce
            ? $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; img-src 'self' blob:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'"
            : "default-src 'none'; frame-ancestors 'none'"
    };

    private bool IsTrustedRequest(RequestInput input)
    {
        if (!IsLanAddress(input.Address) || !Uri.TryCreate("http://" + input.Host, UriKind.Absolute, out var host) ||
            host.Port != Port || host.UserInfo.Length != 0 || host.AbsolutePath != "/" || !_localHosts.Contains(host.Host))
            return false;
        return input.Origin.Length == 0 || Uri.TryCreate(input.Origin, UriKind.Absolute, out var origin) &&
            origin.Scheme == "http" && string.Equals(origin.Authority, host.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Reply> HandleAsync(RequestInput input, Stream body, CancellationToken ct)
    {
        if (!IsTrustedRequest(input)) return JsonReply(403, false, "Acceso exclusivo a la red local.");
        if (input.Path == "/" && input.Method == "GET")
        {
            string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            using var resource = typeof(LocalRemoteServerService).Assembly.GetManifestResourceStream("Nokto.Core.Remote.RemoteDashboard.html")!;
            using var reader = new StreamReader(resource);
            string html = (await reader.ReadToEndAsync(ct)).Replace("__NONCE__", nonce, StringComparison.Ordinal);
            return new(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), nonce);
        }
        if (input.Key.Length != SessionKey.Length || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(input.Key), Encoding.UTF8.GetBytes(SessionKey)))
            return JsonReply(401, false, "Escanea el QR de Ajustes para conectar.");
        if (!await _requestGate.WaitAsync(TimeSpan.FromMilliseconds(250), ct)) return JsonReply(429, false, "Espera un momento e inténtalo de nuevo.");
        try
        {
            if (input.Path == "/api/status" && input.Method == "GET")
            {
                var status = new RemoteStatusSnapshot
                {
                    Metrics = adapter.GetCurrentMetrics(), UptimeSeconds = Environment.TickCount64 / 1000,
                    Battery = adapter.GetBatteryStatus(), Audio = adapter.GetAudioDevices(), Tasks = engine.GetActiveWorkflows()
                };
                return new(200, "application/json; charset=utf-8",
                    JsonSerializer.SerializeToUtf8Bytes(status, NoktoJsonContext.Default.RemoteStatusSnapshot));
            }
            if (input.Path == "/api/snapshot" && input.Method == "GET")
            {
                if (!AllowScreenPreview) return JsonReply(403, false, "La vista de pantalla está desactivada.");
                await _snapshotGate.WaitAsync(ct);
                try
                {
                    byte[] image = await capturePreview(ct);
                    return image.Length == 0 ? JsonReply(503, false, "La pantalla no está disponible.") : new(200, "image/jpeg", image);
                }
                finally { _snapshotGate.Release(); }
            }
            if (input.Method != "POST") return JsonReply(405, false, "Método no permitido.");
            if (input.Length > MaxBodyBytes) return JsonReply(413, false, "Petición demasiado grande.");
            switch (input.Path)
            {
                case "/api/audio/volume":
                    using (var json = await ReadBodyAsync(body, ct))
                    {
                        if (!json.RootElement.TryGetProperty("volume", out var volume) || volume.ValueKind != JsonValueKind.Number ||
                            !volume.TryGetDouble(out double percent) || !double.IsFinite(percent) || percent is < 0 or > 100)
                            return JsonReply(400, false, "El volumen debe estar entre 0 y 100.");
                        await adapter.SetMasterVolumeAsync((float)(percent / 100), ct);
                    }
                    break;
                case "/api/audio/toggle-output-mute":
                    if (!adapter.ToggleOutputMute()) return JsonReply(503, false, "Salida de audio no disponible.");
                    break;
                case "/api/audio/toggle-mic-mute":
                    if (!adapter.ToggleInputMute()) return JsonReply(503, false, "Micrófono no disponible.");
                    break;
                case "/api/tasks/stop":
                    using (var json = await ReadBodyAsync(body, ct))
                    {
                        if (!json.RootElement.TryGetProperty("taskId", out var taskId) || taskId.ValueKind != JsonValueKind.String ||
                            taskId.GetString() is not { Length: > 0 and <= 128 } id)
                            return JsonReply(400, false, "Indica la tarea que deseas finalizar.");
                        if (!engine.IsRoutineRunning(id)) return JsonReply(404, false, "La tarea ya ha finalizado.");
                        engine.StopRoutine(id);
                    }
                    break;
                case "/api/power/monitors-off":
                    await adapter.SetDisplayPowerAsync(false, ct);
                    break;
                case "/api/power/lock":
                    await adapter.SetPowerStateAsync(PowerAction.LockStation, cancellationToken: ct);
                    break;
                case "/api/power/sleep":
                    await adapter.SetPowerStateAsync(PowerAction.Sleep, cancellationToken: ct);
                    break;
                case "/api/power/shutdown":
                    await adapter.SetPowerStateAsync(PowerAction.Shutdown, cancellationToken: ct);
                    break;
                default: return JsonReply(404, false, "Ruta no disponible.");
            }
            return JsonReply(200, true, "Acción enviada.");
        }
        catch (JsonException) { return JsonReply(400, false, "Petición JSON inválida."); }
        catch (InvalidDataException) { return JsonReply(413, false, "Petición demasiado grande."); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413) { return JsonReply(413, false, "Petición demasiado grande."); }
        catch (OperationCanceledException) { return JsonReply(408, false, "Petición cancelada."); }
        catch (Exception) { return JsonReply(503, false, "No se pudo ejecutar la acción local."); }
        finally { _requestGate.Release(); }
    }

    private static async Task<JsonDocument> ReadBodyAsync(Stream body, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        byte[] buffer = new byte[512];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + read > MaxBodyBytes) throw new InvalidDataException();
            memory.Write(buffer, 0, read);
        }
        var document = JsonDocument.Parse(memory.ToArray());
        if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
        document.Dispose();
        throw new JsonException();
    }

    // Hosting also has internal awaits: begin its shutdown away from any UI context.
    public Task StopAsync() => Task.Run(StopCoreAsync);

    private async Task StopCoreAsync()
    {
        // Desktop Exit may synchronously await disposal after the UI pump has stopped.
        // Transport shutdown and request draining must never need that dispatcher.
        var lifetime = _lifetime;
        if (lifetime is null) return;
        lifetime.Cancel();
        _listener?.Close();
        if (_acceptLoop is not null) await _acceptLoop.ConfigureAwait(false);
        if (_fallback is not null)
        {
            await _fallback.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _fallback.DisposeAsync().ConfigureAwait(false);
        }
        await Task.WhenAll(_requests.Values).ConfigureAwait(false);
        _requests.Clear();
        _listener = null; _fallback = null; _acceptLoop = null;
        _lifetime = null; SessionKey = ""; Transport = "";
        lifetime.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _snapshotGate.Dispose(); _requestGate.Dispose();
    }
}

