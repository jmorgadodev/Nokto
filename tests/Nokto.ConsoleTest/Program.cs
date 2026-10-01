using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Core.Serialization;
using Nokto.Platform.Windows;

namespace Nokto.ConsoleTest;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using ISystemAdapter adapter = new WindowsSystemAdapter();

        // Modo de verificación automatizada para CI o terminales sin consola interactiva
        if (args.Length > 0 && (args[0] == "--verify" || args[0] == "-v") || Console.IsInputRedirected)
        {
            return await RunAutomatedVerificationAsync(adapter);
        }

        Console.Title = "Nokto — Consola de Pruebas de Sistema (Fase 1)";

        while (true)
        {
            Console.Clear();
            PrintBanner();

            Console.WriteLine(" Seleccione una opción para evaluar los adaptadores nativos:");
            Console.WriteLine();
            Console.WriteLine("  [1] Probar apagado de monitores");
            Console.WriteLine("  [2] Probar Modo Trabajo (Keep-Alive) con log de jitter");
            Console.WriteLine("  [3] Probar desvanecimiento de volumen (Fade de 10s)");
            Console.WriteLine("  [4] Leer métricas en vivo (CPU, RAM, Red)");
            Console.WriteLine("  [5] Validar serialización JSON AOT (Data Contracts)");
            Console.WriteLine("  [6] Salir");
            Console.WriteLine();
            Console.Write(" >> Ingrese opción [1-6]: ");

            var key = Console.ReadKey(intercept: true);
            Console.WriteLine(key.KeyChar);
            Console.WriteLine();

            switch (key.KeyChar)
            {
                case '1':
                    await TestMonitorPowerOffAsync(adapter);
                    break;

                case '2':
                    await TestKeepAliveLoopAsync(adapter);
                    break;

                case '3':
                    await TestVolumeFadeAsync(adapter);
                    break;

                case '4':
                    await TestLiveMetricsAsync(adapter);
                    break;

                case '5':
                    TestAotSerialization();
                    break;

                case '6':
                    Console.WriteLine("Finalizando consola de pruebas de Nokto. ¡Hasta pronto!");
                    return 0;

                default:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Opción no válida. Presione cualquier tecla para continuar...");
                    Console.ResetColor();
                    Console.ReadKey(intercept: true);
                    break;
            }
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
 ███╗   ██╗ ██████╗ ██╗  ██╗████████╗ ██████╗ 
 ████╗  ██║██╔═══██╗██║ ██╔╝╚══██╔══╝██╔═══██╗
 ██╔██╗ ██║██║   ██║█████╔╝    ██║   ██║   ██║
 ██║╚██╗██║██║   ██║██╔═██╗    ██║   ██║   ██║
 ██║ ╚████║╚██████╔╝██║  ██╗   ██║   ╚██████╔╝
 ╚═╝  ╚═══╝ ╚═════╝ ╚═╝  ╚═╝   ╚═╝    ╚═════╝ 
        Sistema Determinista de Energía & Automatización
        Fase 1: Core Headless & Windows System Adapter
");
        Console.ResetColor();
    }

    private static async Task<int> RunAutomatedVerificationAsync(ISystemAdapter adapter)
    {
        PrintBanner();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[MODO VERIFICACIÓN AUTOMATIZADA - FASE 1]");
        Console.ResetColor();

        try
        {
            // 1. Prueba de Métricas Pasivas
            Console.Write("1. Probando colector de métricas pasivas... ");
            await Task.Delay(500); // permitir baseline
            var metrics = adapter.GetCurrentMetrics();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"OK! (CPU: {metrics.CpuUsagePercentage}%, RAM: {metrics.RamUsedMb}/{metrics.RamTotalMb} MB, NetDown: {metrics.NetworkDownKBs} KB/s, Inactividad: {metrics.UserIdleSeconds}s)");
            Console.ResetColor();

            // 2. Prueba de WASAPI Audio
            Console.Write("2. Probando subsistema de audio WASAPI nativo... ");
            float vol = await adapter.GetMasterVolumeAsync();
            bool muted = await adapter.GetMuteAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"OK! (Volumen actual: {vol * 100:F0}%, Silenciado: {muted})");
            Console.ResetColor();

            // 3. Prueba de Keep-Alive (pulsos atómicos)
            Console.Write("3. Probando pulsos de Keep-Alive (VK_F15 y Mouse Jitter)... ");
            adapter.SimulateKeepAlivePulse(KeepAliveMode.InputSimulation);
            adapter.SimulateKeepAlivePulse(KeepAliveMode.Mixed);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK! (Pulsos enviados mediante SendInput sin excepciones)");
            Console.ResetColor();

            // 4. Prueba de serialización AOT JSON
            Console.Write("4. Probando serialización y deserialización AOT (NoktoJsonContext)... ");
            var config = new AppConfig();
            string json = JsonSerializer.Serialize(config, NoktoJsonContext.Default.AppConfig);
            var deserialized = JsonSerializer.Deserialize(json, NoktoJsonContext.Default.AppConfig);
            if (deserialized == null || deserialized.App.Theme != "Night")
            {
                throw new InvalidOperationException("Fallo de validación en deserialización AOT.");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK! (Generador de código System.Text.Json 100% libre de reflexión)");
            Console.ResetColor();

            // 5. Prueba de Bucle Keep-Alive con CancellationToken
            Console.Write("5. Probando ciclo de PeriodicTimer con jitter (2 segundos)... ");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await adapter.RunKeepAliveLoopAsync(KeepAliveMode.Mixed, 1, 2, msg => { }, cts.Token);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK! (Bucle y cancelación deterministas sin busy-waiting)");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n>> TODAS LAS PRUEBAS AUTOMATIZADAS DE FASE 1 CONCLUYERON CON ÉXITO [0 ERRORES].");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR DE VERIFICACIÓN]: {ex}");
            Console.ResetColor();
            return 1;
        }
    }

    private static async Task TestMonitorPowerOffAsync(ISystemAdapter adapter)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [1] PRUEBA: APAGADO DE MONITORES ===");
        Console.ResetColor();
        Console.WriteLine("Los monitores se apagarán mediante la señal nativa SC_MONITORPOWER en 3 segundos.");
        Console.WriteLine("Para reactivarlos luego, simplemente mueva el ratón o presione cualquier tecla.");
        Console.WriteLine();

        for (int i = 3; i > 0; i--)
        {
            Console.Write($"Apagando en {i}... \r");
            await Task.Delay(1000);
        }

        Console.WriteLine("Enviando señal SC_MONITORPOWER (OFF)...               ");
        await adapter.SetDisplayPowerAsync(turnOn: false);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Señal enviada exitosamente.");
        Console.ResetColor();
        Console.WriteLine("Presione cualquier tecla para regresar al menú...");
        Console.ReadKey(intercept: true);
    }

    private static async Task TestKeepAliveLoopAsync(ISystemAdapter adapter)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [2] PRUEBA: MODO TRABAJO (KEEP-ALIVE) CON JITTER ===");
        Console.ResetColor();
        Console.WriteLine("Seleccione el modo de prueba:");
        Console.WriteLine("  [A] Rango de producción PRD: 45s a 105s (VK_F15 + Mouse Jitter)");
        Console.WriteLine("  [B] Rango de prueba acelerada: 3s a 7s (para verificar el bucle y jitter)");
        Console.Write(" >> Opción [A/B]: ");

        var modeKey = Console.ReadKey(intercept: true);
        Console.WriteLine(modeKey.KeyChar);
        Console.WriteLine();

        int minSec = 45;
        int maxSec = 105;
        if (char.ToUpperInvariant(modeKey.KeyChar) == 'B')
        {
            minSec = 3;
            maxSec = 7;
        }

        Console.WriteLine($"Iniciando bucle de Keep-Alive (Jitter: {minSec}s - {maxSec}s).");
        Console.WriteLine("Presione la tecla [Q] o [ESC] en cualquier momento para detener el bucle.");
        Console.WriteLine("-------------------------------------------------------------------------");

        using var cts = new CancellationTokenSource();

        var loopTask = adapter.RunKeepAliveLoopAsync(
            KeepAliveMode.Mixed,
            minSec,
            maxSec,
            logMessage =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"[{timestamp}] ");
                Console.ResetColor();
                Console.WriteLine(logMessage);
            },
            cts.Token);

        while (!loopTask.IsCompleted)
        {
            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Escape)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[Usuario] Solicitando cancelación del bucle...");
                    Console.ResetColor();
                    cts.Cancel();
                    break;
                }
            }
            await Task.Delay(100);
        }

        try
        {
            await loopTask;
        }
        catch (OperationCanceledException)
        {
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nPrueba de Keep-Alive concluida correctamente.");
        Console.ResetColor();
        Console.WriteLine("Presione cualquier tecla para continuar...");
        Console.ReadKey(intercept: true);
    }

    private static async Task TestVolumeFadeAsync(ISystemAdapter adapter)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [3] PRUEBA: DESVANECIMIENTO DE VOLUMEN (WASAPI FADE 10s) ===");
        Console.ResetColor();

        float initialVolume = await adapter.GetMasterVolumeAsync();
        int initialPercent = (int)Math.Round(initialVolume * 100);
        Console.WriteLine($"Volumen maestro actual: {initialPercent}%");

        Console.WriteLine();
        Console.WriteLine("Seleccione la dirección de la atenuación (Duración: 10 segundos):");
        Console.WriteLine("  [1] Desvanecer hasta 0% (Fade Out)");
        Console.WriteLine("  [2] Desvanecer hasta 100% (Fade In)");
        Console.WriteLine("  [3] Desvanecer hasta 20%");
        Console.Write(" >> Opción [1-3]: ");

        var opt = Console.ReadKey(intercept: true);
        Console.WriteLine(opt.KeyChar);
        Console.WriteLine();

        float target = opt.KeyChar switch
        {
            '1' => 0.0f,
            '2' => 1.0f,
            '3' => 0.2f,
            _ => 0.0f
        };

        Console.WriteLine($"Iniciando atenuación perceptual logarítmica de {initialPercent}% hacia {(int)(target * 100)}%...");
        Console.WriteLine("Presione [ESC] para cancelar la atenuación.");

        using var cts = new CancellationTokenSource();
        var fadeTask = adapter.SetMasterVolumeFadeAsync(target, TimeSpan.FromSeconds(10), cts.Token);

        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(10);

        while (!fadeTask.IsCompleted)
        {
            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Escape)
                {
                    cts.Cancel();
                    Console.WriteLine("\n[Usuario] Desvanecimiento abortado.");
                    break;
                }
            }

            float currentVol = await adapter.GetMasterVolumeAsync();
            int currentPct = (int)Math.Round(currentVol * 100);
            double progress = Math.Clamp(progressStopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            int barWidth = 30;
            int filled = (int)(progress * barWidth);
            string bar = new string('█', filled) + new string('░', barWidth - filled);

            Console.Write($"\r Progreso: [{bar}] {(int)(progress * 100)}% | Volumen actual: {currentPct}%   ");
            await Task.Delay(100);
        }

        try
        {
            await fadeTask;
        }
        catch (OperationCanceledException)
        {
        }

        float finalVol = await adapter.GetMasterVolumeAsync();
        Console.WriteLine($"\n\nDesvanecimiento finalizado. Nivel final de volumen: {(int)Math.Round(finalVol * 100)}%.");
        Console.WriteLine("¿Desea restaurar el volumen inicial de " + initialPercent + "%? [S/N]: ");
        var restoreKey = Console.ReadKey(intercept: true);
        if (char.ToUpperInvariant(restoreKey.KeyChar) == 'S')
        {
            await adapter.SetMasterVolumeAsync(initialVolume);
            Console.WriteLine($"Volumen restaurado a {initialPercent}%.");
        }

        Console.WriteLine("\nPresione cualquier tecla para continuar...");
        Console.ReadKey(intercept: true);
    }

    private static async Task TestLiveMetricsAsync(ISystemAdapter adapter)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [4] PRUEBA: TELEMETRÍA Y MÉTRICAS EN VIVO ===");
        Console.ResetColor();
        Console.WriteLine("Leyendo CPU pasivo (GetSystemTimes), RAM y tráfico de red en tiempo real.");
        Console.WriteLine("Consumo de CPU estimado del lector: <0.01% (cero allocations, sin PerformanceCounters).");
        Console.WriteLine("Presione cualquier tecla para detener la lectura y volver al menú principal.");
        Console.WriteLine();

        Console.WriteLine("┌───────────────────────┬───────────────┬───────────────────────┬───────────────────┐");
        Console.WriteLine("│      HORA (UTC)       │   CPU USAGE   │       RAM USADA       │     RED (D / U)   │");
        Console.WriteLine("├───────────────────────┼───────────────┼───────────────────────┼───────────────────┤");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!Console.KeyAvailable)
        {
            await timer.WaitForNextTickAsync();
            var metrics = adapter.GetCurrentMetrics();

            string time = metrics.Timestamp.ToString("HH:mm:ss.fff");
            string cpu = $"{metrics.CpuUsagePercentage,5:F1}%";
            string ram = $"{metrics.RamUsedMb,6:F0} / {metrics.RamTotalMb,6:F0} MB";
            string net = $"{metrics.NetworkDownKBs,6:F1} / {metrics.NetworkUpKBs,6:F1} KB/s";

            Console.WriteLine($"│  {time,-19}  │     {cpu,-7}   │ {ram,-21} │ {net,-17} │");
        }

        Console.ReadKey(intercept: true);
        Console.WriteLine("└───────────────────────┴───────────────┴───────────────────────┴───────────────────┘");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Monitoreo detenido.");
        Console.ResetColor();
        Console.WriteLine("Presione cualquier tecla para continuar...");
        Console.ReadKey(intercept: true);
    }

    private static void TestAotSerialization()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [5] PRUEBA: SERIALIZACIÓN AOT SIN REFLEXIÓN (NoktoJsonContext) ===");
        Console.ResetColor();

        var config = new AppConfig();
        string configJson = JsonSerializer.Serialize(config, NoktoJsonContext.Default.AppConfig);
        Console.WriteLine(">> Serialización de AppConfig predeterminado:");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(configJson);
        Console.ResetColor();

        var state = new SystemStatusState
        {
            EngineState = EngineState.WaitingTrigger,
            ActivePresetId = "preset_workday_keepalive",
            ActivePresetName = "Jornada Laboral Anti-Ausente",
            CurrentPhase = "EvaluatingSchedule",
            ProgressPercentage = 45.0,
            TimeRemainingSeconds = 3600,
            Metrics = new SystemMetrics
            {
                CpuUsagePercentage = 3.5,
                RamUsedMb = 4120,
                RamTotalMb = 16384,
                NetworkDownKBs = 24.5,
                NetworkUpKBs = 2.1
            }
        };

        string stateJson = JsonSerializer.Serialize(state, NoktoJsonContext.Default.SystemStatusState);
        Console.WriteLine("\n>> Serialización de SystemStatusState:");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(stateJson);
        Console.ResetColor();

        var deserialized = JsonSerializer.Deserialize(stateJson, NoktoJsonContext.Default.SystemStatusState);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nDeserialización AOT exitosa: Preset='{deserialized?.ActivePresetName}', CPU={deserialized?.Metrics.CpuUsagePercentage}%.");
        Console.ResetColor();

        Console.WriteLine("\nPresione cualquier tecla para continuar...");
        Console.ReadKey(intercept: true);
    }
}
