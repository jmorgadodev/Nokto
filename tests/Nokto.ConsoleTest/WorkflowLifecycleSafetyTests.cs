using System.Diagnostics;
using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;

namespace Nokto.ConsoleTest;

internal static class WorkflowLifecycleSafetyTests
{
    public static async Task<int> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "nokto_lifecycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); // Never allow storage to fall back to the user's data.
        var storage = new StorageResolver(root);
        Require(Path.GetFullPath(storage.DataDirectory) == Path.Combine(Path.GetFullPath(root), "data"), "El almacenamiento de prueba debe quedar aislado.");
        var persistence = new PersistenceService(storage);
        int failures = 0;
        foreach (var test in new (string Name, Func<Task> Run)[]
        {
            ("pause during startup", () => PauseStartupAsync(persistence)),
            ("countdown pause", () => PauseCountdownAsync(persistence)),
            ("pipeline pause", () => PauseDelayAsync(persistence)),
            ("grace pause", () => PauseGraceAsync(persistence)),
            ("keep-alive pause and cancellation", () => PauseKeepAliveAsync(persistence)),
            ("power dry-run mapping", () => PowerMappingAsync(persistence)),
            ("large command output", () => LargeOutputAsync(persistence, root)),
            ("command timeout cleanup", () => CommandCancellationAsync(persistence, root, false)),
            ("command user cancellation cleanup", () => CommandCancellationAsync(persistence, root, true))
        })
        {
            try { await test.Run(); Console.WriteLine("[PASS] Lifecycle: " + test.Name); }
            catch (Exception ex) { failures++; Console.WriteLine("[FAIL] Lifecycle: " + test.Name + ": " + ex.Message); }
        }
        return failures == 0 ? 0 : 1;
    }

    private static PresetDefinition Routine(params WorkflowActionItem[] actions) => new()
    {
        Id = "lifecycle_" + Guid.NewGuid().ToString("N"), Name = "Synthetic lifecycle test",
        Trigger = new() { Type = TriggerType.Manual }, Actions = new(actions)
    };

    private static WorkflowActionItem Step(ActionType type, params (string Key, object Value)[] values) => new()
    { ActionType = type, Parameters = values.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)) };

    private static async Task UntilAsync(Func<bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("No se alcanzó el estado esperado.");
            await Task.Delay(20);
        }
    }

    private static async Task PauseCountdownAsync(PersistenceService persistence)
    {
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.LogMessageReceived += message => { if (message.Contains("Evaluando disparador")) started.TrySetResult(); };
        var routine = Routine(Step(ActionType.LaunchApp, ("executablePath", "synthetic"))) with
        { Trigger = new() { Type = TriggerType.Countdown, Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(2) } } };
        var run = engine.StartRoutine(routine);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await UntilAsync(() => engine.CurrentState == EngineState.WaitingTrigger);
        engine.Pause();
        int remaining = engine.GetStatusSnapshot().TimeRemainingSeconds;
        await Task.Delay(2300);
        Require(!run.IsCompleted && adapter.Launches == 0 && engine.CurrentState == EngineState.Paused &&
            engine.GetStatusSnapshot().TimeRemainingSeconds == remaining, "La pausa debe congelar el countdown y bloquear acciones.");
        engine.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Require(adapter.Launches == 1, "Reanudar debe completar una sola vez.");
    }

    private static async Task PauseStartupAsync(PersistenceService persistence)
    {
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        using var continueStartup = new ManualResetEventSlim();
        var starting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.LogMessageReceived += message =>
        {
            if (!message.Contains("Iniciando flujo")) return;
            starting.TrySetResult();
            if (!continueStartup.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Startup fixture was not released.");
        };
        var run = engine.StartRoutine(Routine(Step(ActionType.LaunchApp, ("executablePath", "synthetic"))));
        try
        {
            await starting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            engine.Pause();
            continueStartup.Set();
            await Task.Delay(150);
            Require(engine.CurrentState == EngineState.Paused && !run.IsCompleted && adapter.Launches == 0,
                "Una pausa durante el arranque debe bloquear la primera acción.");
            engine.Resume();
            await run.WaitAsync(TimeSpan.FromSeconds(2));
            Require(adapter.Launches == 1, "Reanudar el arranque debe ejecutar una sola vez.");
        }
        finally { continueStartup.Set(); }
    }

    private static async Task PauseDelayAsync(PersistenceService persistence)
    {
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        var run = engine.StartRoutine(Routine(Step(ActionType.WaitDelay, ("delaySeconds", 2)), Step(ActionType.LaunchApp, ("executablePath", "synthetic"))));
        await UntilAsync(() => engine.CurrentState == EngineState.ExecutingActions);
        engine.Pause();
        await Task.Delay(2300);
        Require(!run.IsCompleted && adapter.Launches == 0, "Una espera pausada no debe avanzar al siguiente paso.");
        engine.Resume();
        Require(engine.CurrentState == EngineState.ExecutingActions, "Reanudar debe restaurar la fase de acciones.");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Require(adapter.Launches == 1, "La espera reanudada debe completar una vez.");
    }

    private static async Task PauseGraceAsync(PersistenceService persistence)
    {
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        var run = engine.StartRoutine(Routine(Step(ActionType.PowerAction, ("powerAction", "Sleep"), ("gracePeriodSeconds", 2))));
        await UntilAsync(() => engine.CurrentState == EngineState.GracePeriod);
        engine.Pause();
        int remaining = engine.GetStatusSnapshot().GracePeriodRemainingSeconds;
        await Task.Delay(2300);
        Require(!run.IsCompleted && adapter.PowerActions.Count == 0 && engine.GetStatusSnapshot().GracePeriodRemainingSeconds == remaining,
            "La pausa de gracia debe impedir la energía y congelar segundos.");
        engine.Resume();
        Require(engine.CurrentState == EngineState.GracePeriod, "Reanudar debe restaurar la gracia.");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Require(adapter.PowerActions.Single() == PowerAction.Sleep, "La gracia reanudada debe respetar la acción solicitada.");
    }

    private static async Task PowerMappingAsync(PersistenceService persistence)
    {
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        foreach (var action in new[] { TerminalActionType.Shutdown, TerminalActionType.Sleep, TerminalActionType.Hibernate, TerminalActionType.Restart })
        {
            await engine.StartRoutine(new PresetDefinition
            {
                Id = "mapping_" + action, Name = "Synthetic power mapping", Trigger = new() { Type = TriggerType.Manual },
                TerminalAction = new() { Type = action, Parameters = new() { ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(0) } }
            });
            Require(adapter.PowerActions.Last().ToString() == action.ToString(), "Dry-run debe conservar el mapeo " + action);
        }
    }

    private static async Task PauseKeepAliveAsync(PersistenceService persistence)
    {
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        var routine = Routine(Step(ActionType.KeepAliveEngine, ("delayHours", 0.01)), Step(ActionType.LaunchApp, ("executablePath", "never")));
        var run = engine.StartRoutine(routine);
        await UntilAsync(() => Volatile.Read(ref adapter.Pulses) > 0);
        engine.Pause();
        await Task.Delay(150); // Allow an in-flight fake pulse to finish cancellation.
        int pulses = Volatile.Read(ref adapter.Pulses);
        await Task.Delay(250);
        Require(adapter.Pulses == pulses && !run.IsCompleted, "Keep-alive pausado no debe seguir emitiendo actividad.");
        engine.Resume();
        await UntilAsync(() => Volatile.Read(ref adapter.Pulses) > pulses);
        engine.Pause();
        engine.StopRoutine(routine.Id);
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Require(adapter.Launches == 0, "Cancelar una pausa debe desbloquear el runner sin avanzar acciones.");
    }

    private static WorkflowActionItem Command(string script, int timeout) => Step(ActionType.ExecuteCommand,
        ("executablePath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")),
        ("arguments", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\""), ("timeoutSeconds", timeout));

    private static async Task LargeOutputAsync(PersistenceService persistence, string root)
    {
        string script = Path.Combine(root, "output.ps1");
        string pidFile = Path.Combine(root, "output.pid");
        string releaseFile = Path.Combine(root, "output.release");
        await File.WriteAllTextAsync(script, "$PID | Set-Content -LiteralPath '" + pidFile.Replace("'", "''") + "'; " +
            "while (!(Test-Path -LiteralPath '" + releaseFile.Replace("'", "''") + "')) { Start-Sleep -Milliseconds 20 }; " +
            "$line = 'x' * 2048; for ($i=0; $i -lt 1024; $i++) { [Console]::Out.WriteLine($line); [Console]::Error.WriteLine($line) }");
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        Process? owned = null;
        var run = engine.StartRoutine(Routine(Command(script, 15)));
        try
        {
            await UntilAsync(() => File.Exists(pidFile) && new FileInfo(pidFile).Length > 0);
            owned = Process.GetProcessById(int.Parse((await File.ReadAllTextAsync(pidFile)).Trim()));
            await File.WriteAllTextAsync(releaseFile, "go");
            await run.WaitAsync(TimeSpan.FromSeconds(20));
            Require(engine.CurrentState == EngineState.Completed, "La salida grande debe drenarse sin bloqueo.");
        }
        finally
        {
            if (owned is not null)
            {
                try
                {
                    if (!owned.HasExited) { owned.Kill(entireProcessTree: true); await owned.WaitForExitAsync(); }
                }
                finally { owned.Dispose(); }
            }
        }
    }

    private static async Task CommandCancellationAsync(PersistenceService persistence, string root, bool userCancel)
    {
        string prefix = Path.Combine(root, userCancel ? "cancel" : "timeout");
        string script = prefix + ".ps1", pidFile = prefix + ".pid", childPidFile = prefix + ".child.pid";
        string childScript = prefix + ".child.ps1";
        await File.WriteAllTextAsync(childScript, "$PID | Set-Content -LiteralPath '" + childPidFile.Replace("'", "''") + "'; Start-Sleep -Seconds 120");
        await File.WriteAllTextAsync(script, "$PID | Set-Content -LiteralPath '" + pidFile.Replace("'", "''") + "'; " +
            "Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -WindowStyle Hidden -ArgumentList '-NoProfile -NonInteractive -File \"" + childScript + "\"'; Start-Sleep -Seconds 120");
        using var adapter = new SafeAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        using var cancel = new CancellationTokenSource();
        var routine = Routine(Command(script, userCancel ? 30 : 4), Step(ActionType.LaunchApp, ("executablePath", "must-not-launch")));
        var run = engine.StartRoutine(routine, cancel.Token);
        Process? owned = null, child = null;
        try
        {
            await UntilAsync(() => File.Exists(pidFile) && new FileInfo(pidFile).Length > 0 && File.Exists(childPidFile) && new FileInfo(childPidFile).Length > 0);
            // The only PIDs opened/terminated here are written by these freshly created fixtures.
            owned = Process.GetProcessById(int.Parse((await File.ReadAllTextAsync(pidFile)).Trim()));
            child = Process.GetProcessById(int.Parse((await File.ReadAllTextAsync(childPidFile)).Trim()));
            if (userCancel) cancel.Cancel();
            Exception? error = null;
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception ex) { error = ex; }
            await UntilAsync(() => owned.HasExited && child.HasExited);
            Require(owned.HasExited && child.HasExited, "Cancelar/timeout debe terminar el comando propio y su árbol.");
            Require(adapter.Launches == 0, "El comando interrumpido debe impedir acciones posteriores.");
            if (userCancel) Require(error is null, "La cancelación del usuario conserva la finalización normal del contrato.");
            else Require(error is TimeoutException && engine.CurrentState == EngineState.Failed, "Timeout debe informar Failed y TimeoutException.");
            Require(persistence.ReadRecentAuditLogs().Single(entry => entry.PresetId == routine.Id).Status == (userCancel ? "Finalized" : "Failed"),
                "La auditoría debe distinguir cancelación del usuario de timeout.");
        }
        finally
        {
            cancel.Cancel();
            foreach (var process in new[] { owned, child })
            {
                if (process is null) continue;
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
                finally { process.Dispose(); }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { /* Result was asserted above; cleanup only. */ }
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class SafeAdapter : ISystemAdapter
    {
        public bool IsDryRunMode { get; set; } = true;
        public int Launches;
        public int Pulses;
        public List<PowerAction> PowerActions { get; } = [];
        public Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); PowerActions.Add(action); return Task.CompletedTask; }
        public Task LaunchAppAsync(LaunchAppOptions options, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref Launches); return Task.CompletedTask; }
        public SystemMetrics GetCurrentMetrics() => new();
        public Task SetDisplayPowerAsync(bool turnOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetMasterVolumeAsync(float volume, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> GetMuteAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void SimulateKeepAlivePulse(KeepAliveMode mode) => throw new NotSupportedException();
        public async Task RunKeepAliveLoopAsync(KeepAliveMode mode, int jitterMinSeconds, int jitterMaxSeconds, Action<string>? onActivityLogged, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref Pulses);
                await Task.Delay(25, cancellationToken);
            }
        }
        public float GetMasterPeakValue() => throw new NotSupportedException();
        public BatteryStatus GetBatteryStatus() => throw new NotSupportedException();
        public Task<byte[]> CaptureScreenAsync(bool stampMetadata = false, string? label = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void SendMediaControl(bool pauseOnly = true) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
