namespace Nokto.Core.Models;

/// <summary>Herramientas de productividad privadas, guardadas en el config portable.</summary>
public sealed class IslandProductivityState
{
    public bool Enabled { get; set; } = true;
    public bool CompactProgress { get; set; } = false;
    public bool AlertSound { get; set; } = true;
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;

    public DateTimeOffset? TimerDeadlineUtc { get; set; }
    public TimeSpan TimerPausedRemaining { get; set; } = TimeSpan.Zero;
    public DateTimeOffset? StopwatchStartedUtc { get; set; }
    public TimeSpan StopwatchAccumulated { get; set; } = TimeSpan.Zero;
    public bool PomodoroBreak { get; set; }
    public DateTimeOffset? PomodoroDeadlineUtc { get; set; }
    public TimeSpan PomodoroPausedRemaining { get; set; } = TimeSpan.Zero;
    public List<IslandAlarmItem> Alarms { get; set; } = [];
    public List<IslandTaskItem> Tasks { get; set; } = [];
}

public sealed class IslandAlarmItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Alarma";
    public TimeSpan LocalTime { get; set; }
    public bool Daily { get; set; }
    public DateTimeOffset DueUtc { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class IslandTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Completed { get; set; }
    public DateTimeOffset? ReminderUtc { get; set; }
}

public sealed record IslandProductivityAlert(string Kind, string Message, DateTimeOffset AtUtc);
