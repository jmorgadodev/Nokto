using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Applications;

internal sealed class ManualWindowProbeApp : Application
{
    public static string Token = "";
    public static bool RefuseClose;
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnLastWindowClose;
        Window Create(string suffix)
        {
            var window = new Window { Title = Token + suffix, Width = 220, Height = 120 };
            window.Closing += (_, e) => e.Cancel = RefuseClose;
            return window;
        }
        lifetime.MainWindow = Create("-one");
        lifetime.Startup += (_, _) => Create("-two").Show();
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class ManualWindowTests
{
    public static void Run()
    {
        Probe(false, (process, windows) =>
        {
            var one = windows[0];
            var expired = one with { ProcessStartTicks = one.ProcessStartTicks + 1 };
            ApplicationWindowService.CloseAsync([new(expired, false)], false).GetAwaiter().GetResult();
            Require(Windows(process.Id).Count == 2, "Una identidad caducada no cierra una ventana reutilizada.");
            ApplicationWindowService.CloseAsync([new(one, false)], false).GetAwaiter().GetResult();
            var remaining = Windows(process.Id);
            Require(remaining.Count == 1 && remaining[0].Handle != one.Handle, "Cerrar ventana concreta conserva la otra ventana.");
            ApplicationWindowService.CloseAsync([new(remaining[0], true)], false).GetAwaiter().GetResult();
            Require(process.WaitForExit(5000), "Cerrar todas las ventanas permite terminar el proceso normalmente.");
        });
        Probe(true, (process, windows) =>
        {
            using var cancellation = new CancellationTokenSource(300);
            try
            {
                ApplicationWindowService.CloseAsync([new(windows[0], true)], false, cancellation.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Se esperaba cancelación.");
            }
            catch (OperationCanceledException) { }
            Require(Windows(process.Id).Count == 2, "Cancelar la espera no fuerza el cierre.");
            var timer = Stopwatch.StartNew();
            try
            {
                ApplicationWindowService.CloseAsync([new(windows[0], true)], false).GetAwaiter().GetResult();
                throw new InvalidOperationException("Se esperaba rechazo de cierre.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("15 segundos")) { }
            Require(timer.Elapsed >= TimeSpan.FromSeconds(15) && timer.Elapsed < TimeSpan.FromSeconds(20) && Windows(process.Id).Count == 2,
                "Aplicación con cambios pendientes: 15 segundos de espera sin matar el proceso.");
        });
        Console.WriteLine("[PASS] Win32 real: identidad caducada, ventana individual, aplicación completa, cancelación y rechazo de WM_CLOSE tras 15 s.");
    }

    private static IReadOnlyList<ApplicationWindow> Windows(int pid) => ApplicationWindowService.GetWindows().Where(w => w.ProcessId == pid).ToArray();
    private static void Probe(bool refuse, Action<Process, IReadOnlyList<ApplicationWindow>> test)
    {
        string token = "NoktoWindowProbe-" + Guid.NewGuid().ToString("N");
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--manual-window-probe " + token + (refuse ? " refuse" : ""))
            { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IReadOnlyList<ApplicationWindow> windows;
            do { Thread.Sleep(50); windows = Windows(process.Id); } while (windows.Count != 2 && DateTime.UtcNow < deadline);
            Require(windows.Count == 2, "El proceso de prueba creó dos ventanas enumerables.");
            test(process, windows);
        }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
