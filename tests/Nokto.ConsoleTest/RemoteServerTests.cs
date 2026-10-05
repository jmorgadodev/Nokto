using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Remote;
using Nokto.Core.Serialization;

namespace Nokto.ConsoleTest;

internal static class RemoteServerTests
{
    public static async Task<int> RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nokto_remote_" + Guid.NewGuid().ToString("N"));
        using var adapter = new WorkflowTestAdapter();
        var persistence = new PersistenceService(new StorageResolver(directory));
        using var engine = new WorkflowEngine(adapter, persistence);
        await using var server = new LocalRemoteServerService(adapter, engine, _ => Task.FromResult(new byte[] { 255, 216, 255, 217 }));
        try
        {
            Require(LocalRemoteServerService.IsLanAddress(IPAddress.Parse("192.168.1.25")) &&
                LocalRemoteServerService.IsLanAddress(IPAddress.Parse("10.0.0.25")) &&
                LocalRemoteServerService.IsLanAddress(IPAddress.Parse("172.16.1.25")) &&
                !LocalRemoteServerService.IsLanAddress(IPAddress.Parse("8.8.8.8")) &&
                !LocalRemoteServerService.IsLanAddress(IPAddress.Parse("100.64.0.1")) &&
                !LocalRemoteServerService.IsLanAddress(null), "La frontera de IP debe aceptar sólo loopback y redes privadas.");
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            await server.StartAsync(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using (var root = await client.GetAsync("/"))
            {
                string html = await root.Content.ReadAsStringAsync();
                Require(root.IsSuccessStatusCode && html.Contains("PANTALLA EN VIVO") && html.Contains("X-Nokto-Key") &&
                    !html.Contains("https://") && root.Headers.Contains("Content-Security-Policy"), "SPA offline y cabeceras de seguridad.");
            }
            using (var noKey = await client.GetAsync("/api/status"))
                Require(noKey.StatusCode == HttpStatusCode.Unauthorized, "Las métricas y capturas requieren emparejamiento.");
            client.DefaultRequestHeaders.Add("X-Nokto-Key", server.SessionKey);
            var lanAddress = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(entry => entry.Address).FirstOrDefault(address => !IPAddress.IsLoopback(address) && LocalRemoteServerService.IsLanAddress(address));
            if (lanAddress is not null)
            {
                using var lanClient = new HttpClient();
                lanClient.DefaultRequestHeaders.Add("X-Nokto-Key", server.SessionKey);
                using var lanStatus = await lanClient.GetAsync($"http://{lanAddress}:{port}/api/status");
                Require(lanStatus.IsSuccessStatusCode, "El listener atiende también en la interfaz LAN real.");
            }
            using (var foreignOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/power/shutdown"))
            {
                foreignOrigin.Headers.Add("Origin", "http://external.invalid");
                using var result = await client.SendAsync(foreignOrigin);
                Require(result.StatusCode == HttpStatusCode.Forbidden && adapter.PowerActions == 0, "Un origen ajeno no puede apagar el equipo.");
            }
            using (var foreignHost = new HttpRequestMessage(HttpMethod.Get, "/api/status"))
            {
                foreignHost.Headers.Host = "rebound.invalid:" + port;
                using var result = await client.SendAsync(foreignHost);
                Require(result.StatusCode == HttpStatusCode.Forbidden, "Una cabecera Host externa no puede reutilizar el servidor.");
            }
            var first = engine.StartRoutine(Routine("remote-one"));
            var second = engine.StartRoutine(Routine("remote-two"));
            using (var status = await client.GetAsync("/api/status"))
            {
                var decoded = JsonSerializer.Deserialize(await status.Content.ReadAsByteArrayAsync(), NoktoJsonContext.Default.RemoteStatusSnapshot)!;
                Require(decoded.Tasks.Count == 2 && decoded.Audio.OutputId == "real-speakers" && decoded.Audio.OutputVolumePercent == 50 &&
                    decoded.UptimeSeconds > 0, "El estado móvil contiene la telemetría real y ambas tareas.");
            }
            using (var volume = await Post(client, "/api/audio/volume", "{\"volume\":25}"))
                Require(volume.IsSuccessStatusCode && adapter.Volume == 0.25f, "El slider remoto actúa sobre volumen maestro.");
            foreach (string invalid in new[] { "{\"volume\":-1}", "{\"volume\":101}", "{\"volume\":\"25\"}", "[25]", "{", "{}" })
            {
                using var result = await Post(client, "/api/audio/volume", invalid);
                Require(result.StatusCode == HttpStatusCode.BadRequest && adapter.Volume == 0.25f, "El volumen inválido no modifica el sistema.");
            }
            using (var tooLarge = await Post(client, "/api/audio/volume", "{\"volume\":50,\"padding\":\"" + new string('x', 4500) + "\"}"))
                Require(tooLarge.StatusCode == HttpStatusCode.RequestEntityTooLarge && adapter.Volume == 0.25f, "Los cuerpos se limitan antes de ejecutar.");
            using (var mute = await Post(client, "/api/audio/toggle-output-mute", "{}"))
                Require(mute.IsSuccessStatusCode && adapter.OutputMuted, "Mute de salida remoto.");
            using (var mute = await Post(client, "/api/audio/toggle-mic-mute", "{}"))
                Require(mute.IsSuccessStatusCode && adapter.InputMuted, "Mute de micrófono remoto.");
            using (var stop = await Post(client, "/api/tasks/stop", "{\"taskId\":\"remote-one\"}"))
                Require(stop.IsSuccessStatusCode && !engine.IsRoutineRunning("remote-one") && engine.IsRoutineRunning("remote-two"), "Finalización remota aislada.");
            await first;
            using (var snapshot = await client.GetAsync("/api/snapshot"))
                Require(snapshot.IsSuccessStatusCode && snapshot.Content.Headers.ContentType?.MediaType == "image/jpeg" &&
                    snapshot.Headers.CacheControl?.NoStore == true && (await snapshot.Content.ReadAsByteArrayAsync()).Length == 4, "Snapshot JPEG sin caché.");
            foreach (string action in new[] { "lock", "sleep", "shutdown", "monitors-off" })
            {
                using var power = await Post(client, "/api/power/" + action, "{}");
                Require(power.IsSuccessStatusCode, "Acción remota disponible: " + action);
            }
            Require(adapter.PowerActions == 3 && adapter.DisplayActions == 1, "Las acciones usan exclusivamente el adaptador local.");
            string previousKey = server.SessionKey;
            await server.StopAsync();
            await server.StartAsync(port);
            using (var stale = await client.GetAsync("/api/status"))
                Require(stale.StatusCode == HttpStatusCode.Unauthorized && server.SessionKey != previousKey, "Reiniciar invalida la sesión QR anterior.");
            client.DefaultRequestHeaders.Remove("X-Nokto-Key");
            client.DefaultRequestHeaders.Add("X-Nokto-Key", server.SessionKey);
            using (var resumed = await client.GetAsync("/api/status"))
                Require(resumed.IsSuccessStatusCode, "El servidor puede detenerse y reiniciarse sin perder el puerto.");
            engine.FinishAll(); await second;
            await VerifyBurstQueueingAsync(adapter, engine);
            Console.WriteLine($"[PASS] LAN ({server.Transport}): SPA, acceso privado, clave QR, origen/Host, validación, audio, tareas aisladas, JPEG, energía y reinicio.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] LAN: " + ex);
            return 1;
        }
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, string json) =>
        client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
    private static async Task VerifyBurstQueueingAsync(WorkflowTestAdapter adapter, WorkflowEngine engine)
    {
        var releaseCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LocalRemoteServerService(adapter, engine, async ct =>
        {
            await releaseCapture.Task.WaitAsync(ct);
            return [255, 216, 255, 217];
        });
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await server.StartAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-Nokto-Key", server.SessionKey);
        // Observe capacity only to synchronize setup; assertions exercise real HTTP replies.
        var gate = (SemaphoreSlim)typeof(LocalRemoteServerService).GetField("_requestGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(server)!;
        var occupied = Enumerable.Range(0, 8).Select(_ => client.GetAsync("/api/snapshot")).ToArray();
        try
        {
            var setup = System.Diagnostics.Stopwatch.StartNew();
            while (gate.CurrentCount != 0 && setup.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(1);
            Require(gate.CurrentCount == 0, "La prueba debe ocupar los ocho slots antes de la ráfaga.");
            var queued = client.GetAsync("/api/status");
            await Task.Delay(50);
            releaseCapture.TrySetResult();
            using var accepted = await queued;
            Require(accepted.IsSuccessStatusCode, "Un clic en una ráfaga breve debe esperar capacidad, no recibir 429 inmediato.");
            foreach (var pending in occupied) { using var reply = await pending; Require(reply.IsSuccessStatusCode, "Captura ocupante válida."); }

            releaseCapture = new(TaskCreationOptions.RunContinuationsAsynchronously);
            occupied = Enumerable.Range(0, 8).Select(_ => client.GetAsync("/api/snapshot")).ToArray();
            setup.Restart();
            while (gate.CurrentCount != 0 && setup.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(1);
            Require(gate.CurrentCount == 0, "Saturación persistente reproducible.");
            var throttleWatch = System.Diagnostics.Stopwatch.StartNew();
            using var throttled = await client.GetAsync("/api/status");
            Require(throttled.StatusCode == HttpStatusCode.TooManyRequests && throttleWatch.ElapsedMilliseconds >= 200,
                "La saturación persistente mantiene 429 después de una espera acotada.");
            releaseCapture.TrySetResult();
            foreach (var pending in occupied) { using var reply = await pending; }
            using var recovered = await client.GetAsync("/api/status");
            Require(recovered.IsSuccessStatusCode && gate.CurrentCount == 8, "La cola libera todos los slots tras recuperar capacidad.");
            Console.WriteLine("[PASS] LAN: ráfaga breve en cola, saturación acotada y recuperación de los ocho slots.");
        }
        finally
        {
            releaseCapture.TrySetResult();
            foreach (var pending in occupied) { using var reply = await pending; }
        }
    }
    private static PresetDefinition Routine(string id) => new()
    {
        Id = id, Name = id, Trigger = new() { Type = TriggerType.Countdown,
            Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(600) } }
    };
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static async Task<int> PreviewAsync(string descriptor)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nokto_mobile_preview_" + Guid.NewGuid().ToString("N"));
        using var adapter = new WorkflowTestAdapter();
        using var engine = new WorkflowEngine(adapter, new PersistenceService(new StorageResolver(directory)));
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1280, 720));
        surface.Canvas.Clear(new SkiaSharp.SKColor(22, 24, 29));
        using var paint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0, 210, 255), TextSize = 42, IsAntialias = true };
        surface.Canvas.DrawText("Nokto · Vista de pantalla", 80, 180, paint);
        using var image = surface.Snapshot();
        using var jpeg = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 70);
        byte[] preview = jpeg.ToArray();
        await using var server = new LocalRemoteServerService(adapter, engine, _ => Task.FromResult(preview));
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await server.StartAsync(port);
        var a = engine.StartRoutine(Routine("manual_preview") with { Name = "Temporizador de audio", Description = "Silenciar salida + Apagar monitores" });
        var b = engine.StartRoutine(Routine("work_preview") with { Name = "Jornada de trabajo", Description = "Mantener equipo activo" });
        await File.WriteAllTextAsync(descriptor, JsonSerializer.Serialize(new { Url = server.GetPairingUrl("127.0.0.1"), Pid = Environment.ProcessId }));
        Console.WriteLine("Vista móvil de pruebas preparada.");
        await Task.Delay(TimeSpan.FromMinutes(10));
        engine.FinishAll(); await Task.WhenAll(a, b);
        return 0;
    }
}

