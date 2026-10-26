using Nokto.Core.Models;

namespace Nokto.Core.Services;

/// <summary>
/// Núcleo sin UI. No sondea el sistema: el anfitrión llama Tick cuando existen
/// trabajos activos. Se basa en vencimientos absolutos UTC para resistir reinicios.
/// No despierta a Windows mientras está suspendido.
/// </summary>
public sealed class IslandProductivityEngine
{
    public IslandProductivityState State { get; }
    public IslandProductivityAlert? PendingAlert { get; private set; }

    public IslandProductivityEngine(IslandProductivityState state)
    {
        State = state;
        State.Alarms ??= [];
        State.Tasks ??= [];
        State.PomodoroWorkMinutes = Math.Clamp(State.PomodoroWorkMinutes, 1, 180);
        State.PomodoroBreakMinutes = Math.Clamp(State.PomodoroBreakMinutes, 1, 60);
    }

    public static TimeSpan Remaining(DateTimeOffset? deadline, TimeSpan paused, DateTimeOffset now)
        => deadline is { } t ? (t > now ? t - now : TimeSpan.Zero)
                            : paused > TimeSpan.Zero ? paused : TimeSpan.Zero;

    public TimeSpan TimerRemaining(DateTimeOffset now) => Remaining(State.TimerDeadlineUtc, State.TimerPausedRemaining, now);
    public TimeSpan PomodoroRemaining(DateTimeOffset now) => Remaining(State.PomodoroDeadlineUtc, State.PomodoroPausedRemaining, now);
    public TimeSpan StopwatchElapsed(DateTimeOffset now) => State.StopwatchAccumulated +
        (State.StopwatchStartedUtc is { } start && now > start ? now - start : TimeSpan.Zero);

    public bool AnyRunning => State.TimerDeadlineUtc.HasValue ||
        State.PomodoroDeadlineUtc.HasValue || State.StopwatchStartedUtc.HasValue;

    public bool HasScheduled => State.Alarms.Any(a => a.Enabled) ||
        State.Tasks.Any(t => !t.Completed && t.ReminderUtc.HasValue);

    public bool NeedsClock => AnyRunning || HasScheduled;

    public void StartTimer(int minutes, DateTimeOffset now)
    {
        State.TimerPausedRemaining = TimeSpan.Zero;
        State.TimerDeadlineUtc = now.AddMinutes(Math.Clamp(minutes, 1, 240));
        PendingAlert = null;
    }
    public void ToggleTimer(DateTimeOffset now)
    {
        if (State.TimerDeadlineUtc.HasValue)
        {
            State.TimerPausedRemaining = TimerRemaining(now);
            State.TimerDeadlineUtc = null;
        }
        else if (State.TimerPausedRemaining > TimeSpan.Zero)
        {
            State.TimerDeadlineUtc = now + State.TimerPausedRemaining;
            State.TimerPausedRemaining = TimeSpan.Zero;
        }
    }
    public void ResetTimer()
    {
        State.TimerDeadlineUtc = null;
        State.TimerPausedRemaining = TimeSpan.Zero;
    }
    public void ToggleStopwatch(DateTimeOffset now)
    {
        if (State.StopwatchStartedUtc is { } start)
        {
            if (now > start) State.StopwatchAccumulated += now - start;
            State.StopwatchStartedUtc = null;
        }
        else State.StopwatchStartedUtc = now;
    }
    public void ResetStopwatch()
    {
        State.StopwatchStartedUtc = null;
        State.StopwatchAccumulated = TimeSpan.Zero;
    }
    public void StartPomodoro(DateTimeOffset now)
    {
        State.PomodoroPausedRemaining = TimeSpan.Zero;
        State.PomodoroDeadlineUtc = now.AddMinutes(
            State.PomodoroBreak ? State.PomodoroBreakMinutes : State.PomodoroWorkMinutes);
        PendingAlert = null;
    }
    public void TogglePomodoro(DateTimeOffset now)
    {
        if (State.PomodoroDeadlineUtc.HasValue)
        {
            State.PomodoroPausedRemaining = PomodoroRemaining(now);
            State.PomodoroDeadlineUtc = null;
        }
        else if (State.PomodoroPausedRemaining > TimeSpan.Zero)
        {
            State.PomodoroDeadlineUtc = now + State.PomodoroPausedRemaining;
            State.PomodoroPausedRemaining = TimeSpan.Zero;
        }
        else StartPomodoro(now);
    }
    public void ResetPomodoro()
    {
        State.PomodoroDeadlineUtc = null;
        State.PomodoroPausedRemaining = TimeSpan.Zero;
        State.PomodoroBreak = false;
    }

