using System.Text.Json;
using Nokto.Core.Models;
using Nokto.Core.Services;
using Nokto.Core.Serialization;

static void Ensure(bool ok, string name)
{
    if (!ok) throw new Exception("[FAIL] " + name);
    Console.WriteLine("[PASS] " + name);
}
var t = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);
var state = new IslandProductivityState { PomodoroWorkMinutes = 1, PomodoroBreakMinutes = 2 };
var engine = new IslandProductivityEngine(state);

engine.StartTimer(5, t);
Ensure(engine.TimerRemaining(t.AddMinutes(2)) == TimeSpan.FromMinutes(3), "Timer calcula tiempo real.");
engine.ToggleTimer(t.AddMinutes(2));
Ensure(!engine.AnyRunning && engine.TimerRemaining(t.AddMinutes(3)) == TimeSpan.FromMinutes(3),
    "Timer pausa sin consumir tiempo.");
engine.ToggleTimer(t.AddMinutes(3));
Ensure(engine.TimerRemaining(t.AddMinutes(5)) == TimeSpan.FromMinutes(1), "Timer reanuda.");
Ensure(engine.Tick(t.AddMinutes(6).AddSeconds(1)), "Timer vence en plazo.");
Ensure(engine.PendingAlert?.Kind == "timer", "Timer produce alerta única.");
engine.DismissAlert();

engine.ToggleStopwatch(t);
Ensure(engine.StopwatchElapsed(t.AddSeconds(20)) == TimeSpan.FromSeconds(20), "Cronómetro avanza.");
engine.ToggleStopwatch(t.AddSeconds(20));
Ensure(engine.StopwatchElapsed(t.AddMinutes(1)) == TimeSpan.FromSeconds(20), "Cronómetro pausa.");
engine.ResetStopwatch();

engine.StartPomodoro(t);
Ensure(engine.Tick(t.AddMinutes(1).AddSeconds(1)), "Pomodoro vence.");
Ensure(engine.PendingAlert?.Kind == "pomodoro" && state.PomodoroBreak, "Pomodoro ofrece descanso.");
Ensure(state.PomodoroDeadlineUtc is null, "Descanso no se inicia sin aprobación.");
engine.DismissAlert();

Ensure(engine.AddTask("Terminar presentación", t, 15), "Se añade tarea local.");
Ensure(engine.Tick(t.AddMinutes(15)), "Aviso de tarea vence.");
Ensure(engine.PendingAlert?.Kind == "reminder", "Recordatorio notifica.");
Ensure(!engine.Tick(t.AddMinutes(16)), "Aviso de tarea no se duplica.");
Ensure(engine.CompleteTask(state.Tasks[0].Id), "Tarea completada.");
engine.DismissAlert();

var localNow = DateTimeOffset.Now;
Ensure(engine.AddAlarm(localNow.AddMinutes(2).ToString("HH:mm"), false, localNow),
    "Alarma única programa hora local.");
var once = state.Alarms.Last();
Ensure(engine.Tick(once.DueUtc.AddSeconds(1)), "Alarma única se activa.");
Ensure(!once.Enabled && engine.PendingAlert?.Kind == "alarm", "Alarma única se desactiva tras vencer.");
engine.DismissAlert();
Ensure(engine.AddAlarm(localNow.AddMinutes(2).ToString("HH:mm"), true, localNow),
    "Alarma diaria programada.");
var daily = state.Alarms.Last();
var due = daily.DueUtc;
Ensure(engine.Tick(due.AddSeconds(1)) && daily.Enabled && daily.DueUtc > due,
    "Alarma diaria se reprograma en día siguiente.");

var config = new AppConfig { Island = new IslandSettings { Productivity = state } };
byte[] json = JsonSerializer.SerializeToUtf8Bytes(config, NoktoJsonContext.Default.AppConfig);
var restored = JsonSerializer.Deserialize(json, NoktoJsonContext.Default.AppConfig);
Ensure(restored?.Island.Productivity.Tasks.Count == 1 && restored.Island.Productivity.Alarms.Count == 2,
    "Persistencia local JSON de tareas y alarmas.");
Console.WriteLine("[PASS] Suite de productividad completada.");