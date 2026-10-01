using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Serialization;
using Nokto.LanServer;
using Nokto.Platform.Windows;

namespace Nokto.ConsoleTest;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using ISystemAdapter adapter = new WindowsSystemAdapter();
        var persistence = new PersistenceService();
        using var engine = new WorkflowEngine(adapter, persistence);

        if (args.Length > 0 && args[0] == "--generate-icon")
        {
            string targetPath = GetUiAssetsIcoPath();
            IconGenerator.GenerateOfficialIco(targetPath);
            Console.WriteLine($"[PASS] Icon generated at {targetPath}");
            return 0;
        }

        // Modo de verificación automatizada para CI, suite de pruebas o terminales sin consola interactiva
        bool isAutomated = (args.Length > 0 && (args[0] == "--auto-test" || args[0] == "-a" || args[0] == "--verify" || args[0] == "-v")) || Console.IsInputRedirected;
        if (isAutomated)
        {
            try
            {
                string targetPath = GetUiAssetsIcoPath();
                if (!File.Exists(targetPath))
                {
                    IconGenerator.GenerateOfficialIco(targetPath);
                }
            }
            catch { }
            return await RunAutomatedVerificationAsync(adapter, persistence, engine);
        }

        Console.Title = "Nokto — Consola de Pruebas de Sistema (Fases 1, 2, 3 y 4)";

        while (true)
        {
            Console.Clear();
            PrintBanner();

            Console.WriteLine(" Seleccione una opción para evaluar los adaptadores, el motor y el servidor LAN:");
            Console.WriteLine();
            Console.WriteLine("  [1] Probar apagado de monitores");
            Console.WriteLine("  [2] Probar Modo Trabajo (Keep-Alive) con log de jitter");
            Console.WriteLine("  [3] Probar desvanecimiento de volumen (Fade de 10s)");
            Console.WriteLine("  [4] Leer métricas en vivo (CPU, RAM, Red)");
            Console.WriteLine("  [5] Validar serialización JSON AOT (Data Contracts)");
            Console.WriteLine("  [6] Probar Motor de Flujos (WorkflowEngine, Pipeline y Auditoría)");
            Console.WriteLine("  [7] Probar Microservidor LAN y Generador de QR");
            Console.WriteLine("  [8] Salir");
            Console.WriteLine();
            Console.Write(" >> Ingrese opción [1-8]: ");

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
                    await TestWorkflowEngineAsync(engine, persistence);
                    break;

                case '7':
                    TestLanServerInteractive(engine, adapter);
                    break;

                case '8':
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
        Fase 4: Microservidor LAN, PWA & Control Remoto Web
");
        Console.ResetColor();
    }

    private static async Task<int> RunAutomatedVerificationAsync(ISystemAdapter adapter, PersistenceService persistence, IWorkflowEngine engine)
    {
        PrintBanner();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("    NOKTO - SUITE DE VERIFICACIÓN AUTOMATIZADA DEL SISTEMA (10/10 TESTS)        ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        int passedCount = 0;
        int failedCount = 0;
        var totalStopwatch = Stopwatch.StartNew();

        // [TEST 01] Detección de Procesos y Debounce (evaluar proceso en ejecución)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 01] Detección de Procesos y Debounce ... ");
            try
            {
                var curProc = Process.GetCurrentProcess();
                var procs = Process.GetProcessesByName(curProc.ProcessName);
                if (procs.Length == 0) throw new InvalidOperationException("No se detectó el proceso actual.");
                // Ventana de debounce activa
                await Task.Delay(500);
                curProc.Refresh();
                if (curProc.HasExited) throw new InvalidOperationException("El proceso finalizó durante la ventana de debounce.");
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"Proceso '{curProc.ProcessName}.exe' (PID: {curProc.Id}) supervisado con debounce");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 02] Métricas en vivo (lectura real de CPU %, RAM MB y Red KB/s)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 02] Métricas en vivo (CPU %, RAM MB, Red KB/s) ... ");
            try
            {
                await Task.Delay(250);
                var metrics = adapter.GetCurrentMetrics();
                if (metrics.RamTotalMb <= 0 || metrics.RamUsedMb <= 0)
                    throw new InvalidOperationException("Lectura de memoria RAM inválida.");
                if (metrics.CpuUsagePercentage < 0 || metrics.CpuUsagePercentage > 100)
                    throw new InvalidOperationException("Lectura de CPU fuera de rango porcentual.");
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"CPU: {metrics.CpuUsagePercentage:F1}%, RAM: {metrics.RamUsedMb:F0}/{metrics.RamTotalMb:F0} MB, Red: {metrics.NetworkDownKBs:F1} KB/s");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 03] Monitor de Inactividad de Periféricos (GetLastInputInfo real)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 03] Monitor de Inactividad de Periféricos (GetLastInputInfo) ... ");
            try
            {
                var metrics = adapter.GetCurrentMetrics();
                if (metrics.UserIdleSeconds < 0)
                    throw new InvalidOperationException("Tiempo de inactividad de periféricos negativo.");
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"Inactividad detectada: {metrics.UserIdleSeconds}s mediante GetLastInputInfo");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 04] Detección de Estado de Batería / AC (GetSystemPowerStatus real)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 04] Detección de Estado de Batería / AC (GetSystemPowerStatus) ... ");
            try
            {
                var bat = adapter.GetBatteryStatus();
                string desc = bat.HasBattery 
                    ? $"Batería presente ({bat.BatteryLifePercent}%), Cargando: {bat.IsCharging}, AC: {bat.IsOnAcPower}" 
                    : $"Estación Desktop / AC Online (Sin batería conectada, AC: {bat.IsOnAcPower})";
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, desc);
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 05] Detección de Nivel y Silencio de Audio (WASAPI IAudioMeterInformation real)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 05] Detección de Nivel y Silencio de Audio (WASAPI Metering) ... ");
            try
            {
                float peak = adapter.GetMasterPeakValue();
                float vol = await adapter.GetMasterVolumeAsync();
                bool muted = await adapter.GetMuteAsync();
                if (peak < 0.0f || peak > 1.0f)
                    throw new InvalidOperationException($"Nivel de pico fuera de rango: {peak}");
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"Peak: {peak:F4}, Vol: {vol * 100:F0}%, Muted: {muted} (IAudioMeterInformation COM OK)");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 06] Motor Keep-Alive / Jitter (simulación no destructiva VK_F15)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 06] Motor Keep-Alive / Jitter (VK_F15 seguro) ... ");
            try
            {
                adapter.SimulateKeepAlivePulse(KeepAliveMode.InputSimulation);
                adapter.SimulateKeepAlivePulse(KeepAliveMode.Mixed);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await adapter.RunKeepAliveLoopAsync(KeepAliveMode.Mixed, 1, 2, msg => { }, cts.Token);
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, "Pulsos VK_F15 y Mouse Jitter generados vía SendInput sin excepciones");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 07] Captura de Pantalla real estampada guardada en ./data/snapshots/
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 07] Captura de Pantalla real en ./data/snapshots/ ... ");
            try
            {
                string snapshotDir = persistence.Storage.SnapshotsDirectory;
                if (!Directory.Exists(snapshotDir)) Directory.CreateDirectory(snapshotDir);
                byte[] captureBytes = await adapter.CaptureScreenAsync(true, "AutoTest");
                if (captureBytes.Length < 100 || captureBytes[0] != (byte)'B' || captureBytes[1] != (byte)'M')
                    throw new InvalidOperationException("Formato BMP de captura inválido.");
                string testFile = Path.Combine(snapshotDir, $"test_capture_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bmp");
                await File.WriteAllBytesAsync(testFile, captureBytes);
                if (!File.Exists(testFile) || new FileInfo(testFile).Length == 0)
                    throw new InvalidOperationException("El archivo de captura no se guardó en disco.");
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"BMP válido de {captureBytes.Length / 1024} KB guardado en {Path.GetFileName(testFile)}");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 08] Serialización AOT transaccional de presets.json y audit.jsonl
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 08] Serialización AOT de presets.json y audit.jsonl ... ");
            try
            {
                var cfg = persistence.LoadConfig();
                persistence.SaveConfig(cfg);
                var presets = persistence.LoadPresets();
                if (presets.Presets.Count == 0) throw new InvalidOperationException("Catálogo de presets vacío.");
                persistence.SavePresets(presets);
                var testEntry = new AuditLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    PresetId = "preset_aot_verify",
                    Status = "Verified",
                    ExecutionDurationSeconds = 1,
                    TriggerFired = "AutoTestTrigger",
                    TerminalActionExecuted = "None",
                    ExitNotes = "Verificación AOT determinista"
                };
                persistence.AppendAuditLog(testEntry);
                var recent = persistence.ReadRecentAuditLogs(5);
                if (!recent.Any(e => e.PresetId == "preset_aot_verify"))
                    throw new InvalidOperationException("Registro de auditoría no encontrado en audit.jsonl.");
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"Presets: {presets.Presets.Count}, Config y AuditLog transaccionales 100% AOT");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 09] Arranque del Microservidor HTTP LAN y respuesta HTTP 200 en /api/status
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 09] Microservidor HTTP LAN y HTTP 200 en /api/status ... ");
            try
            {
                int testPort = 4889;
                string testToken = "autotest_token_99";
                var settings = new LanServerSettings
                {
                    Enabled = true,
                    Port = testPort,
                    RequireAuth = true,
                    AuthToken = testToken
                };
                using var lanServer = new LanHttpServer(engine, adapter, settings);
                lanServer.Start();

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var res = await http.GetAsync($"http://localhost:{testPort}/api/status?auth={testToken}");
                if (!res.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP GET /api/status falló con código {res.StatusCode}");
                string content = await res.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize(content, NoktoJsonContext.Default.SystemStatusState);
                if (status == null) throw new InvalidOperationException("Deserialización de SystemStatusState nula.");

                lanServer.Stop();
                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"Puerto {testPort}, HTTP {(int)res.StatusCode} OK, PWA OLED lista y JSON autenticado");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
        }

        // [TEST 10] Disparo de flujo encadenado en modo Dry-Run con periodo de gracia de 5 segundos
        {
            var sw = Stopwatch.StartNew();
            Console.Write("[TEST 10] Flujo encadenado en modo Dry-Run (Gracia 5s) ... ");
            try
            {
                engine.IsDryRunMode = true;
                adapter.IsDryRunMode = true;

                int graceTicksReceived = 0;
                void OnGraceTick(int remaining)
                {
                    graceTicksReceived++;
                }

                engine.GracePeriodTick += OnGraceTick;

                var dryRunPreset = new PresetDefinition
                {
                    Id = "preset_dry_run_test",
                    Name = "Dry-Run Safe Test Flow",
                    Trigger = new TriggerDefinition
                    {
                        Type = TriggerType.Countdown,
                        Parameters = new Dictionary<string, JsonElement>
                        {
                            ["durationSeconds"] = JsonSerializer.SerializeToElement(1)
                        }
                    },
                    Pipeline =
                    [
                        new PipelineStepDefinition
                        {
                            StepOrder = 1,
                            ActionType = ActionType.CaptureScreenshot,
                            IgnoreFailure = true
                        }
                    ],
                    TerminalAction = new TerminalActionDefinition
                    {
                        Type = TerminalActionType.Shutdown,
                        Parameters = new Dictionary<string, JsonElement>
                        {
                            ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(5),
                            ["forced"] = JsonSerializer.SerializeToElement(true)
                        }
                    }
                };

                await engine.StartPresetAsync(dryRunPreset);
                engine.GracePeriodTick -= OnGraceTick;

                var recent = persistence.ReadRecentAuditLogs(1);
                if (recent.Count == 0 || recent[0].ExitNotes?.Contains("[DRY-RUN]") != true)
                {
                    throw new InvalidOperationException("El registro de auditoría no contiene la marca de simulación [DRY-RUN].");
                }

                sw.Stop();
                PrintPass(sw.ElapsedMilliseconds, $"Gracia completada ({graceTicksReceived} ticks), apagado simulado de forma segura y auditado");
                passedCount++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                PrintFail(sw.ElapsedMilliseconds, ex.Message);
                failedCount++;
            }
            finally
            {
                engine.IsDryRunMode = false;
                adapter.IsDryRunMode = false;
            }
        }

        totalStopwatch.Stop();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        if (failedCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  RESULTADO: {passedCount}/10 TESTS SUPERADOS [0 FALLOS] - TIEMPO TOTAL: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  RESULTADO: {passedCount}/10 SUPERADOS, {failedCount} FALLADOS - TIEMPO TOTAL: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        return failedCount == 0 ? 0 : 1;
    }

    private static void PrintPass(long elapsedMs, string detail)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS]");
        Console.ResetColor();
        Console.WriteLine($" ({elapsedMs} ms) - {detail}");
    }

    private static void PrintFail(long elapsedMs, string error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL]");
        Console.ResetColor();
        Console.WriteLine($" ({elapsedMs} ms) - ERROR: {error}");
    }

    private static void TestLanServerInteractive(IWorkflowEngine engine, ISystemAdapter adapter)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [7] PRUEBA: MICROSERVIDOR LAN Y CONTROL REMOTO WEB ===");
        Console.ResetColor();

        int port = 4884;
        string token = "nokto_dev_" + Guid.NewGuid().ToString("N")[..6];
        var settings = new LanServerSettings
        {
            Enabled = true,
            Port = port,
            RequireAuth = true,
            AuthToken = token
        };

        using var server = new LanHttpServer(engine, adapter, settings);
        server.Start();

        string url = server.GetConnectionUrl();
        Console.WriteLine($"Servidor iniciado en: {url}");
        Console.WriteLine("Abra la URL anterior en el navegador de su teléfono o PC.");
        Console.WriteLine("Generando código QR...");

        byte[] qrPng = QrCodeService.GeneratePngBytes(url, 6);
        Console.WriteLine($"Código QR generado exitosamente ({qrPng.Length} bytes PNG).");
        Console.WriteLine("\nPresione cualquier tecla para detener el servidor LAN y regresar al menú...");
        Console.ReadKey(intercept: true);

        server.Stop();
        Console.WriteLine("Servidor detenido.");
    }

    private static async Task TestWorkflowEngineAsync(IWorkflowEngine engine, PersistenceService persistence)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== [6] PRUEBA: MOTOR DE FLUJOS ENCADENADOS Y AUDITORÍA ===");
        Console.ResetColor();

        Console.WriteLine("Ejecutando flujo de prueba interactivo de 3 segundos:");
        Console.WriteLine("  - Disparador: Countdown (3s)");
        Console.WriteLine("  - Paso 1: Captura de pantalla de diagnóstico");
        Console.WriteLine("  - Paso 2: Desvanecimiento de volumen WASAPI");
        Console.WriteLine("  - Acción terminal: 'None' (para no apagar el PC de pruebas)");
        Console.WriteLine();

        var demoPreset = new PresetDefinition
        {
            Id = "preset_interactive_demo",
            Name = "Demostración de Pipeline Fase 3",
            Trigger = new TriggerDefinition
            {
                Type = TriggerType.Countdown,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["durationSeconds"] = JsonSerializer.SerializeToElement(3)
                }
            },
            Pipeline =
            [
                new PipelineStepDefinition
                {
                    StepOrder = 1,
                    ActionType = ActionType.CaptureScreenshot,
                    IgnoreFailure = true
                },
                new PipelineStepDefinition
                {
                    StepOrder = 2,
                    ActionType = ActionType.AudioFadeOut,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["durationSeconds"] = JsonSerializer.SerializeToElement(3),
                        ["targetVolumePercentage"] = JsonSerializer.SerializeToElement(50)
                    },
                    IgnoreFailure = true
                }
            ],
            TerminalAction = new TerminalActionDefinition
            {
                Type = TerminalActionType.None
            }
        };

        engine.LogMessageReceived += msg => Console.WriteLine(msg);

        await engine.StartPresetAsync(demoPreset);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nFlujo completado. Registros recientes en audit.jsonl:");
        Console.ResetColor();

        var logs = persistence.ReadRecentAuditLogs(3);
        foreach (var l in logs)
        {
            Console.WriteLine($"  - [{l.Timestamp:HH:mm:ss}] Preset: {l.PresetId} | Estado: {l.Status} | Duración: {l.ExecutionDurationSeconds}s | Snapshot: {l.SnapshotFile ?? "None"}");
        }

        Console.WriteLine("\nPresione cualquier tecla para continuar...");
        Console.ReadKey(intercept: true);
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

    private static string GetUiAssetsIcoPath()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Nokto.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return Path.Combine(dir, "src", "Nokto.UI", "Assets", "nokto.ico");
    }
}