    public bool AddAlarm(string time, bool daily, DateTimeOffset localNow)
    {
        if (!TimeSpan.TryParseExact(time, new[] { "hh\\:mm", "h\\:mm" },
                System.Globalization.CultureInfo.InvariantCulture, out var local) ||
            local < TimeSpan.Zero || local >= TimeSpan.FromDays(1) ||
            State.Alarms.Count >= 12) return false;
        var next = localNow.LocalDateTime.Date + local;
        if (next <= localNow.LocalDateTime) next = next.AddDays(1);
        State.Alarms.Add(new IslandAlarmItem
        {
            LocalTime = local,
            DueUtc = new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next)).ToUniversalTime(),
            Daily = daily
        });
        return true;
    }
    public bool RemoveAlarm(string id) => State.Alarms.RemoveAll(x => x.Id == id) > 0;

    public bool AddTask(string text, DateTimeOffset now, int reminderMinutes = 0)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > 120 || State.Tasks.Count >= 30 ||
            reminderMinutes < 0 || reminderMinutes > 10080) return false;
        State.Tasks.Add(new IslandTaskItem
        {
            Text = text, ReminderUtc = reminderMinutes > 0 ? now.AddMinutes(reminderMinutes) : null
        });
        return true;
    }
    public bool CompleteTask(string id)
    {
        var task = State.Tasks.FirstOrDefault(t => t.Id == id);
        if (task is null) return false;
        task.Completed = !task.Completed;
        task.ReminderUtc = null;
        return true;
    }
    public bool RemoveTask(string id) => State.Tasks.RemoveAll(t => t.Id == id) > 0;
    public void DismissAlert() => PendingAlert = null;

    /// <summary>Devuelve true únicamente cuando cambia un dato persistido.</summary>
    public bool Tick(DateTimeOffset now)
    {
        bool modified = false;
        if (State.TimerDeadlineUtc is { } timer && timer <= now)
        {
            ResetTimer();
            Notify("timer", "Temporizador terminado", now);
            modified = true;
        }
        if (State.PomodoroDeadlineUtc is { } pomo && pomo <= now)
        {
            State.PomodoroDeadlineUtc = null;
            State.PomodoroPausedRemaining = TimeSpan.Zero;
            string message = State.PomodoroBreak ? "Descanso terminado" : "Sesión de enfoque terminada";
            State.PomodoroBreak = !State.PomodoroBreak;
            Notify("pomodoro", message, now);
            modified = true;
        }
        foreach (var alarm in State.Alarms.ToArray())
        {
            if (!alarm.Enabled || alarm.DueUtc > now) continue;
            Notify("alarm", alarm.Label, now);
            if (alarm.Daily)
            {
                var next = now.LocalDateTime.Date + alarm.LocalTime;
                if (next <= now.LocalDateTime) next = next.AddDays(1);
                alarm.DueUtc = new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next)).ToUniversalTime();
            }
            else alarm.Enabled = false;
            modified = true;
        }
        foreach (var task in State.Tasks)
        {
            if (task.Completed || task.ReminderUtc is not { } due || due > now) continue;
            task.ReminderUtc = null;
            Notify("reminder", task.Text, now);
            modified = true;
        }
        return modified;
    }

    private void Notify(string kind, string text, DateTimeOffset now)
    {
        // Una alerta a la vez, conservando prioridad de alarmas cuando coinciden.
        if (PendingAlert is null || kind == "alarm")
            PendingAlert = new IslandProductivityAlert(kind, text, now);
    }
}
