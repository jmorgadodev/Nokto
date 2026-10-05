using System.Text.Json;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;

namespace Nokto.ConsoleTest;

internal static class ConcurrentWorkflowTests
{
    public static async Task<int> RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nokto_concurrency_" + Guid.NewGuid().ToString("N"));
        var persistence = new PersistenceService(new StorageResolver(directory));
        using var adapter = new WorkflowTestAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var first = engine.StartPresetAsync(Countdown("first", 30), cancellation.Token);
            var second = engine.StartPresetAsync(Countdown("second", 30), cancellation.Token);
            await Task.Delay(150);
            Require(!first.IsCompleted && !second.IsCompleted,
                "Iniciar una segunda rutina debe mantener ambas ejecuciones vivas e independientes.");
            Require(engine.GetActiveWorkflows().Count == 2, "La lista debe incluir ambas rutinas.");
            var statusJson = JsonSerializer.SerializeToUtf8Bytes(engine.GetStatusSnapshot(), Nokto.Core.Serialization.NoktoJsonContext.Default.SystemStatusState);
            Require(JsonSerializer.Deserialize(statusJson, Nokto.Core.Serialization.NoktoJsonContext.Default.SystemStatusState)?.ActiveWorkflows.Count == 2,
                "El contexto JSON generado debe soportar el estado multi-rutina.");
            var duplicate = engine.StartRoutine(Countdown("first", 99));
            Require(ReferenceEquals(first, duplicate), "Una ID activa debe devolver la misma tarea, sin duplicar ni reiniciar.");
            engine.PostponeRoutine("first", TimeSpan.FromSeconds(10));
            Require(engine.GetActiveWorkflows().Single(w => w.RoutineId == "first").RemainingSeconds > 30 &&
                engine.GetActiveWorkflows().Single(w => w.RoutineId == "second").RemainingSeconds <= 30,
                "Posponer una rutina no debe alterar la otra.");
            engine.StopRoutine("first");
            var replacement = engine.StartRoutine(Countdown("first", 40));
            await first.WaitAsync(TimeSpan.FromSeconds(3));
            Require(engine.IsRoutineRunning("first") && engine.IsRoutineRunning("second") && !replacement.IsCompleted,
                "El cierre de la ejecución anterior no debe eliminar una nueva con la misma ID.");
            var failure = engine.StartRoutine(Countdown("failed", 0) with
            {
                Pipeline = [new() { ActionType = ActionType.ExecuteCommand, Parameters = new()
                { ["executablePath"] = JsonSerializer.SerializeToElement("nokto_missing_" + Guid.NewGuid().ToString("N") + ".exe") } }]
            });
            try { await failure; throw new InvalidOperationException("El error de comando debe notificarse al llamador."); }
            catch (System.ComponentModel.Win32Exception) { }
            Require(engine.GetActiveWorkflows().Count == 2, "El fallo de una rutina no debe terminar las demás.");
            engine.StopRoutine("second");
            await second.WaitAsync(TimeSpan.FromSeconds(3));
            Require(engine.IsRoutineRunning("first"), "Finalizar la segunda no debe cancelar la primera.");
            using var external = new CancellationTokenSource();
            var externalTask = engine.StartRoutine(Countdown("external", 30), external.Token);
            external.Cancel();
            await externalTask.WaitAsync(TimeSpan.FromSeconds(3));
            Require(!engine.IsRoutineRunning("external") && engine.IsRoutineRunning("first"), "Cancelación externa aislada.");
            var watched = engine.StartRoutine(Countdown("idle", 0) with
            {
                Trigger = new() { Type = TriggerType.UserIdle, Parameters = new() { ["idleMinutes"] = JsonSerializer.SerializeToElement(0) } },
                Pipeline = [new() { ActionType = ActionType.KeepAliveEngine }],
                TerminalAction = new() { Type = TerminalActionType.LockStation, Parameters = new() { ["delayHours"] = JsonSerializer.SerializeToElement(8) } }
            });
            await Task.Delay(2200);
            var watching = engine.GetActiveWorkflows().Single(w => w.RoutineId == "idle");
            Require(watching.RemainingSeconds is null && watching.TimeText.StartsWith("Activa:") && watching.KeepAliveActive,
                "La jornada usa tiempo transcurrido y mantiene su loop durante las ocho horas configuradas.");
            engine.StopRoutine("idle");
            await watched.WaitAsync(TimeSpan.FromSeconds(3));
            Require(adapter.PowerActions == 0, "Finalizar durante vigilancia debe impedir la acción terminal.");
            engine.FinishAll();
            await replacement.WaitAsync(TimeSpan.FromSeconds(3));
            Require(engine.GetActiveWorkflows().Count == 0 && engine.CurrentState == EngineState.Idle, "Finalizar todas debe vaciar el registro.");
            Console.WriteLine("[PASS] Concurrencia: cancelación y posposición aisladas, IDs duplicadas, reinicio, error independiente, tokens externos, jornada continua y cierre global.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Concurrencia: {ex.Message}");
            return 1;
        }
    }

    private static PresetDefinition Countdown(string id, int seconds) => new()
    {
        Id = id, Name = id,
        Trigger = new TriggerDefinition
        {
            Type = TriggerType.Countdown,
            Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(seconds) }
        },
        TerminalAction = new TerminalActionDefinition { Type = TerminalActionType.None }
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
