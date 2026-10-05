using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.ConsoleTest;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;
using Nokto.UI;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

internal sealed class DemoSystemAdapter : ISystemAdapter
{
    public bool IsDryRunMode { get; set; } = true;
    public float Volume = 0.65f;
    public bool OutputMuted;
    public bool InputMuted;
    public byte[] CaptureBytes = [];

    public HardwareProfile GetHardwareProfile() => new()
    {
        OperatingSystemName = "Windows 11 Pro 23H2 (Build 22631.3880)",
        ProcessorName = "13th Gen Intel(R) Core(TM) i7-13700H (20 Hilos @ 5.0 GHz)",
        InstalledMemoryGigabytes = 32.0,
        GraphicsAdapterName = "NVIDIA GeForce RTX 4060 Laptop GPU (8GB GDDR6)",
        MonitorCount = 1,
        PrimaryScreenWidth = 1920,
        PrimaryScreenHeight = 1080,
        PrimaryScreenRefreshRateHz = 144
    };

    public SystemMetrics GetCurrentMetrics() => new()
    {
        CpuUsagePercentage = 5.4,
        CpuKernelPercentage = 1.2,
        CpuUserPercentage = 4.2,
        GpuUsagePercentage = 7.8,
        GpuAdapterName = "RTX 4060",
        RamUsedMb = 12288,
        RamTotalMb = 32768,
        DiskFreeGb = 428,
        DiskTotalGb = 1024,
        DiskReadMBs = 4.2,
        DiskWriteMBs = 1.8,
        DiskTotalMBs = 6.0,
        NetworkDownKBs = 1420,
        NetworkUpKBs = 310,
        Battery = new BatteryStatus
        {
            HasBattery = true,
            IsCharging = true,
            IsOnAcPower = true,
            BatteryLifePercent = 100,
            BatteryLifeSecondsRemaining = -1
        },
        UserIdleSeconds = 12,
        Timestamp = DateTimeOffset.UtcNow
    };

    public BatteryStatus GetBatteryStatus() => new()
    {
        HasBattery = true,
        IsCharging = true,
        IsOnAcPower = true,
        BatteryLifePercent = 100,
        BatteryLifeSecondsRemaining = -1
    };

    public AudioDeviceProfile GetAudioDevices() => new()
    {
        OutputId = "speakers-realtek",
        InputId = "mic-realtek",
        OutputName = "Altavoces (Realtek High Definition Audio)",
        InputName = "Micrófono de matriz (Realtek(R) Audio)",
        OutputVolumePercent = (int)Math.Round(Volume * 100),
        OutputMuted = OutputMuted,
        InputMuted = InputMuted
    };

    public IReadOnlyList<AudioEndpointInfo> GetOutputAudioDevices() =>
    [
        new("speakers-realtek", "Altavoces (Realtek High Definition Audio)", true),
        new("headphones-bt", "Auriculares Bluetooth WH-1000XM4", false),
        new("hdmi-monitor", "Audio Digital HDMI (NVIDIA High Definition)", false)
    ];

    public IReadOnlyList<AudioEndpointInfo> GetInputAudioDevices() =>
    [
        new("mic-realtek", "Micrófono de matriz (Realtek(R) Audio)", true),
        new("mic-usb", "Micrófono USB Studio Condenser", false)
    ];

