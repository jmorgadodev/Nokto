using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Nokto.UI;
using Nokto.UI.Tray;

internal static class Program
{
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--media-refresh-probe")) { MediaRefreshConcurrencyProbe.RunAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--codex-quota-smoke")) { IslandCodexProbe.Smoke(); return 0; }
            if (args.Contains("--audio-interop-probe"))
            {
                AudioInteropProbe.Run();
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--recognition-reference-probe")
            {
                RecognitionAudioProbe.ReferenceAsync(args[1], args.Contains("--online")).GetAwaiter().GetResult();
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--manual-window-probe")
            {
                ManualWindowProbeApp.Token = args[1];
                ManualWindowProbeApp.RefuseClose = args.Contains("refuse");
                return AppBuilder.Configure<ManualWindowProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
            }
            AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
            if (args.Contains("--island-interaction-safety-probe")) { IslandInteractionSafetyProbe.Run(); return 0; }
            if (args.Contains("--island-codex-probe")) { IslandCodexProbe.Run(); return 0; }
            if (args.Contains("--island-notices-probe"))
            {
                RecognitionMotionProbe.Run(); IslandNoticesProbe.Run();
                return 0;
            }
            if (args.Contains("--island-composition-probe"))
            {
                IslandCompositionProbe.Run();
                return 0;
            }
            if (args.Contains("--island-harmony-probe"))
            {
                IslandHarmonyProbe.Run();
                return 0;
            }
            if (args.Contains("--island-audio-presentation-probe"))
            {
                IslandAudioPresentationProbe.Run();
                return 0;
            }
            if (args.Contains("--island-music-probe"))
            {
                RecognitionAudioProbe.Run();
                IslandMusicProbe.Run();
                return 0;
            }
            if (args.Contains("--eye-preview"))
            {
                EyePreviewProbe.Run();
                return 0;
            }
            if (args.Contains("--island-layout-probe"))
            {
                IslandLayoutProbe.Run();
                return 0;
            }
            if (args.Contains("--generate-screenshots"))
            {
                ScreenshotGenerator.Run();
                return 0;
            }
            ManualControlTests.Run();
            ManualWindowTests.Run();
            if (args.Contains("--manual-control-test")) return 0;
            RemoteShutdownTests.Run();
            if (args.Contains("--remote-shutdown-test")) return 0;
            WindowLifecycleTests.Run();
            if (args.Contains("--window-lifecycle-test")) return 0;
            RoutineUiTests.Run();
            using var tray = new TrayIcon { IsVisible = true };
            tray.Icon = DynamicTrayIconRenderer.RenderTrayIcon(TrayIconVisualState.Idle);
            var initial = ReadResources();
            for (int i = 0; i < 750; i++)
                tray.Icon = DynamicTrayIconRenderer.RenderTrayIcon(TrayIconVisualState.Idle);
            AssertBounded(initial, "750 refrescos en reposo");

            // Warm every visual frame before measuring prolonged, varying telemetry.
            foreach (int remaining in new[] { 120, 121 })
                for (int progress = 0; progress <= 100; progress += 5)
                    for (int phase = 0; phase < 4; phase++)
                        tray.Icon = DynamicTrayIconRenderer.RenderTrayIcon(
                            TrayIconVisualState.InProgress, progress, remaining, phase / 3f);
            tray.Icon = DynamicTrayIconRenderer.RenderTrayIcon(TrayIconVisualState.Completed);
            var warmed = ReadResources();
            for (int i = 0; i < 10_000; i++)
            {
                tray.Icon = DynamicTrayIconRenderer.RenderTrayIcon(
                    TrayIconVisualState.InProgress, i % 1001 / 10d, i % 240, i % 101 / 100f);
                tray.ToolTipText = $"Nokto: actualización {i}";
            }
            AssertBounded(warmed, "10.000 refrescos con progreso, pulso y aviso final");
            Console.WriteLine("[PASS] Bandeja nativa: recursos estables durante actualizaciones repetidas.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] UI/Bandeja: {ex}");
            return 1;
        }
    }

    private static (uint Gdi, uint User) ReadResources()
    {
        using var process = Process.GetCurrentProcess();
        return (GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1));
    }

    private static void AssertBounded((uint Gdi, uint User) before, string scenario)
    {
        var after = ReadResources();
        long gdiGrowth = (long)after.Gdi - before.Gdi;
        long userGrowth = (long)after.User - before.User;
        Console.WriteLine($"{scenario}: GDI {before.Gdi}→{after.Gdi}, USER {before.User}→{after.User}.");
        if (gdiGrowth > 12 || userGrowth > 12)
            throw new InvalidOperationException($"Fuga de recursos nativos tras {scenario}: GDI +{gdiGrowth}, USER +{userGrowth}.");
    }
}
