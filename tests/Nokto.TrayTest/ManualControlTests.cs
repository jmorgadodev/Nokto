using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.ConsoleTest;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

internal static class ManualControlTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nokto-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var persistence = new PersistenceService(new StorageResolver(directory));
        var config = new AppConfig();
        config.Settings.EvidenceScreenshotsEnabled = true;
        config.Settings.PanicHotkey = "Ctrl+Alt+Shift+F18";
        config.Settings.MicMuteHotkey = "Ctrl+Alt+Shift+F7";
        config.Settings.AudioMuteHotkey = "Ctrl+Alt+Shift+F8";
        persistence.SaveConfig(config);
        using var adapter = new WorkflowTestAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        using var model = new MainViewModel(adapter, engine, persistence, new NoQuota(), new Catalog());
        var view = new ManualControlView { DataContext = model };
        var window = new Window { Content = view, Width = 1000, Height = 680 };
        try
        {
            window.Show(); Pump();
            var energy = view.FindControl<ComboBox>("ManualEnergyPicker")!;
            Require(energy.TranslatePoint(new Point(0, energy.Bounds.Height), view) is { Y: < 500 } &&
                !view.FindControl<Expander>("ManualApplicationsExpander")!.IsExpanded, "Energía está accesible sin desplegar opciones de aplicaciones.");
            Require(model.CountdownMinutes == 30 && model.ManualGraceSeconds == 60 && model.ManualGracePeriodEnabled &&
                !model.ManualAudioFadeEnabled && !model.CanStartManualTask, "Valores iniciales: 30 min, aviso 60 s y ninguna acción implícita.");
            var check = view.FindControl<Button>("VerifyManualTaskButton")!;
            Click(check); Pump();
            Require(model.CurrentStatusText.Contains("Selecciona al menos una acción"), "Comprobar valida Control Manual, no Rutinas. Estado: " + model.CurrentStatusText + " Comando: " + check.Command?.GetType().Name);
            model.ManualMuteOutput = true; model.CountdownMinutes = 0;
            Require(!model.CanStartManualTask && model.ManualSummaryText.Contains("mayor que cero"), "Cuenta atrás cero rechazada.");
            model.SelectedTriggerTypeIndex = 6;
            Require(model.CanStartManualTask && model.ManualStartButtonText.Contains("Ejecutar ahora") && model.ManualSummaryText.Contains("Ahora:"), "Ahora explícito y resumen reactivo.");
            foreach (int index in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                model.CountdownMinutes = 30; model.SelectedProcessName = "probe.exe"; model.SelectedTriggerTypeIndex = index;
                Require(model.TryBuildManualTask(out var plan, out _, out _), "Condición accesible: " + index);
                var expected = index switch { 1 => TriggerType.Countdown, 2 => TriggerType.UserIdle, 3 => TriggerType.ProcessExit, 4 => TriggerType.AudioSilence, 5 => TriggerType.BatteryState, 6 => TriggerType.Manual, _ => TriggerType.Countdown };
                Require(plan!.Trigger.Type == expected, "Disparador real coincide con selección.");
            }
            model.BatteryTriggerOnAcDisconnect = false; model.BatteryTriggerOnThreshold = false; model.SelectedTriggerTypeIndex = 5;
            Require(!model.CanStartManualTask, "Batería sin condición rechazada.");
            model.BatteryTriggerOnAcDisconnect = true;
            model.SelectedTriggerTypeIndex = 1;
            model.ExactTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromMinutes(-1));
            if (model.ExactTime < TimeSpan.Zero) model.ExactTime = TimeSpan.FromHours(23);
            Require(model.ExactTimeSummaryText.Contains("Mañana"), "Hora pasada se explica como mañana.");
            model.SelectedTriggerTypeIndex = 0;
            Wait(() => model.ManualFilteredApps.Count > 0);
            view.FindControl<Expander>("ManualApplicationsExpander")!.IsExpanded = true; Pump();
            model.ManualAppSearch = "Probe";
            Require(model.ManualFilteredApps.Count == 1, "Buscar aplicaciones comparte catálogo local.");
            view.FindControl<ComboBox>("ManualInstalledAppPicker")!.SelectedItem = model.ManualFilteredApps[0]; Pump();
            Click(view.FindControl<Button>("AddManualAppButton")!); Pump();
            Require(model.ManualLaunchApplications.Count == 1 && model.ManualLaunchApplications[0].Options.Arguments == "--from-shortcut", "Click real conserva argumentos del acceso directo.");
            string originalName = model.StudioPresetName;
            model.ManualExecutablePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Require(model.ManualAppArguments == "" && model.ManualAppWorkingDirectory is null, "Cambiar ruta elimina argumentos ajenos.");
            Require(model.StudioPresetName == originalName, "Selección manual no modifica Rutinas.");
            var target = new ApplicationWindow(123, 456, 789, "C:\\probe.exe", "Documento con título largo", "Probe");
            adapter.ApplicationWindows.Add(target);
            model.RefreshManualWindowsCommand.Execute(null); model.SelectedManualWindow = target;
            model.AddManualCloseTargetCommand.Execute(null);
            model.ManualCloseAllWindows = true; model.AddManualCloseTargetCommand.Execute(null);
            model.ManualScreenshotEnabled = true; model.ManualAudioFadeEnabled = true; model.ManualMuteMicrophone = true;
            model.ManualPauseMedia = true; model.ManualTurnOffMonitors = true; model.ManualLockSession = true; model.ManualPowerActionIndex = 1;
            Require(model.TryBuildManualTask(out var snapshot, out var summary, out _) && snapshot!.Description == summary, "Un único builder alimenta resumen y ejecución.");
            var order = snapshot!.Pipeline.Select(s => s.ActionType).ToArray();
            Require(order.SequenceEqual(new[] { ActionType.CaptureScreenshot, ActionType.CloseSelectedApplications, ActionType.LaunchApp,
                ActionType.AudioFadeOut, ActionType.MuteAudio, ActionType.MuteMicrophone, ActionType.MediaControl, ActionType.TurnOffMonitors, ActionType.LockWorkstation }), "Orden fijo: captura, cierre, apertura, audio, pantalla y bloqueo.");
            var targets = snapshot.Pipeline[1].Parameters!["targets"].Deserialize(Nokto.Core.Serialization.NoktoJsonContext.Default.ApplicationCloseTargetArray);
            Require(targets!.Length == 2 && targets[0].Window == target && targets[1].AllApplicationWindows, "Identidad estable y alcance serializados.");
            model.ManualForceClose = true; model.ManualPowerActionIndex = 2;
            Require(!model.IsManualForcedCloseAvailable && model.TryBuildManualTask(out var sleep, out _, out _) && !sleep!.TerminalAction.Parameters!["forced"].GetBoolean(), "Forzado nunca se arrastra a suspensión.");
            VerifyLayout(view, model);
            var remove = view.GetVisualDescendants().OfType<Button>().First(button => button.DataContext is ManualLaunchSelection && button.Content?.ToString() == "Quitar");
            Click(remove); Pump();
            Require(model.ManualLaunchApplications.Count == 0, "Quitar aplicación ejecuta el comando desde la plantilla.");
            VerifyCloseFailureStopsPower(engine, adapter, snapshot);
            VerifyGraceCancellation(engine, adapter, snapshot);
            Console.WriteLine("[PASS] Control Manual: 7 condiciones, selectores reales, resumen, orden, identidad, cierre rechazado/UIPI, aviso cancelable y layout compacto.");
        }
        finally { window.Close(); }
    }

    private static void VerifyCloseFailureStopsPower(WorkflowEngine engine, WorkflowTestAdapter adapter, PresetDefinition plan)
    {
        foreach (Exception failure in new Exception[] { new InvalidOperationException("Pendiente de guardar tras 15 segundos"), new Win32Exception(5) })
        {
            adapter.Calls.Clear(); adapter.PowerActions = 0; adapter.CloseFailure = failure;
            var test = plan with { Id = Guid.NewGuid().ToString("N"), Trigger = new() { Type = TriggerType.Manual },
                Pipeline = plan.Pipeline.Skip(1).ToList(), TerminalAction = new() { Type = TerminalActionType.Shutdown, Parameters = new() { ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(0) } } };
            var task = engine.StartRoutine(test); Wait(() => task.IsCompleted);
            Require(task.IsFaulted && adapter.PowerActions == 0 && !adapter.Calls.Any(c => c.StartsWith("Launch:")), "Un cierre rechazado impide abrir aplicaciones y apagar.");
        }
        adapter.CloseFailure = null;
        adapter.Calls.Clear(); adapter.PowerActions = 0;
        var accepted = plan with { Id = Guid.NewGuid().ToString("N"), Trigger = new() { Type = TriggerType.Manual },
            Pipeline = plan.Pipeline.Skip(1).ToList(), TerminalAction = new() { Type = TerminalActionType.Shutdown, Parameters = new() { ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(0) } } };
        var success = engine.StartRoutine(accepted); Wait(() => success.IsCompleted);
        Require(success.IsCompletedSuccessfully && adapter.PowerActions == 2 && adapter.Calls.First() == "CloseSelected" && adapter.Calls.Any(c => c.StartsWith("Launch:")), "Cierre aceptado continúa con apertura y energía; bloqueo y apagado se simulan.");
    }

    private static void VerifyGraceCancellation(WorkflowEngine engine, WorkflowTestAdapter adapter, PresetDefinition plan)
    {
        adapter.Calls.Clear(); adapter.PowerActions = 0;
        var waiting = plan with { Id = Guid.NewGuid().ToString("N"), Trigger = new() { Type = TriggerType.Manual } };
        var task = engine.StartRoutine(waiting); Wait(() => engine.GetActiveWorkflows().Any(w => w.GracePeriodActive));
        Require(adapter.Calls.IsEmpty, "El aviso sucede antes de cualquier acción.");
        engine.StopRoutine(waiting.Id); Wait(() => task.IsCompleted);
        Require(adapter.Calls.IsEmpty && adapter.PowerActions == 0, "Cancelar aviso evita todas las acciones.");
    }

    private static void VerifyLayout(ManualControlView view, MainViewModel model)
    {
        foreach (int theme in new[] { 2, 3, 4 })
        foreach (var size in new[] { new Size(1000, 680), new Size(1920, 1080) })
        {
            model.SelectedThemeModeIndex = theme;
            view.Measure(size); view.Arrange(new Rect(size)); Pump();
            var summary = view.FindControl<TextBlock>("ManualTaskSummary")!;
            Require(summary.Bounds.Width > 200 && summary.Bounds.Width < size.Width && summary.Bounds.Height > 0, "Resumen envuelve dentro del panel.");
            foreach (var box in view.GetVisualDescendants().OfType<ComboBox>().Where(box => box.IsVisible && box.Bounds.Width > 0))
                Require(box.Bounds.Width <= size.Width - 40, "Selector no excede el ancho de la ventana.");
            var countdown = view.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.IsVisible && n.DataContext == model && !n.ShowButtonSpinner).ToArray();
            Require(countdown.Length == 3 && countdown.All(n => n.GetVisualDescendants().OfType<TextBox>().Any(t => t.Bounds.Width >= 35 && !string.IsNullOrEmpty(t.Text))),
                "Horas, minutos y segundos tienen espacio para mostrar sus cifras completas.");
        }
        model.SelectedThemeModeIndex = 3;
        view.Measure(new Size(1000, 680)); view.Arrange(new Rect(0, 0, 1000, 680)); Pump();
        using var bitmap = new RenderTargetBitmap(new PixelSize(1000, 680), new Vector(96, 96));
        bitmap.Render(view);
        var root = FindRoot(); Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        bitmap.Save(Path.Combine(root, "artifacts", "manual-control-preview.png"));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Nokto.sln"))) directory = directory.Parent ?? throw new InvalidOperationException("No se encontró el repositorio.");
        return directory.FullName;
    }
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Wait(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do { Pump(); Thread.Sleep(5); } while (!done() && DateTime.UtcNow < deadline);
        Require(done(), "Timeout Control Manual.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Catalog : IInstalledAppsService
    {
        public Task<IReadOnlyList<InstalledApplication>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledApplication>>([new("Probe", Path.Combine(Environment.SystemDirectory, "notepad.exe"), "--from-shortcut")]);
    }
    private sealed class NoQuota : IAiQuotaService
    {
        public AiQuotaSnapshot InspectLocalQuotas() => new(false, [], null);
        public bool LaunchEnvironment(string environmentId) => false;
    }
}
