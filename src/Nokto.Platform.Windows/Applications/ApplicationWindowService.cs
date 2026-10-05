using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.Applications;

/// <summary>Normal WM_CLOSE only. Never kills processes or falls back to a different foreground window.</summary>
public static class ApplicationWindowService
{
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    public static IReadOnlyList<ApplicationWindow> GetWindows()
    {
        var windows = new List<ApplicationWindow>();
        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) && TryReadIdentity(handle) is { } identity) windows.Add(identity);
            return true;
        }, IntPtr.Zero);
        return windows.OrderBy(w => w.ApplicationName, StringComparer.CurrentCultureIgnoreCase).ThenBy(w => w.Title).ToArray();
    }

    private static ApplicationWindow? ReadIdentity(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == GetShellWindow()) return null;
        if (NativeMethods.GetWindowThreadProcessId(handle, out var pid) == 0 || pid == Environment.ProcessId) return null;
        using var process = Process.GetProcessById((int)pid);
        if (process.ProcessName is "explorer" or "ShellExperienceHost" or "StartMenuExperienceHost" or "SearchHost" or "dwm") return null;
        string? path = process.MainModule?.FileName;
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("No se pudo identificar el ejecutable de la ventana.");
        var title = new StringBuilder(1024);
        GetWindowText(handle, title, title.Capacity);
        if (title.Length == 0) return null;
        return new(handle.ToInt64(), (int)pid, process.StartTime.ToUniversalTime().Ticks, path,
            title.ToString(), process.ProcessName);
    }

    private static ApplicationWindow? TryReadIdentity(IntPtr handle)
    {
        try { return ReadIdentity(handle); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
    }

    private static bool SameApplication(ApplicationWindow a, ApplicationWindow b)
    {
        if (!string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
        // Shared host executables do not uniquely identify their hosted application.
        return Path.GetFileNameWithoutExtension(a.ExecutablePath).ToLowerInvariant() is
            "applicationframehost" or "conhost" or "wscript" or "cscript" or "powershell" or "pwsh"
            ? a.ProcessId == b.ProcessId && a.ProcessStartTicks == b.ProcessStartTicks : true;
    }

    private static bool StillOpen(ApplicationWindow identity)
    {
        var handle = new IntPtr(identity.Handle);
        if (identity.ProcessId == Environment.ProcessId)
            throw new InvalidOperationException("Nokto no puede seleccionarse como objetivo de cierre.");
        if (NativeMethods.GetWindowThreadProcessId(handle, out var pid) == 0 || pid != identity.ProcessId) return false;
        Process? process = null;
        try
        {
            process = Process.GetProcessById((int)pid);
            return process.StartTime.ToUniversalTime().Ticks == identity.ProcessStartTicks &&
                string.Equals(process.MainModule?.FileName, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; } // Process has exited.
        catch (InvalidOperationException) when (process?.HasExited == true) { return false; }
        finally { process?.Dispose(); }
    }

    public static async Task CloseAsync(IReadOnlyList<ApplicationCloseTarget> targets, bool foregroundAtExecution,
        CancellationToken cancellationToken = default)
    {
        // Native identity reads run off the UI thread; request cancellation remains cooperative.
        await Task.Run(async () =>
        {
            var chosen = targets.ToList();
            if (foregroundAtExecution && ReadIdentity(NativeMethods.GetForegroundWindow()) is { } foreground)
                chosen.Add(new(foreground, false));
            var open = GetWindows();
            foreach (var target in chosen) _ = StillOpen(target.Window); // Surface access failures instead of treating them as closed.
            var toClose = chosen.SelectMany(target => target.AllApplicationWindows
                ? open.Where(w => SameApplication(w, target.Window))
                : StillOpen(target.Window) ? new[] { target.Window } : Array.Empty<ApplicationWindow>()).DistinctBy(w => w.Handle).ToArray();
            foreach (var window in toClose)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!StillOpen(window)) continue;
                if (!NativeMethods.PostMessage(new IntPtr(window.Handle), 0x0010, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"No se pudo solicitar el cierre de {window.ApplicationName}. No se ejecutarán las acciones restantes.");
            }
            var deadline = Stopwatch.StartNew();
            while (toClose.Any(StillOpen) || chosen.Where(t => t.AllApplicationWindows)
                .Any(t => GetWindows().Any(w => SameApplication(w, t.Window))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (deadline.Elapsed >= TimeSpan.FromSeconds(15))
                    throw new InvalidOperationException("Una aplicación sigue abierta tras 15 segundos. Revisa sus cambios pendientes. Se han detenido las acciones restantes.");
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
