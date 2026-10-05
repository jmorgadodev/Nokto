namespace Nokto.Core.Models;

/// <summary>A window identity captured locally; PID and creation time guard against handle reuse.</summary>
public sealed record ApplicationWindow(long Handle, int ProcessId, long ProcessStartTicks,
    string ExecutablePath, string Title, string ApplicationName)
{
    public string DisplayName => $"{ApplicationName} — {Title}";
}

public sealed record ApplicationCloseTarget(ApplicationWindow Window, bool AllApplicationWindows);
