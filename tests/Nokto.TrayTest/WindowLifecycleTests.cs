using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Nokto.ConsoleTest;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;
using Nokto.UI;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

internal static class WindowLifecycleTests
{
    public static void Run()
    {
        var app = (App)Avalonia.Application.Current!;
        var mainField = typeof(App).GetField("_mainWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousWindow = mainField.GetValue(app);
        var closed = new MainWindow();
        try
        {
            closed.Show();
            closed.ForceCloseApplication();
            mainField.SetValue(app, closed);
            app.ShowMainWindow();
            Require(!closed.IsVisible && closed.PlatformImpl is null,
                "Restaurar desde bandeja una ventana destruida debe ser inocuo.");
            mainField.SetValue(app, null);
            app.ShowMainWindow();

            string directory = Path.Combine(Path.GetTempPath(), "nokto_window_lifecycle_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var persistence = new PersistenceService(new StorageResolver(directory));
            var config = new AppConfig();
            config.Settings.MicMuteHotkey = "Ctrl+Alt+Shift+F7";
            config.Settings.AudioMuteHotkey = "Ctrl+Alt+Shift+F8";
            persistence.SaveConfig(config);
            using var adapter = new WorkflowTestAdapter();
            using var engine = new WorkflowEngine(adapter, persistence);
            using var model = new MainViewModel(adapter, engine, persistence, new EmptyAiService());
            var window = new MainWindow();
            try
            {
                window.InitializeWithViewModel(model);
                mainField.SetValue(app, window);
                window.Show();
                model.MinimizeToTrayOnClose = true;
                window.Close();
                Require(!window.IsVisible && window.PlatformImpl is not null,
                    "Minimizar al cerrar conserva una ventana nativa restaurable.");
                window.WindowState = WindowState.Normal;
                app.ShowMainWindow();
                Require(window.IsVisible && window.WindowState == WindowState.Normal,
                    "La restauración de bandeja respeta una ventana normal.");
                for (int i = 0; i < 5; i++)
                {
                    window.WindowState = WindowState.Minimized;
                    Require(!window.IsVisible, "Minimizar oculta la ventana hacia la bandeja.");
                    app.ShowMainWindow();
                    Require(window.IsVisible && window.WindowState != WindowState.Minimized,
                        "Restaurar desde la bandeja muestra una ventana que estaba minimizada.");
                }

                var overlay = new GraceOverlayWindow();
                overlay.Show();
                typeof(MainWindow).GetField("_graceOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, overlay);
                window.FindControl<CheckBox>("MinimizeOnCloseCheckBox")!.IsChecked = false;
                Require(!model.MinimizeToTrayOnClose && !persistence.LoadConfig().Settings.MinimizeToTrayOnClose,
                    "La opción de Ajustes persiste y actualiza la política de cierre real.");
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Require(window.PlatformImpl is null && overlay.PlatformImpl is null,
                    "Desactivar minimizar al cerrar destruye la ventana y cierra su overlay.");
                app.ShowMainWindow();
                Require(window.PlatformImpl is null, "La bandeja no revive la ventana tras salir.");
            }
            finally { window.ForceCloseApplication(); }
            var explicitExit = new MainWindow();
            try
            {
                explicitExit.InitializeWithViewModel(model);
                explicitExit.Show();
                model.MinimizeToTrayOnClose = true;
                var pending = engine.StartRoutine(new PresetDefinition { Id = "exit-ui", Name = "Pendiente", Trigger = new() { Type = TriggerType.Countdown, Parameters = new() { ["durationSeconds"] = System.Text.Json.JsonSerializer.SerializeToElement(60) } } });
                var button = explicitExit.FindControl<Button>("ExitNoktoButton")!;
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Dispatcher.UIThread.RunJobs();
                Require(explicitExit.PlatformImpl is null && !engine.IsRoutineRunning("exit-ui"), "Salir desde la interfaz cierra completamente y cancela las rutinas aunque minimizar al cerrar esté activo.");
                pending.Wait(TimeSpan.FromSeconds(2));
            }
            finally { explicitExit.ForceCloseApplication(); }
            Console.WriteLine("[PASS] Ciclo de ventana: restauración destruida/nula, cierre configurable y overlay liberado.");
        }
        finally
        {
            mainField.SetValue(app, previousWindow);
            closed.ForceCloseApplication();
        }
    }

    private sealed class EmptyAiService : IAiQuotaService
    {
        public AiQuotaSnapshot InspectLocalQuotas() => new(false, [], null);
        public bool LaunchEnvironment(string id) => false;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
