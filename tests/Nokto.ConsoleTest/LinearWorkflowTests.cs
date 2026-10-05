using System.Collections.ObjectModel;
using System.Text.Json;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Serialization;

namespace Nokto.ConsoleTest;

internal static class LinearWorkflowTests
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var persistence = new PersistenceService(new StorageResolver(Path.Combine(Path.GetTempPath(), "nokto_linear_" + Guid.NewGuid().ToString("N"))));
            VerifySchedules();
            using var adapter = new WorkflowTestAdapter();
            using var engine = new WorkflowEngine(adapter, persistence);
            var actions = new ObservableCollection<WorkflowActionItem>
            {
                Step(ActionType.LaunchApp, ("executablePath", "editor.exe")),
                Step(ActionType.AudioConfig, ("volumePercent", 45), ("muteOutput", false), ("muteMicrophone", true)),
                Step(ActionType.LaunchApp, ("executablePath", "chat.exe"))
            };
            // The collection order is authoritative, even with stale persisted ordinal values.
            actions[0] = actions[0] with { StepOrder = 99 };
            var routine = new PresetDefinition { Id = "linear", Name = "Cabina", Trigger = new() { Type = TriggerType.Countdown,
                Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(0) } }, Actions = actions };
            await engine.StartRoutine(routine).WaitAsync(TimeSpan.FromSeconds(2));
            Require(adapter.Calls.SequenceEqual(new[] { "Launch:editor.exe", "Volume", "OutputMute", "InputMute", "Launch:chat.exe" }) &&
                adapter.PowerActions == 0 && adapter.Volume == .45f && adapter.InputMuted,
                "El flujo ejecuta Actions en orden sin añadir energía implícita.");
            adapter.Calls.Clear(); adapter.RunningApps.Add("editor.exe");
            await engine.StartRoutine(routine);
            Require(!adapter.Calls.Contains("Launch:editor.exe") && adapter.Calls.Last() == "Launch:chat.exe", "Omitir programa abierto continúa el flujo.");
            adapter.Calls.Clear();
            await engine.StartRoutine(routine with { Actions = [Step(ActionType.LaunchApp, ("executablePath", "editor.exe"), ("skipIfAlreadyRunning", false))] });
            Require(adapter.Calls.Single() == "Launch:editor.exe", "Desactivar la omisión permite otro lanzamiento.");

            var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(routine, NoktoJsonContext.Default.PresetDefinition), NoktoJsonContext.Default.PresetDefinition)!;
            Require(restored.Actions?.Count == 3 && restored.TerminalAction.Type == TerminalActionType.None, "Actions persiste sin acción terminal obligatoria.");
            var legacy = new PresetDefinition { Pipeline = [new() { ActionType = ActionType.WaitDelay, StepOrder = 1 }],
                TerminalAction = new() { Type = TerminalActionType.Sleep, Parameters = new() { ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(15) } } };
            var migrated = WorkflowDefinition.GetActions(legacy);
            Require(migrated.Count == 2 && migrated[1].ActionType == ActionType.PowerAction && migrated[1].Parameters!["powerAction"].GetString() == "Sleep", "Migrar conserva la energía explícita al final.");

            using var cancelled = new CancellationTokenSource();
            var delayed = engine.StartRoutine(routine with { Actions = [Step(ActionType.WaitDelay, ("delaySeconds", 60)), Step(ActionType.LaunchApp, ("executablePath", "never.exe"))] }, cancelled.Token);
            cancelled.Cancel(); await delayed.WaitAsync(TimeSpan.FromSeconds(2));
            Require(!adapter.Calls.Contains("Launch:never.exe"), "Cancelar una espera evita las acciones siguientes.");
            await VerifyLiveSchedule(engine, adapter, routine);
            await engine.StartRoutine(routine with { Trigger = new() { Type = TriggerType.Manual }, Actions = [], TerminalAction = new() { Type = TerminalActionType.Shutdown } });
            Require(adapter.PowerActions == 0, "Actions explícito ignora cualquier terminal heredado residual.");
            await engine.StartRoutine(routine with { Trigger = new() { Type = TriggerType.Manual }, Actions = [Step(ActionType.PowerAction, ("powerAction", "Sleep"), ("gracePeriodSeconds", 0))] });
            Require(adapter.PowerActions == 1, "La energía sólo se ejecuta al añadir su acción al flujo.");
            Console.WriteLine("[PASS] Pipeline lineal: orden, omisión de apps, audio, cancelación, persistencia y migración sin energía obligatoria.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[FAIL] Pipeline lineal: " + ex); return 1; }
    }
    internal static WorkflowActionItem Step(ActionType type, params (string Key, object Value)[] values) => new()
    { ActionType = type, Parameters = values.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)) };
    private static async Task VerifyLiveSchedule(WorkflowEngine engine, WorkflowTestAdapter adapter, PresetDefinition routine)
    {
        var trigger = new RoutineTrigger { Type = TriggerType.ScheduledTime, Parameters = new()
        {
            ["timeOfDay"] = JsonSerializer.SerializeToElement(DateTime.Now.AddSeconds(2).ToString("HH:mm:ss")),
            ["daysOfWeek"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, 7).ToArray()),
            ["repeat"] = JsonSerializer.SerializeToElement(true)
        } };
        var execution = engine.StartRoutine(routine with { Trigger = trigger, Actions = [Step(ActionType.LaunchApp, ("executablePath", "schedule.exe"))] });
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!adapter.Calls.Contains("Launch:schedule.exe") && DateTime.UtcNow < deadline) await Task.Delay(10);
        await Task.Delay(50);
        Require(adapter.Calls.Count(call => call == "Launch:schedule.exe") == 1 && engine.IsRoutineRunning(routine.Id) &&
            engine.GetStatusSnapshot().EngineState == EngineState.WaitingTrigger, "El horario ejecuta una vez y vuelve a esperar el próximo día.");
        engine.StopRoutine(routine.Id);
        await execution.WaitAsync(TimeSpan.FromSeconds(2));
        Require(!engine.IsRoutineRunning(routine.Id), "Finalizar desarma un horario recurrente.");
    }
    private static void VerifySchedules()
    {
        var trigger = new RoutineTrigger { Type = TriggerType.ScheduledTime, Parameters = new()
        {
            ["timeOfDay"] = JsonSerializer.SerializeToElement("09:30:00"),
            ["daysOfWeek"] = JsonSerializer.SerializeToElement(new[] { 1, 3, 5 })
        } };
        var sunday = new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero);
        Require(RoutineSchedule.NextOccurrence(trigger, sunday, TimeZoneInfo.Utc) == new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero), "El horario conserva la hora local y los días elegidos.");
        var monday = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        Require(RoutineSchedule.NextOccurrence(trigger, monday, TimeZoneInfo.Utc).Day == 7, "Un horario vencido salta al siguiente día seleccionado.");
        Require(RoutineSchedule.NextOccurrence(trigger, monday.AddHours(-2), TimeZoneInfo.Utc, new(2026, 10, 5)).Day == 7, "Una recurrencia no ejecuta dos veces la misma fecha.");
        var dst = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var spring = trigger with { Parameters = new() { ["timeOfDay"] = JsonSerializer.SerializeToElement("02:30:00") } };
        var next = RoutineSchedule.NextOccurrence(spring, new(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), dst);
        Require(TimeZoneInfo.ConvertTime(next, dst).Hour == 3, "El salto DST no crea una hora imposible.");
        Console.WriteLine("[PASS] Horarios: días semanales, siguiente fecha, recurrencia sin duplicados y transición DST.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
