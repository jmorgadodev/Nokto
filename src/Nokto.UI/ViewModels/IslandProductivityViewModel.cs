using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;

namespace Nokto.UI.ViewModels;

/// <summary>
/// Reloj local que sigue vivo mientras Nokto está en la bandeja aunque la isla
/// no se muestre. Guarda únicamente cambios significativos, nunca cada tick.
/// </summary>
public sealed class IslandProductivityViewModel : ObservableObject, IDisposable
{
    private readonly IslandProductivityEngine _engine;
    private readonly AppConfig _config;
    private readonly PersistenceService _persistence;
    private readonly DispatcherTimer _pulse;
    private bool _disposed;

    public event Action<IslandProductivityAlert>? AlertRaised;
    public event Action? Updated;

    public IslandProductivityViewModel(AppConfig config, PersistenceService persistence)
    {
        _config = config;
        _persistence = persistence;
        _config.Island.Productivity ??= new IslandProductivityState();
        _engine = new IslandProductivityEngine(_config.Island.Productivity);
        _pulse = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pulse.Tick += (_, _) => Refresh();
        Refresh();
    }

    public IslandProductivityEngine Engine => _engine;
    public bool Enabled
    {
        get => _engine.State.Enabled;
        set { if (_engine.State.Enabled == value) return; _engine.State.Enabled = value; Persist(); }
    }
    public bool CompactProgress
    {
        get => _engine.State.CompactProgress;
        set { if (_engine.State.CompactProgress == value) return; _engine.State.CompactProgress = value; Persist(); }
    }
    public bool AlertSound
    {
        get => _engine.State.AlertSound;
        set { if (_engine.State.AlertSound == value) return; _engine.State.AlertSound = value; Persist(); }
    }
    public int WorkMinutes
    {
        get => _engine.State.PomodoroWorkMinutes;
        set { _engine.State.PomodoroWorkMinutes = Math.Clamp(value, 1, 180); Persist(); }
    }
    public int BreakMinutes
    {
        get => _engine.State.PomodoroBreakMinutes;
        set { _engine.State.PomodoroBreakMinutes = Math.Clamp(value, 1, 60); Persist(); }
    }

    public string TimerText => Format(_engine.TimerRemaining(DateTimeOffset.UtcNow));
    public string StopwatchText => Format(_engine.StopwatchElapsed(DateTimeOffset.UtcNow));
    public string PomodoroText => Format(_engine.PomodoroRemaining(DateTimeOffset.UtcNow));
    public string PomodoroPhase => _engine.State.PomodoroBreak ? "Descanso" : "Enfoque";
    public string TimerAction => _engine.State.TimerDeadlineUtc is not null ? "Pausar" : "Reanudar";
    public string StopwatchAction => _engine.State.StopwatchStartedUtc is not null ? "Pausar" : "Iniciar";
    public string PomodoroAction => _engine.State.PomodoroDeadlineUtc is not null ? "Pausar" : "Iniciar";
    public bool HasAlert => _engine.PendingAlert is not null;
    public string AlertText => _engine.PendingAlert?.Message ?? "";
    public string CompactText => _engine.State.TimerDeadlineUtc.HasValue ? "⏱ " + TimerText
        : _engine.State.PomodoroDeadlineUtc.HasValue ? "◉ " + PomodoroText : "";
    public bool HasCompactText => Enabled && CompactProgress && CompactText.Length > 0;
    public string NextTask => _engine.State.Tasks.FirstOrDefault(t => !t.Completed)?.Text ?? "Sin tareas pendientes";
    public string NextAlarm => _engine.State.Alarms.Where(a => a.Enabled)
        .OrderBy(a => a.DueUtc).Select(a => $"{a.LocalTime:hh\\:mm}" + (a.Daily ? " · Diario" : " · Una vez"))
        .FirstOrDefault() ?? "Sin alarmas";
    public int PendingTasks => _engine.State.Tasks.Count(t => !t.Completed);
    public IReadOnlyList<IslandTaskItem> Tasks => _engine.State.Tasks;
    public IReadOnlyList<IslandAlarmItem> Alarms => _engine.State.Alarms;

    public void StartTimer(int minutes) => Apply(() => _engine.StartTimer(minutes, DateTimeOffset.UtcNow));
    public void ToggleTimer() => Apply(() => _engine.ToggleTimer(DateTimeOffset.UtcNow));
    public void ResetTimer() => Apply(_engine.ResetTimer);
    public void ToggleStopwatch() => Apply(() => _engine.ToggleStopwatch(DateTimeOffset.UtcNow));
    public void ResetStopwatch() => Apply(_engine.ResetStopwatch);
    public void TogglePomodoro() => Apply(() => _engine.TogglePomodoro(DateTimeOffset.UtcNow));
    public void ResetPomodoro() => Apply(_engine.ResetPomodoro);
    public bool AddAlarm(string time, bool daily)
    {
        bool ok = _engine.AddAlarm(time, daily, DateTimeOffset.Now);
        if (ok) Persist();
        return ok;
    }
    public bool RemoveNextAlarm()
    {
        var next = _engine.State.Alarms.Where(a => a.Enabled).OrderBy(a => a.DueUtc).FirstOrDefault();
        if (next is null) return false;
        bool ok = _engine.RemoveAlarm(next.Id);
        if (ok) Persist();
        return ok;
    }
    public bool AddTask(string text, int reminderMinutes)
    {
        bool ok = _engine.AddTask(text, DateTimeOffset.UtcNow, reminderMinutes);
        if (ok) Persist();
        return ok;
    }
    public bool CompleteNextTask()
    {
        var next = _engine.State.Tasks.FirstOrDefault(t => !t.Completed);
        if (next is null) return false;
        bool ok = _engine.CompleteTask(next.Id);
        if (ok) Persist();
        return ok;
    }
    public void DismissAlert() { _engine.DismissAlert(); Notify(); }

    private void Apply(Action action) { action(); Persist(); }
    private void Persist()
    {
        if (_disposed) return;
        _persistence.SaveConfig(_config);
        Notify();
        SchedulePulse();
    }
    private void Refresh()
    {
        if (_disposed) return;
        bool changed = _engine.Tick(DateTimeOffset.UtcNow);
        if (changed)
        {
            _persistence.SaveConfig(_config);
            if (_engine.PendingAlert is { } alert)
            {
                if (_engine.State.Enabled)
                {
                    if (_engine.State.AlertSound) _ = MessageBeep(0x40);
                    AlertRaised?.Invoke(alert);
                }
            }
        }
        Notify();
        SchedulePulse();
    }
    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(Enabled), nameof(CompactProgress), nameof(AlertSound), nameof(WorkMinutes), nameof(BreakMinutes),
            nameof(TimerText), nameof(StopwatchText), nameof(PomodoroText), nameof(PomodoroPhase),
            nameof(TimerAction), nameof(StopwatchAction), nameof(PomodoroAction),
            nameof(CompactText), nameof(HasCompactText), nameof(AlertText), nameof(HasAlert),
            nameof(NextTask), nameof(NextAlarm), nameof(PendingTasks), nameof(Tasks), nameof(Alarms)
        }) OnPropertyChanged(name);
        Updated?.Invoke();
    }
    private void SchedulePulse()
    {
        if (_disposed) return;
        if (!_engine.NeedsClock) { _pulse.Stop(); return; }
        _pulse.Interval = TimeSpan.FromSeconds(_engine.AnyRunning ? 1 : 10);
        _pulse.Start();
    }
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
             : $"{time.Minutes:00}:{time.Seconds:00}";
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);

    public void Dispose()
    {
        _disposed = true;
        _pulse.Stop();
    }
}
