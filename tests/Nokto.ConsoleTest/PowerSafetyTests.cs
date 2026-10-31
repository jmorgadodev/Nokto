using System.ComponentModel;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;
using Nokto.Platform.Windows.Power;

namespace Nokto.ConsoleTest;

internal static class PowerSafetyTests
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var backend = new FakePower();
            var power = new WindowsPowerController(backend);
            foreach (var action in Enum.GetValues<PowerAction>()) await power.ExecuteAsync(action, true, true, default);
            await power.SetDisplayAsync(true, true, default);
            Check(backend.Calls.Count == 0, "Dry-run cubre apagado, reinicio, suspensión, hibernación, bloqueo, sesión y pantallas sin llamadas nativas");
            foreach (var action in Enum.GetValues<PowerAction>())
            {
                try { await power.ExecuteAsync(action, true, false, new CancellationToken(true)); throw new Exception("No respeta cancelación"); }
                catch (OperationCanceledException) { }
            }
            Check(backend.Calls.Count == 0, "La cancelación precede a cualquier acción del sistema");
            try { await power.ExecuteAsync((PowerAction)99, false, true, default); throw new Exception("Enum desconocido aceptado"); }
            catch (ArgumentOutOfRangeException) { }
            foreach (var action in Enum.GetValues<PowerAction>())
            {
                backend.Calls.Clear(); await power.ExecuteAsync(action, false, false, default);
                string expected = action switch
                {
                    PowerAction.Shutdown => "Exit:EWX_SHUTDOWN, EWX_POWEROFF", PowerAction.Restart => "Exit:EWX_REBOOT",
                    PowerAction.Sleep => "Suspend:False:False", PowerAction.Hibernate => "Suspend:True:False",
                    PowerAction.LockStation => "Lock", PowerAction.Logoff => "Exit:EWX_LOGOFF", _ => "Display:False"
                };
                Check(backend.Calls.Last() == expected, "Mapeo nativo simulado: " + action);
            }
            backend.Calls.Clear(); backend.ExitOk = false;
            await power.ExecuteAsync(PowerAction.Restart, true, false, default);
            Check(backend.Calls.Last() == "Fallback:True:True", "El fallback de reinicio conserva flags y comprueba el resultado");
            backend.FallbackOk = false;
            await MustFail(() => power.ExecuteAsync(PowerAction.Shutdown, true, false, default));
            await MustFail(() => power.ExecuteAsync(PowerAction.Logoff, false, false, default));
            backend.PrivilegeOk = false; backend.Calls.Clear();
            await MustFail(() => power.ExecuteAsync(PowerAction.Sleep, false, false, default));
            Check(backend.Calls.SequenceEqual(["Privilege"]), "Privilegio denegado no se informa como apagado/suspensión exitosa");
            backend.DisplayOk = false;
            await MustFail(() => power.SetDisplayAsync(false, false, default));
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[FAIL] Power: " + ex); return 1; }
    }
    private static async Task MustFail(Func<Task> run)
    {
        try { await run(); throw new Exception("Fallo nativo ignorado"); }
        catch (Win32Exception ex) { Check(ex.NativeErrorCode == 5, "Los rechazos de Windows conservan el código de error"); }
    }
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("[PASS] " + text); }
    private sealed class FakePower : IWindowsPowerBackend
    {
        public List<string> Calls { get; } = [];
        public int LastError => 5;
        public bool ExitOk = true, PrivilegeOk = true, FallbackOk = true, DisplayOk = true;
        public bool EnableShutdownPrivilege() { Calls.Add("Privilege"); return PrivilegeOk; }
        public bool ExitWindows(NativeConstants.ExitWindowsFlags flags) { Calls.Add("Exit:" + flags); return ExitOk; }
        public bool InitiateShutdown(bool force, bool reboot) { Calls.Add($"Fallback:{force}:{reboot}"); return FallbackOk; }
        public bool Suspend(bool hibernate, bool force) { Calls.Add($"Suspend:{hibernate}:{force}"); return true; }
        public bool Lock() { Calls.Add("Lock"); return true; }
        public bool Display(bool turnOn) { Calls.Add("Display:" + turnOn); return DisplayOk; }
    }
}
