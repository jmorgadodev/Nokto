using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Nokto.ConsoleTest;
using Nokto.Core.Engine;
using Nokto.Core.Persistence;
using Nokto.Core.Remote;

internal static class RemoteShutdownTests
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nokto_lan_shutdown_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var adapter = new WorkflowTestAdapter();
        using var engine = new WorkflowEngine(adapter, new PersistenceService(new StorageResolver(directory)));
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new LocalRemoteServerService(adapter, engine, async ct =>
        {
            captureStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
            finally { await Task.Delay(100).ConfigureAwait(false); }
            return [];
        });
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Task.Run(() => server.StartAsync(port)).GetAwaiter().GetResult();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-Nokto-Key", server.SessionKey);
        var request = client.GetAsync("/api/snapshot");
        if (!captureStarted.Task.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("La captura de prueba no empezó.");

        // Model desktop Exit: the UI thread cannot pump continuations while Dispose is waiting.
        var context = new PausedContext();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closingThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); completed.TrySetResult(); }
            catch (Exception ex) { completed.TrySetException(ex); }
        }) { IsBackground = true, Name = "Nokto shutdown regression" };
        closingThread.Start();
        bool closedWithoutUiPump = false;
        try { closedWithoutUiPump = completed.Task.Wait(TimeSpan.FromSeconds(3)); }
        finally
        {
            // Release on failure as well, so the regression never leaves a blocked test process.
            context.Release();
            completed.Task.Wait(TimeSpan.FromSeconds(3));
            try { using var response = request.GetAwaiter().GetResult(); }
            catch (HttpRequestException) { } // Listener may close before writing a cancelled response.
            catch (TaskCanceledException) { }
        }
        if (!closedWithoutUiPump || context.PostCount != 0)
            throw new InvalidOperationException($"Cerrar LAN con una captura pendiente depende del hilo UI: el proceso queda vivo sin ventana. Finalizo: {closedWithoutUiPump}; continuaciones: {context.PostCount}.");
        var reuse = new TcpListener(IPAddress.Any, port);
        reuse.Start(); reuse.Stop();
        Console.WriteLine("[PASS] Cierre LAN: captura cancelada, sin continuación UI bloqueada y puerto liberado.");
    }

    private sealed class PausedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private bool _released;
        public int PostCount { get; private set; }
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_queue)
            {
                PostCount++;
                if (!_released) { _queue.Enqueue((callback, state)); return; }
            }
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
        public void Release()
        {
            lock (_queue)
            {
                _released = true;
                while (_queue.TryDequeue(out var work))
                    ThreadPool.QueueUserWorkItem(_ => work.Callback(work.State));
            }
        }
    }
}