    public bool SetDefaultAudioDevice(string deviceId, bool input) => true;
    public bool ToggleOutputMute() { OutputMuted = !OutputMuted; return true; }
    public bool ToggleInputMute() { InputMuted = !InputMuted; return true; }
    public bool SetInputMute(bool mute) { InputMuted = mute; return true; }
    public Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetDisplayPowerAsync(bool turnOn, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetMasterVolumeAsync(float volume, CancellationToken ct = default) { Volume = volume; return Task.CompletedTask; }
    public Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken ct = default) => Task.CompletedTask;
    public Task<float> GetMasterVolumeAsync(CancellationToken ct = default) => Task.FromResult(Volume);
    public Task SetMuteAsync(bool mute, CancellationToken ct = default) { OutputMuted = mute; return Task.CompletedTask; }
    public Task<bool> GetMuteAsync(CancellationToken ct = default) => Task.FromResult(OutputMuted);
    public void SimulateKeepAlivePulse(KeepAliveMode mode) { }
    public Task RunKeepAliveLoopAsync(KeepAliveMode mode, int min, int max, Action<string>? log, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);
    public float GetMasterPeakValue() => 0.15f;
    public Task<byte[]> CaptureScreenAsync(bool stamp = false, string? label = null, CancellationToken ct = default) => Task.FromResult(CaptureBytes);
    public void SendMediaControl(bool pauseOnly = true) { }
    public bool IsAppRunning(string executablePath) => false;
    public Task LaunchAppAsync(LaunchAppOptions options, CancellationToken ct = default) => Task.CompletedTask;
    public void CloseForegroundApplication() { }
    public IReadOnlyList<ApplicationWindow> GetApplicationWindows() => [];
    public Task CloseApplicationsAsync(IReadOnlyList<ApplicationCloseTarget> targets, bool fg, CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class DemoAiService : IAiQuotaService
{
    public AiQuotaSnapshot InspectLocalQuotas() => new(true,
    [
        new("codex", "Codex Desktop / VS Code", "IconReticle", true, "C:/Codex.exe", null,
        [
            new("Codex (GPT-4o / Claude 3.5)", "Ventana 5h", 84.5, "84.5% restante · 15.2k tokens"),
            new("Codex (Semanal)", "Cuota semanal", 92.0, "92.0% restante")
        ], 84.5, "Sesión activa en disco", false, HasExplicitQuotaMetrics: true),

        new("antigravity", "Google Antigravity IDE", "IconReticle", true, "C:/Antigravity.exe", null,
        [
            new("Gemini 3.8 Pro (Reasoning)", "Ventana 24h", 95.2, "95.2% restante · 42.8k tokens")
        ], 95.2, "En ejecución", false, HasExplicitQuotaMetrics: true),

        new("claude", "Claude Desktop", "IconTerminal", true, "C:/Claude.exe", null, [], 0, "Instalado localmente", false),
        new("lmstudio", "LM Studio (Local LLM)", "IconTerminal", true, "C:/LMStudio.exe", null, [], 0, "Servidor local listo", false),
        new("ollama", "Ollama CLI", "IconTerminal", true, "C:/ollama.exe", null, [], 0, "Modelos descargados", false)
    ], null);

    public bool LaunchEnvironment(string id) => true;
}

internal sealed class DemoAppCatalog : IInstalledAppsService
{
    public Task<IReadOnlyList<InstalledApplication>> ScanAsync(CancellationToken ct = default)
    {
        byte[] iconPixels = new byte[4096];
        for (int i = 0; i < iconPixels.Length; i += 4)
        {
            iconPixels[i] = 0;      // B
            iconPixels[i + 1] = 210;// G
            iconPixels[i + 2] = 255;// R
            iconPixels[i + 3] = 255;// A
        }

        return Task.FromResult<IReadOnlyList<InstalledApplication>>([
            new("Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe", IconPixels: iconPixels),
            new("Blender 4.2 LTS", @"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe", IconPixels: iconPixels),
            new("Spotify", @"C:\Users\jorge\AppData\Roaming\Spotify\Spotify.exe", IconPixels: iconPixels),
            new("Discord", @"C:\Users\jorge\AppData\Local\Discord\app.exe", IconPixels: iconPixels),
            new("Steam", @"C:\Program Files (x86)\Steam\steam.exe", IconPixels: iconPixels),
            new("Terminal de Windows", @"wt.exe", IconPixels: iconPixels)
        ]);
    }
}

internal static class ScreenshotGenerator
{
    public static void Run()
    {
        Console.WriteLine("[ScreenshotGenerator] Initializing high-fidelity screenshots for GitHub README (Light Mode)...");
        string outputDir = @"c:\Users\jorge\Proyectos\Nokto\assets\screenshots";
        Directory.CreateDirectory(outputDir);

        string tempDir = Path.Combine(Path.GetTempPath(), "nokto_screenshots_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var storage = new StorageResolver(tempDir);
        var persistence = new PersistenceService(storage);

        // Configure realistic settings
        var config = new AppConfig();
        config.Settings.Language = "es";
        config.Settings.Theme = "Light";
        config.Settings.ShowNetworkCardInHome = true;
        config.Settings.ShowHardwareCardInHome = true;
        config.Settings.ShowEnergyStatusCardInHome = true;
        config.Settings.ShowAiRadarCardInHome = true;
        config.Settings.ShowQuickActionsInHome = true;
        config.Settings.ShowAudioControlCardInHome = true;
        config.Settings.EvidenceScreenshotsEnabled = true;
        config.Settings.PanicHotkey = "Pause";
        config.Settings.BatteryProtectionEnabled = true;
        config.Settings.BatteryThresholdPercent = 10;
        config.Settings.BatteryAction = "Hibernate";
        config.Settings.DiskAlertThresholdGb = 50;
        config.Settings.MicMuteHotkey = "Ctrl+Shift+M";
        config.Settings.AudioMuteHotkey = "Ctrl+Shift+O";
        config.Settings.CheckUpdatesOnStartup = true;
        config.Settings.UpdateRepositoryOwner = "jmorgadodev";
        config.Settings.UpdateRepositoryName = "Nokto";
        config.Settings.AiEnvironmentOrder = ["codex", "antigravity", "claude", "lmstudio", "ollama"];
        config.LanServer.Enabled = true;
        config.LanServer.Port = 4884;
        persistence.SaveConfig(config);

        // Load rich default presets
        var presetsFile = Presets.GetDefaultPresets();
        persistence.SavePresets(new PresetsFile
        {
            Presets = presetsFile.Select(p => p with { IsSystemPreset = true }).ToList()
        });

        using var adapter = new DemoSystemAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        var aiService = new DemoAiService();
        var appCatalog = new DemoAppCatalog();

        // Switch application to Light theme
        if (Application.Current is App app)
        {
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            app.SetTheme("Light", TimeSpan.Zero, TimeSpan.Zero);
        }
        else if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        }

        using var viewModel = new MainViewModel(adapter, engine, persistence, aiService, appCatalog);
        viewModel.OverrideExecutableLocation = @"C:\Program Files\Nokto\Nokto.exe";
        viewModel.SelectedThemeModeIndex = 2; // Modo Día (Light)
        viewModel.PanicHotkeyText = "Pause";
        viewModel.PanicHotkeyStatus = "Atajo [Pause] registrado correctamente como guardia de pánico del sistema.";
        viewModel.MicMuteHotkeyText = "Ctrl+Shift+M";
        viewModel.MicHotkeyStatus = "Atajo [Ctrl+Shift+M] activo en segundo plano.";
        viewModel.AudioMuteHotkeyText = "Ctrl+Shift+O";
        viewModel.AudioHotkeyStatus = "Atajo [Ctrl+Shift+O] activo en segundo plano.";

        // Populate realistic evidence gallery items
        viewModel.EvidenceGalleryItems.Clear();
        viewModel.EvidenceGalleryItems.Add(new EvidenceItem
        {
            FileName = "nokto_evidence_2026-10-05_150000.png",
            TimestampText = "05/10/2026 15:00:00",
            FileSizeText = "482 KB",
            FilePath = @"C:\ProgramData\Nokto\evidence\nokto_evidence_2026-10-05_150000.png"
        });
        viewModel.EvidenceGalleryItems.Add(new EvidenceItem
        {
            FileName = "nokto_evidence_2026-10-05_123000.png",
            TimestampText = "05/10/2026 12:30:00",
            FileSizeText = "515 KB",
            FilePath = @"C:\ProgramData\Nokto\evidence\nokto_evidence_2026-10-05_123000.png"
        });

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1180,
            Height = 820,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(300);
        Dispatcher.UIThread.RunJobs();

        // Ensure AI environments are loaded
        int retry = 0;
        while (viewModel.DetectedAiEnvironments.Count < 2 && retry++ < 20)
        {
            Thread.Sleep(50);
            Dispatcher.UIThread.RunJobs();
        }

        // Enable Remote Control to generate live QR code
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int freePort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        viewModel.RemoteControlPort = freePort;
        viewModel.RemoteControlEnabled = true;
        int qrRetry = 0;
        while ((!viewModel.RemoteServerRunning || viewModel.RemoteQrCode is null) && qrRetry++ < 40)
        {
            Thread.Sleep(50);
            Dispatcher.UIThread.RunJobs();
        }

        // Start 1 active routine to showcase the live concurrent task capsule and telemetry
        var activePreset = persistence.LoadPresets().Presets.FirstOrDefault(p => p.Id.Contains("workday") || p.Id.Contains("keepalive"))
            ?? persistence.LoadPresets().Presets.First();
        var runningTask = engine.StartRoutine(activePreset);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(200);
        Dispatcher.UIThread.RunJobs();

        // -------------------------------------------------------------
        // CAPTURA 1: CABINA DE OPERACIONES Y TELEMETRÍA (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 01_cockpit_telemetria.png (Light Mode)...");
        viewModel.SelectedTabIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        RenderWindow(window, Path.Combine(outputDir, "01_cockpit_telemetria.png"));

        // -------------------------------------------------------------
        // CAPTURA 2: CONTROL MANUAL Y TAREAS CONCURRENTES (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 02_control_manual.png (Light Mode)...");
        viewModel.SelectedTabIndex = 1;
        viewModel.ManualGracePeriodEnabled = true;
        viewModel.ManualGraceSeconds = 60;
        viewModel.ManualAudioFadeEnabled = true;
        viewModel.ManualScreenshotEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        RenderWindow(window, Path.Combine(outputDir, "02_control_manual.png"));

        // -------------------------------------------------------------
        // CAPTURA 3: CONFIGURADOR DE RUTINAS / PIPELINES (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 03_rutinas_pipeline.png (Light Mode)...");
        viewModel.SelectedTabIndex = 2;
        var nightRoutine = viewModel.RoutineCards.FirstOrDefault(r => r.Name.Contains("Noches") || r.Name.Contains("Desconexión"))
            ?? viewModel.RoutineCards.FirstOrDefault();
        if (nightRoutine != null)
        {
            viewModel.SelectedRoutineItem = nightRoutine;
        }
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        RenderWindow(window, Path.Combine(outputDir, "03_rutinas_pipeline.png"));

        // -------------------------------------------------------------
        // CAPTURA 4: AJUSTES: ACTUALIZACIONES, APARIENCIA DÍA, ARRANQUE (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 04_ajustes_actualizaciones_apariencia.png (Light Mode - Top)...");
        viewModel.SelectedTabIndex = 3;
        viewModel.IsEvidenceGalleryOpen = false;
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(200);
        Dispatcher.UIThread.RunJobs();

        var scrollViewer = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer != null)
        {
            scrollViewer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(250);
            Dispatcher.UIThread.RunJobs();
        }
        RenderWindow(window, Path.Combine(outputDir, "04_ajustes_actualizaciones_apariencia.png"));

        // -------------------------------------------------------------
        // CAPTURA 5: AJUSTES: PÁNICO, GUARDIÁN DE BATERÍA, MODULARIDAD Y DISCO (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 05_ajustes_guardian_modularidad.png (Light Mode - Mid 1)...");
        if (scrollViewer != null)
        {
            var panicTb = scrollViewer.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(tb => tb.Text != null && (tb.Text.Contains("PÁNICO") || tb.Text.Contains("PANIC")));
            if (panicTb != null)
            {
                var border = panicTb.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Padding.Top >= 10);
                ScrollToVisual(scrollViewer, border ?? (Visual)panicTb, 12);
            }
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(250);
            Dispatcher.UIThread.RunJobs();
        }
        RenderWindow(window, Path.Combine(outputDir, "05_ajustes_guardian_modularidad.png"));

        // -------------------------------------------------------------
        // CAPTURA 6: AJUSTES: CONTROL DE AUDIO WASAPI Y HERRAMIENTAS IA (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 06_ajustes_audio_herramientas_ia.png (Light Mode - Mid 2)...");
        if (scrollViewer != null)
        {
            var audioView = scrollViewer.GetVisualDescendants().OfType<AudioSettingsView>().FirstOrDefault();
            if (audioView != null)
            {
                ScrollToVisual(scrollViewer, audioView, 12);
            }
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(250);
            Dispatcher.UIThread.RunJobs();
        }
        RenderWindow(window, Path.Combine(outputDir, "06_ajustes_audio_herramientas_ia.png"));

        // -------------------------------------------------------------
        // CAPTURA 7: CONTROL REMOTO MÓVIL LAN, GALERÍA DE EVIDENCIAS Y ACERCA DE (MODO DÍA)
        // -------------------------------------------------------------
        Console.WriteLine("[ScreenshotGenerator] Capturing 07_control_remoto_evidencias.png (Light Mode - Bottom)...");
        viewModel.IsEvidenceGalleryOpen = true;
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(200);
        Dispatcher.UIThread.RunJobs();

        if (scrollViewer != null)
        {
            var lanTb = scrollViewer.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(tb => tb.Text != null && tb.Text.Contains("CONTROL REMOTO MÓVIL"));
            if (lanTb != null)
            {
                var border = lanTb.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Padding.Top >= 10);
                ScrollToVisual(scrollViewer, border ?? (Visual)lanTb, -20);
            }
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(250);
            Dispatcher.UIThread.RunJobs();
        }
        RenderWindow(window, Path.Combine(outputDir, "07_control_remoto_evidencias.png"));

        // Clean up
        window.Close();
        Console.WriteLine($"[ScreenshotGenerator] All screenshots successfully generated in {outputDir}!");
    }

    private static void ScrollToVisual(ScrollViewer scrollViewer, Visual visual, double topOffset = 10)
    {
        var transform = visual.TransformToVisual(scrollViewer);
        if (transform.HasValue)
        {
            var point = transform.Value.Transform(new Point(0, 0));
            scrollViewer.Offset = new Vector(0, Math.Max(0, scrollViewer.Offset.Y + point.Y - topOffset));
        }
    }

    private static void RenderWindow(Window window, string filePath)
    {
        int width = (int)window.Width;
        int height = (int)window.Height;
        if (width <= 0) width = 1180;
        if (height <= 0) height = 820;

        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();

        using var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        rtb.Render(window);
        rtb.Save(filePath);
    }
}
