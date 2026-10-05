using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.ConsoleTest;
using Nokto.Core.Engine;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;
using Nokto.Core.Abstractions;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;
using Nokto.UI;
using Nokto.Platform.Windows.Hotkeys;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Media;

internal static class RoutineUiTests
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nokto_routine_ui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var persistence = new PersistenceService(new StorageResolver(directory));
        var config = new AppConfig();
        config.Settings.MicMuteHotkey = "Ctrl+Alt+Shift+F7";
        config.Settings.AudioMuteHotkey = "Ctrl+Alt+Shift+F8";
        persistence.SaveConfig(config);
        persistence.SavePresets(new PresetsFile { Presets = [Routine("a"), Routine("b")] });
        using var adapter = new WorkflowTestAdapter();
        using var engine = new WorkflowEngine(adapter, persistence);
        var aiService = new TestAiService();
        using var model = new MainViewModel(adapter, engine, persistence, aiService, new TestAppCatalog());
        var window = new MainWindow { DataContext = model };
        Dispatcher.UIThread.RunJobs();
        Wait(() => model.DetectedAiEnvironments.Count == 5);
        Require(window.WindowState == WindowState.Maximized && model.SelectedOutputAudioDevice?.Id == "real-speakers" &&
            model.MasterVolumePercent == 50 && adapter.DefaultAudioSwitches == 0,
            "La ventana inicia maximizada y el audio sigue el ID multimedia real aunque HDMI1 ocupe el índice cero.");
        VerifyOutputMuteIndicator(window, model, adapter);
        VerifyFooter(window);
        model.SelectedRoutineItem = model.RoutineCards.Single(card => card.Id == "a");
        var a = model.RunCurrentStudioPipelineCommand.ExecuteAsync(null);
        Pump();
        Require(model.IsSelectedRoutineRunning && !model.RunCurrentStudioPipelineCommand.CanExecute(null) &&
            model.FinishSelectedRoutineCommand.CanExecute(null), "Botones ligados a la primera rutina.");
        Require(model.RoutineCards.Single(card => card.Id == "a").IsRunning && model.IsSingleRoutineActive && model.TimeRemainingText.Contains("Restante:"),
            "Badge individual y cuenta atrás real.");
        VerifyFooter(window);
        model.SelectedRoutineItem = model.RoutineCards.Single(card => card.Id == "b");
        Require(model.RunCurrentStudioPipelineCommand.CanExecute(null) && !model.FinishSelectedRoutineCommand.CanExecute(null),
            "Seleccionar otra rutina debe permitir iniciar sin finalizar la primera.");
        var b = model.RunCurrentStudioPipelineCommand.ExecuteAsync(null);
        Pump();
        Require(model.ActiveRoutines.Count == 2 && model.FooterTaskText == "2 rutinas activas" &&
            model.FooterTaskToolTip.Contains("Rutina a") && model.FooterTaskToolTip.Contains("Rutina b"),
            "Dos chips independientes y tooltip con ambos nombres.");
        var activeLists = window.GetLogicalDescendants().OfType<ItemsControl>().Where(c => ReferenceEquals(c.ItemsSource, model.ActiveRoutines)).ToArray();
        var home = activeLists[0];
        Require(activeLists.Length == 2, "La lista compacta de Inicio y el panel de cápsulas manual deben compartir el estado concurrente.");
        Require(home.IsVisible, "Lista de rutinas activa visible en Inicio.");
        VerifyFooter(window);
        model.FinishSelectedRoutineCommand.Execute(null);
        Pump();
        Require(engine.IsRoutineRunning("a") && !engine.IsRoutineRunning("b") && model.CanStartSelectedRoutine,
            "Finalizar la selección sólo afecta a esa ID.");
        model.ActiveRoutines.Single().FinishCommand.Execute(null);
        Wait(() => a.IsCompleted && b.IsCompleted);
        Require(!model.HasActiveRoutines && !home.GetLogicalAncestors().OfType<ScrollViewer>().First().IsVisible && model.FooterTaskText == "Ninguna tarea en curso",
            "El último chip restaura Inicio y Footer en reposo.");

        model.CountdownMinutes = 30;
        model.ManualMuteOutput = true;
        model.ManualMuteMicrophone = true;
        var manualOne = model.StartManualTaskCommand.ExecuteAsync(null);
        Pump();
        var manualFirst = model.ActiveRoutines.Single(item => item.IsManual);
        Require(model.StartManualTaskCommand.CanExecute(null), "Una tarea manual en curso no bloquea activar otra desde el botón real.");
        Require(manualFirst.Info.Description.Contains("silenciar salida") && manualFirst.Info.Description.Contains("silenciar micrófono") &&
            !manualFirst.Info.Description.Contains("Apagar PC"), "El programador manual ofrece acciones independientes y conserva el equipo encendido por defecto.");
        var manualTwo = model.StartManualTaskCommand.ExecuteAsync(null);
        Pump();
        Require(model.ActiveRoutines.Count(item => item.IsManual) == 2 &&
            model.ActiveRoutines.Where(item => item.IsManual).Select(item => item.Info.RoutineId).Distinct().Count() == 2,
            "Cada tarea manual usa una ejecución identificable e independiente.");
        manualFirst.FinishCommand.Execute(null);
        Wait(() => manualOne.IsCompleted);
        Require(model.ActiveRoutines.Count(item => item.IsManual) == 1 && !manualTwo.IsCompleted,
            "Finalizar una tarea manual no cancela la siguiente.");
        model.ActiveRoutines.Single(item => item.IsManual).FinishCommand.Execute(null);
        Wait(() => manualTwo.IsCompleted);

        var fhd = new HardwareProfile { MonitorCount = 1, PrimaryScreenWidth = 1920, PrimaryScreenHeight = 1080, PrimaryScreenRefreshRateHz = 144 };
        Require(fhd.MonitorsText == "1 Pantalla: FHD (1920×1080 @ 144Hz)", "La ficha de hardware incluye nombre comercial y frecuencia de refresco.");

        var warning = Routine("warning") with
        {
            Trigger = new() { Type = TriggerType.Countdown, Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(0) } },
            TerminalAction = new() { Type = TerminalActionType.LockStation, Parameters = new() { ["gracePeriodSeconds"] = JsonSerializer.SerializeToElement(60) } }
        };
        var grace = engine.StartRoutine(warning);
        var other = engine.StartRoutine(Routine("other"));
        Wait(() => model.GraceRoutineName == warning.Name);
        model.FinishGraceRoutine();
        Wait(() => grace.IsCompleted);
        Require(engine.IsRoutineRunning("other") && adapter.PowerActions == 0, "El aviso previo cancela sólo su rutina y no ejecuta la acción terminal.");
        engine.FinishAll();
        Wait(() => other.IsCompleted);

        VerifyLinearEditor(window, model, persistence);
        model.AddStudioStep("PowerAction");
        window.Show(); Pump();
        var editor = window.GetVisualDescendants().OfType<RoutineEditorView>().Single();
        VerifyAppPickerClick(editor, model);
        editor.Measure(new Size(650, 800)); editor.Arrange(new Rect(0, 0, 650, 800)); Pump();
        var numeric = editor.GetVisualDescendants().OfType<NumericUpDown>().Single(input => input.Name == "ActionGracePeriodInput" && input.DataContext is StudioStepItem { IsPowerAction: true });
        numeric.Value = 300;
        numeric.ApplyTemplate();
        numeric.Measure(new Size(110, 40));
        numeric.Arrange(new Rect(0, 0, 110, 40));
        Pump();
        var input = numeric.GetVisualDescendants().OfType<TextBox>().First();
        Require(input.Text == "300 s" && input.Bounds.Width >= 40 && numeric.Bounds.Width >= 95,
            $"El aviso numérico debe mostrar 300 s completo: '{input.Text}', ancho {input.Bounds.Width}.");
        window.GetLogicalDescendants().OfType<TabControl>().Distinct().Single().SelectedIndex = 0;
        VerifyHomeDashboard(window, model, engine);
        VerifyAiTools(window, model, persistence, aiService);
        VerifyRemoteControls(window, model, persistence, adapter);
        VerifyPanicAndAppearance(window, model, persistence, engine);
        Console.WriteLine("[PASS] Avalonia: selección independiente, tareas manuales concurrentes, cápsulas, footer, resolución comercial y control 300 s legible.");
    }

    private static void VerifyAppPickerClick(RoutineEditorView editor, MainViewModel model)
    {
        var add = editor.FindControl<Button>("AddWorkflowActionButton")!;
        var flyout = (Flyout)add.Flyout!;
        flyout.ShowAt(add); Pump();
        var program = ((StackPanel)flyout.Content!).Children.OfType<Button>().Single(button => button.CommandParameter?.ToString() == "LaunchApp");
        int before = model.StudioPipelineSteps.Count;
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(program, null);
        Pump();
        Require(model.StudioPipelineSteps.Count == before + 1 && model.StudioPipelineSteps.Last().IsLaunchApp, "Un clic real en el menú debe añadir la tarjeta para elegir aplicaciones.");
        var step = model.StudioPipelineSteps.Last();
        editor.UpdateLayout(); Pump();
        var picker = editor.GetVisualDescendants().OfType<ComboBox>().Single(control => ReferenceEquals(control.DataContext, step) && ReferenceEquals(control.ItemsSource, step.FilteredInstalledApps));
        picker.SelectedItem = model.InstalledApps[0]; Pump();
        Require(step.LaunchExecutablePath.EndsWith("cmd.exe"), "El selector del programa configura la ruta ejecutable.");
        step.LaunchArguments = "argumentos del acceso directo";
        var pathInput = editor.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "LaunchExecutableInput" && ReferenceEquals(control.DataContext, step));
        pathInput.Text = Environment.ProcessPath; Pump();
        Require(step.LaunchExecutablePath == Environment.ProcessPath && step.SelectedInstalledApp is null && step.LaunchArguments == "",
            "Una ruta manual reemplaza la selección y elimina argumentos ajenos del acceso directo.");
        var direct = editor.FindControl<Button>("AddProgramActionButton")!;
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(direct, null); Pump();
        Require(model.StudioPipelineSteps.Count == before + 2 && model.StudioPipelineSteps.Last().IsLaunchApp, "El acceso directo también añade la tarjeta de aplicaciones.");
        Console.WriteLine("[PASS] Clic real: menú, botón directo y ComboBox configuran aplicaciones.");
    }

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostHotkey(uint thread, uint message, IntPtr key, IntPtr data);

    private static void VerifyPanicAndAppearance(MainWindow window, MainViewModel model, PersistenceService persistence, WorkflowEngine engine)
    {
        var input = window.FindControl<TextBox>("PanicHotkeyInput")!;
        input.Text = "Ctrl+Alt+Shift+F18"; Pump();
        model.ApplyPanicHotkeyCommand.Execute(null); Pump();
        Require(persistence.LoadConfig().Settings.PanicHotkey == "Ctrl+Alt+Shift+F18" && model.PanicHotkeyStatus.StartsWith("Activo:"), "El campo editable guarda y registra el atajo global real.");
        var service = (GlobalHotkeyService)typeof(MainViewModel).GetField("_panicHotkeyService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
        Require(service.IsRegistered, "El atajo de pánico debe estar registrado en Win32.");
        using (var occupied = new GlobalHotkeyService(() => { }))
        {
            Require(occupied.Start(7, 0x82), "El test reserva una combinación diferente.");
            model.PanicHotkeyText = "Ctrl+Alt+Shift+F19"; model.ApplyPanicHotkeyCommand.Execute(null);
            Require(service.IsRegistered && model.PanicHotkeyStatus.Contains("anterior sigue activo") && persistence.LoadConfig().Settings.PanicHotkey.EndsWith("F18"), "Un conflicto conserva el atajo anterior y su configuración.");
        }
        model.PanicHotkeyText = "Ctrl+Unknown"; model.ApplyPanicHotkeyCommand.Execute(null);
        Require(model.PanicHotkeyStatus.Contains("inválido") && service.IsRegistered, "Rechaza combinaciones inválidas sin perder el pánico activo.");
        model.PanicHotkeyText = persistence.LoadConfig().Settings.MicMuteHotkey; model.ApplyPanicHotkeyCommand.Execute(null);
        Require(model.PanicHotkeyStatus.Contains("distinto") && persistence.LoadConfig().Settings.PanicHotkey.EndsWith("F18"), "El pánico no puede compartir el atajo del micrófono.");
        string originalMic = persistence.LoadConfig().Settings.MicMuteHotkey;
        model.MicMuteHotkeyText = "Ctrl+Alt+Shift+F18"; model.ApplyAudioHotkeysCommand.Execute(null);
        Require(persistence.LoadConfig().Settings.MicMuteHotkey == originalMic && model.AudioControlStatusText.Contains("pánico"), "Los ajustes de audio respetan el atajo de pánico elegido.");
        typeof(MainViewModel).GetMethod("LoadSettingsFromConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        Require(model.PanicHotkeyText == "Ctrl+Alt+Shift+F18", "Recargar Ajustes recupera el atajo guardado.");
        var first = engine.StartRoutine(Routine("panic-a")); var second = engine.StartRoutine(Routine("panic-b"));
        Wait(() => model.ActiveRoutineCount == 2);
        uint thread = (uint)typeof(GlobalHotkeyService).GetField("_threadId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Require(PostHotkey(thread, 0x0312, new IntPtr(9001), IntPtr.Zero), "El hilo nativo acepta WM_HOTKEY.");
        Wait(() => first.IsCompleted && second.IsCompleted && model.ActiveRoutineCount == 0);
        service.Stop();
        typeof(MainViewModel).GetField("_registeredPanicChord", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, null);
        model.MicMuteHotkeyText = persistence.LoadConfig().Settings.PanicHotkey;
        model.ApplyAudioHotkeysCommand.Execute(null);
        Require(persistence.LoadConfig().Settings.MicMuteHotkey == originalMic && model.AudioControlStatusText.Contains("pánico"),
            "Audio tampoco puede apropiarse del pánico configurado si Windows no lo pudo activar al iniciar.");
        typeof(MainViewModel).GetMethod("LoadSettingsFromConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        var app = (App)Avalonia.Application.Current!;
        model.SelectedThemeModeIndex = 4; Pump();
        Require(persistence.LoadConfig().Settings.Theme == "Slate" && app.RequestedThemeVariant == App.SlateTheme &&
            app.TryGetResource("ThemeBackground", App.SlateTheme, out var brush) && brush is ISolidColorBrush { Color: var color } && color == Color.Parse("#303641"),
            "Gris intermedio aplica su paleta real y persiste.");
        model.SelectedThemeModeIndex = 2; Pump();
        Require(app.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light, "El tema Día sigue disponible.");
        model.SelectedThemeModeIndex = 3; Pump();
        Require(app.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark && model.ManualPowerActionOptions[0] == "Finalizar sin apagar ni suspender", "Tema Noche y texto manual claro.");
        Console.WriteLine("[PASS] Pánico: registro configurable, conflictos, persistencia y cancelación de todas las rutinas. Tema intermedio verificado.");
    }

    private static void VerifyLinearEditor(MainWindow window, MainViewModel model, PersistenceService persistence)
    {
        Wait(() => model.InstalledApps.Count == 2);
        model.NewStudioPipeline();
        Require(model.StudioTriggerTypeIndex == 7 && model.BuildPresetFromStudio().TerminalAction.Type == TerminalActionType.None,
            "Una rutina nueva es manual y no añade apagado implícito.");
        model.AddStudioStep("LaunchApp");
        var launch = model.StudioPipelineSteps.Single();
        launch.SelectedInstalledApp = model.InstalledApps[0];
        launch.AppSearchText = "editor";
        Require(launch.FilteredInstalledApps.Count == 1 && launch.FilteredInstalledApps[0].Name == "Editor de prueba" && launch.SelectedInstalledApp?.Icon?.PixelSize == new PixelSize(32, 32),
            "Búsqueda reactiva y bitmap nativo 32×32.");
        launch.AppSearchText = "inexistente";
        Require(launch.FilteredInstalledApps.Count == 0 && launch.LaunchExecutablePath.EndsWith("cmd.exe"), "Filtrar no borra la ruta elegida.");
        model.AddStudioStep("AudioConfig");
        var audio = model.StudioPipelineSteps.Last(); audio.AudioVolumePercent = 45; audio.MuteMicrophone = true;
        model.MoveStudioStepUp(audio);
        Require(audio.StepOrder == 1 && launch.StepOrder == 2, "Las flechas reordenan y renumeran la secuencia.");
        model.SaveCurrentStudioPipeline();
        var saved = persistence.LoadPresets().Presets.Single(p => p.Id == model.StudioPresetId);
        Require(saved.Actions?.Select(a => a.ActionType).SequenceEqual(new[] { ActionType.AudioConfig, ActionType.LaunchApp }) == true && saved.TerminalAction.Type == TerminalActionType.None &&
            saved.Actions[1].Parameters!["skipIfAlreadyRunning"].GetBoolean(), "El flujo se guarda en orden con omisión de programas y sin energía.");
        model.StudioTriggerTypeIndex = 2; model.StudioExactTime = new TimeSpan(9, 30, 0);
        foreach (var day in model.StudioWeekdays) day.IsSelected = day.Day is 1 or 3 or 5;
        var scheduled = model.BuildPresetFromStudio();
        Require(scheduled.Trigger.Type == TriggerType.ScheduledTime && scheduled.Trigger.Parameters!["timeOfDay"].GetString() == "09:30" &&
            scheduled.Trigger.Parameters["daysOfWeek"].GetArrayLength() == 3, "El horario guarda HH:mm y días, sin congelar una cuenta atrás.");
        foreach (var day in model.StudioWeekdays) day.IsSelected = false;
        model.SaveCurrentStudioPipeline();
        Require(model.IsRoutineCheckVisible && model.RoutineCheckStatusColor == "#FFB300", "Sin días seleccionados se muestra una validación y no se guarda un horario inválido.");
        model.StudioTriggerTypeIndex = 7; model.StudioPipelineSteps.Clear();
        window.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
        Pump();
        Console.WriteLine("[PASS] Editor lineal: acciones reordenables, búsqueda, iconos, persistencia, horario semanal y validación sin apagado obligatorio.");
    }

    private sealed class TestAppCatalog : IInstalledAppsService
    {
        public Task<IReadOnlyList<InstalledApplication>> ScanAsync(CancellationToken cancellationToken = default)
        {
            byte[] pixels = new byte[4096];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 180; pixels[i + 3] = 255; }
            return Task.FromResult<IReadOnlyList<InstalledApplication>>([new("Editor de prueba", Path.Combine(Environment.SystemDirectory, "cmd.exe"), IconPixels: pixels), new("Chat de prueba", Path.Combine(Environment.SystemDirectory, "notepad.exe"), IconPixels: pixels)]);
        }
    }

    private static void VerifyHomeDashboard(MainWindow window, MainViewModel model, WorkflowEngine engine)
    {
        var runs = Enumerable.Range(0, 10).Select(i => engine.StartRoutine(Routine("layout-" + i))).ToArray();
        Pump();
        Require(model.ActiveRoutineCount == 10, "La cabina debe aceptar diez tareas concurrentes.");
        var home = window.FindControl<Grid>("HomeDashboardGrid")!;
        foreach (var size in new[] { new Size(1000, 520), new Size(1920, 920) })
        {
            home.Measure(size); home.Arrange(new Rect(size));
            var cards = home.Children.Where(c => c.IsVisible && Grid.GetRow(c) < 2).ToArray();
            Require(cards.Length == 6 && cards.All(c => c.Bounds.Bottom <= size.Height && c.Bounds.Right <= size.Width),
                "Las seis tarjetas deben permanecer dentro del dashboard sin scroll global.");
            foreach (var card in cards)
                foreach (var other in cards.Where(c => Grid.GetRow(c) == Grid.GetRow(card) && Grid.GetColumn(c) > Grid.GetColumn(card)))
                    Require(card.Bounds.Right <= other.Bounds.Left, "Las tres columnas no deben superponerse.");
        }
        engine.FinishAll();
        Wait(() => runs.All(run => run.IsCompleted));
    }

    private static void VerifyOutputMuteIndicator(MainWindow window, MainViewModel model, WorkflowTestAdapter adapter)
    {
        var audio = window.GetLogicalDescendants().OfType<HomeAudioView>().Single();
        var button = audio.GetLogicalDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, model.ToggleOutputMuteCommand));
        Require(button.Content?.ToString() == "🔊", "La salida activa debe mostrar el altavoz sin silencio.");
        model.ToggleOutputMuteCommand.Execute(null); Pump();
        Require(button.Content?.ToString() == "🔇" && adapter.OutputMuted && button.Classes.Contains("muted") &&
            model.MasterVolumePercent == 50, "Silenciar cambia icono y estilo sin falsear el volumen maestro.");
        model.ToggleOutputMuteCommand.Execute(null); Pump();
        Require(button.Content?.ToString() == "🔊" && !adapter.OutputMuted && !button.Classes.Contains("muted"),
            "Reactivar la salida restaura inmediatamente el indicador.");
    }

    private static void VerifyAiTools(MainWindow window, MainViewModel model, PersistenceService persistence, TestAiService service)
    {
        window.WindowState = WindowState.Normal; window.Width = 1000; window.Height = 680; window.Show(); Pump();
        var home = window.FindControl<Grid>("HomeDashboardGrid")!;
        var percents = home.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is AiQuotaMetric && Grid.GetColumn(t) == 2).ToArray();
        Require(percents.Length == 10, "El Radar conserva ambas ventanas de Codex y los ocho modelos de Antigravity.");
        foreach (var percent in percents)
        {
            var card = percent.GetVisualAncestors().OfType<Border>().First(b => b.DataContext is AiEnvironmentItem);
            var edge = percent.TranslatePoint(new Point(percent.Bounds.Width, 0), card)!.Value;
            Require(edge.X <= card.Bounds.Width - card.Padding.Right + .5 && percent.Bounds.Width >= percent.DesiredSize.Width,
                $"El porcentaje {percent.Text} cabe completo al abrir a 1000 px: borde {edge.X}, tarjeta {card.Bounds.Width}.");
        }
        foreach (var size in new[] { (Width: 1000, Height: 680), (Width: 1920, Height: 1080) })
        {
            window.Width = size.Width; window.Height = size.Height; Pump();
            var names = home.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "AiModelNameText").ToArray();
            Require(names.Length == 10 && names.All(text => text.Text == ((AiQuotaMetric)text.DataContext!).ModelName &&
                text.TextWrapping == TextWrapping.Wrap && text.TextTrimming == TextTrimming.None &&
                text.IsVisible == ((AiQuotaMetric)text.DataContext!).HasModelLabel), "Los nombres completos de modelos se leen sin elipsis; Codex no repite su titulo.");
            foreach (var bar in home.GetVisualDescendants().OfType<ProgressBar>().Where(bar => bar.DataContext is AiQuotaMetric))
            {
                Require(bar.Bounds.Width == 64 && bar.Bounds.Height == 4, "Las barras mantienen su tamano compacto en ambas resoluciones.");
                var row = bar.GetVisualAncestors().OfType<Grid>().First();
                var label = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "AiModelNameText");
                var labelEdge = label.TranslatePoint(new Point(label.Bounds.Width, 0), row)!.Value;
                Require(labelEdge.X <= bar.Bounds.Left, "El nombre del LLM nunca invade la barra.");
            }
        }
        Require(model.AiEnvironments[0].Id == "codex" && !model.DetectedAiEnvironments[0].MoveUpCommand.CanExecute(null),
            "Codex tiene prioridad inicial aunque la deteccion entregue Antigravity primero.");
        model.SelectedTabIndex = 3; Pump();
        var antigravity = model.DetectedAiEnvironments.Single(item => item.Id == "antigravity");
        var up = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.DataContext, antigravity) && ReferenceEquals(button.Command, antigravity.MoveUpCommand));
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(up, null); Pump();
        Require(model.AiEnvironments[0].Id == "antigravity" && persistence.LoadConfig().Settings.AiEnvironmentOrder[0] == "antigravity",
            "Subir en Ajustes cambia inmediatamente el Radar y guarda el orden local.");
        model.DetectedAiEnvironments.Single(item => item.Id == "codex").MoveUpCommand.Execute(null); Pump();
        var savedOrder = persistence.LoadConfig().Settings.AiEnvironmentOrder.ToArray();
        Require(savedOrder[0] == "codex" && !model.DetectedAiEnvironments[^1].MoveDownCommand.CanExecute(null),
            "El usuario puede devolver Codex al primer puesto; los limites impiden salir de la lista.");
        var claude = model.DetectedAiEnvironments.Single(item => item.Id == "claude");
        claude.ShowInHome = false; Pump();
        Require(!model.AiEnvironments.Any(item => item.Id == "claude") && persistence.LoadConfig().Settings.HiddenAiEnvironmentIds.Contains("claude"),
            "Deseleccionar Claude retira solo su acceso y guarda la preferencia.");
        var refresh = model.RefreshAiQuotas(); Wait(() => refresh.IsCompleted);
        Require(!model.DetectedAiEnvironments.Single(item => item.Id == "claude").ShowInHome && model.AiEnvironments.Count == 4 &&
            model.DetectedAiEnvironments.Select(item => item.Id).SequenceEqual(savedOrder),
            "Volver a detectar conserva la selección del usuario.");
        model.DetectedAiEnvironments.Single(item => item.Id == "ollama").OpenCommand.Execute(null);
        Require(service.LastOpened == "ollama", "El acceso invoca exclusivamente la herramienta seleccionada.");
        model.DetectedAiEnvironments.Single(item => item.Id == "claude").ShowInHome = true;
        using (var restoredAdapter = new WorkflowTestAdapter())
        using (var restoredEngine = new WorkflowEngine(restoredAdapter, persistence))
        using (var restoredModel = new MainViewModel(restoredAdapter, restoredEngine, persistence, service, new TestAppCatalog()))
        {
            Wait(() => restoredModel.DetectedAiEnvironments.Count == 5);
            Require(restoredModel.DetectedAiEnvironments.Select(item => item.Id).SequenceEqual(savedOrder),
                "Un ViewModel nuevo recupera el orden desde el config portable.");
        }
        model.SelectedTabIndex = 0;
        window.Width = 1000; window.Height = 680; Pump();
        string previewDirectory = Path.Combine(Path.GetDirectoryName(persistence.Storage.DataDirectory)!, "radar-preview.png");
        using (var preview = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1000, 680), new Vector(96, 96)))
        { preview.Render(window); preview.Save(previewDirectory); }
        Console.WriteLine($"[PASS] Radar IA: nombres completos, barras compactas, orden persistente y clic real en Ajustes. Vista: {previewDirectory}");
        window.Hide(); window.WindowState = WindowState.Maximized;
    }

    private sealed class TestAiService : IAiQuotaService
    {
        public string? LastOpened;
        public AiQuotaSnapshot InspectLocalQuotas() => new(true,
        [new("antigravity", "Google Antigravity IDE", "IconReticle", true, "C:/Antigravity.exe", null,
            Enumerable.Range(0, 8).Select(index => new AiQuotaMetric($"Gemini 3.8 Pro (High reasoning) modelo {index + 1}",
                $"Gemini 3.8 Pro (High reasoning) modelo {index + 1} · Ventana 5h", 96.6, "96,6% restante")).ToList(), 96.6, "", false, HasExplicitQuotaMetrics: true),
         new("codex", "Codex Desktop / VS Code", "IconReticle", true, "C:/Codex.exe", null,
            [new("Codex", "Ventana 5h", 100, ""), new("Codex", "Semanal", 99.9, "")], 100, "", false, HasExplicitQuotaMetrics: true),
         new("claude", "Claude", "IconTerminal", true, "C:/Claude.exe", null, [], 0, "", false),
         new("lmstudio", "LM Studio", "IconTerminal", true, "C:/LM Studio.exe", null, [], 0, "", false),
         new("ollama", "Ollama", "IconTerminal", true, "C:/ollama.exe", null, [], 0, "", false)], null);
        public bool LaunchEnvironment(string id) { LastOpened = id; return true; }
    }

    private static void VerifyRemoteControls(MainWindow window, MainViewModel model, PersistenceService persistence, WorkflowTestAdapter adapter)
    {
        var connectionButton = window.FindControl<Button>("RemoteConnectionButton")!;
        Require(connectionButton is not null && !connectionButton.IsVisible,
            "El acceso QR debe ocultarse mientras el control remoto esté desactivado.");
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start(); model.RemoteControlPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        model.RemoteControlEnabled = true;
        Wait(() => model.RemoteServerRunning && model.RemoteQrCode is not null);
        Require(model.RemoteConnectionUrl.Contains("#key=") && model.RemoteQrCode!.PixelSize.Width > 100 &&
            persistence.LoadConfig().LanServer.Enabled, "El control LAN guarda sus ajustes y genera un QR de emparejamiento.");
        Require(connectionButton!.IsVisible && connectionButton.Flyout is Flyout,
            "Habilitar LAN muestra el acceso superior al QR.");
        var content = (Border)((Flyout)connectionButton.Flyout!).Content!;
        // Opening the flyout attaches the content to the button's data context.
        window.Show(); connectionButton.Flyout.ShowAt(connectionButton); Pump();
        var image = content.GetLogicalDescendants().OfType<Image>().Single();
        Require(ReferenceEquals(image.Source, model.RemoteQrCode) && image.IsVisible && image.Width == 160,
            "El acceso superior muestra el mismo QR vivo que Ajustes.");
        connectionButton.Flyout.Hide(); window.Hide();
        model.RemoteControlEnabled = false;
        Wait(() => !model.RemoteServerRunning && model.RemoteQrCode is null);
        Require(!connectionButton.IsVisible && image.Source is null,
            "Desactivar LAN oculta el botón y retira el QR anterior.");
        Require(!persistence.LoadConfig().LanServer.Enabled, "Desactivar el módulo cierra el listener y retira el QR.");
        using var source = new SkiaSharp.SKBitmap(4, 2);
        source.Erase(SkiaSharp.SKColors.Cyan);
        using var original = SkiaSharp.SKImage.FromBitmap(source);
        using var bytes = original.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        adapter.CaptureBytes = bytes.ToArray();
        byte[] jpeg = Nokto.UI.Remote.RemotePreviewEncoder.CaptureAsync(adapter, CancellationToken.None).GetAwaiter().GetResult();
        using var decoded = SkiaSharp.SKBitmap.Decode(jpeg);
        Require(decoded.Width == 1280 && decoded.Height == 720 && decoded.GetPixel(0, 0).Red < 10 &&
            decoded.GetPixel(640, 360).Green > 200, "El snapshot es JPEG 1280×720 y conserva las proporciones del escritorio.");
    }

    private static PresetDefinition Routine(string id) => new()
    {
        Id = id, Name = "Rutina " + id,
        Trigger = new() { Type = TriggerType.Countdown, Parameters = new() { ["durationSeconds"] = JsonSerializer.SerializeToElement(30) } },
        TerminalAction = new() { Type = TerminalActionType.None }
    };
    private static void VerifyFooter(Window window)
    {
        var root = (Grid)window.Content!;
        var footer = root.Children.OfType<Border>().Single(b => Grid.GetRow(b) == 2);
        foreach (double width in new[] { 1000d, 1100d, 1920d })
        {
            footer.Measure(new Size(width, 40));
            footer.Arrange(new Rect(0, 0, width, 40));
            var grid = (Grid)footer.Child!;
            var center = grid.Children.Single(c => Grid.GetColumn(c) == 1);
            var metrics = grid.Children.Single(c => Grid.GetColumn(c) == 2);
            Require(center.Bounds.Right <= metrics.Bounds.Left + 0.1 && metrics.Bounds.Right <= grid.Bounds.Width + 0.1,
                "El estado de rutinas no debe desplazar ni superponerse a las métricas.");
        }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Wait(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        do { Pump(); Thread.Sleep(5); } while (!done() && DateTime.UtcNow < deadline);
        Pump(); Require(done(), "Timeout al actualizar la UI de rutinas.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
