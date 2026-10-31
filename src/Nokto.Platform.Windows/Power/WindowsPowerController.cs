using System.ComponentModel;
using System.Runtime.InteropServices;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;
using Nokto.Platform.Windows.KeepAlive;

namespace Nokto.Platform.Windows.Power;

// Keep dispatch independent of audio/telemetry construction so every native
// decision can be verified with a backend that cannot affect the computer.
internal interface IWindowsPowerBackend
{
    int LastError { get; }
    bool EnableShutdownPrivilege();
    bool ExitWindows(NativeConstants.ExitWindowsFlags flags);
    bool InitiateShutdown(bool force, bool reboot);
    bool Suspend(bool hibernate, bool force);
    bool Lock();
    bool Display(bool turnOn);
}

internal sealed class WindowsPowerController(IWindowsPowerBackend backend)
{
    public Task ExecuteAsync(PowerAction action, bool force, bool dryRun, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (dryRun) return Task.CompletedTask;
        switch (action)
        {
            case PowerAction.Shutdown:
            case PowerAction.Restart:
                Require(backend.EnableShutdownPrivilege(), "Windows no concedió el privilegio de apagado.");
                var flags = action == PowerAction.Restart ? NativeConstants.ExitWindowsFlags.EWX_REBOOT :
                    NativeConstants.ExitWindowsFlags.EWX_SHUTDOWN | NativeConstants.ExitWindowsFlags.EWX_POWEROFF;
                if (force) flags |= NativeConstants.ExitWindowsFlags.EWX_FORCEIFHUNG;
                ct.ThrowIfCancellationRequested();
                if (!backend.ExitWindows(flags)) Require(backend.InitiateShutdown(force, action == PowerAction.Restart), "Windows rechazó el apagado o reinicio.");
                break;
            case PowerAction.Sleep:
            case PowerAction.Hibernate:
                Require(backend.EnableShutdownPrivilege(), "Windows no concedió el privilegio de suspensión.");
                ct.ThrowIfCancellationRequested();
                Require(backend.Suspend(action == PowerAction.Hibernate, force), "Windows rechazó la suspensión o hibernación.");
                break;
            case PowerAction.LockStation:
                Require(backend.Lock(), "Windows rechazó el bloqueo de la estación.");
                break;
            case PowerAction.Logoff:
                Require(backend.ExitWindows(NativeConstants.ExitWindowsFlags.EWX_LOGOFF | (force ? NativeConstants.ExitWindowsFlags.EWX_FORCE : 0)), "Windows rechazó el cierre de sesión.");
                break;
            case PowerAction.TurnOffMonitors:
                Require(backend.Display(false), "Windows no respondió al cambio de estado de las pantallas.");
                break;
        }
        return Task.CompletedTask;
    }

    public Task SetDisplayAsync(bool turnOn, bool dryRun, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!dryRun) Require(backend.Display(turnOn), "Windows no respondió al cambio de estado de las pantallas.");
        return Task.CompletedTask;
    }

    private void Require(bool success, string message)
    {
        if (!success) throw new Win32Exception(backend.LastError, message);
    }
}

internal sealed class WindowsPowerBackend : IWindowsPowerBackend
{
    public int LastError { get; private set; }
    private bool Remember(bool success) { LastError = Marshal.GetLastWin32Error(); return success; }
    public bool ExitWindows(NativeConstants.ExitWindowsFlags flags) => Remember(NativeMethods.ExitWindowsEx(flags, NativeConstants.SHTDN_REASON_FLAG_PLANNED));
    public bool InitiateShutdown(bool force, bool reboot) => Remember(NativeMethods.InitiateSystemShutdownEx(null, null, 0, force, reboot, NativeConstants.SHTDN_REASON_FLAG_PLANNED));
    public bool Suspend(bool hibernate, bool force) => Remember(NativeMethods.SetSuspendState(hibernate, force, false));
    public bool Lock() => Remember(NativeMethods.LockWorkStation());
    public bool Display(bool turnOn)
    {
        // A hung application must not block the workflow indefinitely.
        bool ok = SendMessageTimeout(NativeConstants.HWND_BROADCAST, NativeConstants.WM_SYSCOMMAND,
            (IntPtr)NativeConstants.SC_MONITORPOWER, (IntPtr)(turnOn ? NativeConstants.MONITOR_ON : NativeConstants.MONITOR_OFF),
            0x0002, 1000, out _) != IntPtr.Zero;
        Remember(ok);
        if (ok && turnOn) KeepAliveEngine.SendMouseJitter(1);
        return ok;
    }
    public bool EnableShutdownPrivilege()
    {
        if (!Remember(NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeConstants.TOKEN_ADJUST_PRIVILEGES | NativeConstants.TOKEN_QUERY, out var token))) return false;
        try
        {
            if (!Remember(NativeMethods.LookupPrivilegeValue(null, NativeConstants.SE_SHUTDOWN_NAME, out var luid))) return false;
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = NativeConstants.SE_PRIVILEGE_ENABLED } };
            return Remember(NativeMethods.AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)) && LastError == 0;
        }
        finally { NativeMethods.CloseHandle(token); }
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
