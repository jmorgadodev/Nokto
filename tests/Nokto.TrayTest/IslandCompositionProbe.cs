using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.UI.Controls;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;
using Nokto.Platform.Windows.Media;

internal static class IslandCompositionProbe
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("[PASS] " + message); }
    private static void Call(IslandWindow island, string method, params object?[] args) => typeof(IslandWindow).GetMethod(method, Flags)!.Invoke(island, args);
    private static void Field(IslandWindow island, string name, object value) => typeof(IslandWindow).GetField(name, Flags)!.SetValue(island, value);
    public static void Run()
    {
        ((Nokto.UI.App)Application.Current!).SetLanguage("es");
        Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        var failures = new List<Exception>();
        try
        {
            double[] heights = [240, 86, 115, 70, 36, 158, 67];
            var columns = IslandColumnLayout.Place(heights, 2);
            double[] totals = Enumerable.Range(0, 2).Select(c => heights.Where((_, i) => columns[i] == c).Sum() + 8 * Math.Max(0, columns.Count(x => x == c) - 1)).ToArray();
            Console.WriteLine($"Alturas de columnas: {string.Join(", ", totals)}");
            Check(totals.Max() <= 480, "Los bloques de la captura se equilibran en dos columnas, sin una columna casi vacía");
            Check(columns.SequenceEqual(columns.Order()), "El equilibrio respeta el orden elegido");
        }
        catch (Exception ex) { failures.Add(ex); }
        string root = Path.Combine(Path.GetTempPath(), "nokto-composition-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var config = new AppConfig(); config.Island.EyeCount = 1;
        var model = new IslandViewModel(config, new PersistenceService(new StorageResolver(root)))
            { ReducedMotion = true, ShowMainButton = false, ShowHideButton = false };
        model.SelectStyle("CrimsonRing");
        string[] enabled = ["media", "volume", "microphone", "output", "weather", "explorer", "folders", "battery"];
        foreach (var feature in model.PageFeatures) model.SetFeatureInSlot(feature.Id, 0, enabled.Contains(feature.Id));
        var folder = Path.Combine(root, "Publicaciones"); Directory.CreateDirectory(folder); model.AddQuickFolder(folder);
        var island = new IslandWindow { DataContext = new CompositionData(model), ShowActivated = false, Position = new PixelPoint(-5000, -5000) };
        Field(island, "_settings", model); Field(island, "_animationTimer", new DispatcherTimer());
        Field(island, "_mediaSnapshot", new MediaSessionSnapshot(true, "Pasión · pista de ejemplo", "Artista de prueba", "fixture", false, true, true, true, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(308), null));
        Field(island, "_mediaUpdatedAt", DateTimeOffset.UtcNow);
        island.ApplySettings(); island.Show();
        try
        {
            Call(island, "SetExpanded", true);
            island.FindControl<TextBlock>("WeatherText")!.Text = "Santiago · 16 °C · Nublado";
            island.FindControl<TextBlock>("WeatherDetail")!.Text = "Humedad 95 % · ciudad de ejemplo";
            Capture(island, "qa-island-composition-menu.png");
            double menuWidth = island.Width;
            Check(island.Screens.Primary is not { } screen || island.Height * screen.Scaling <= screen.WorkingArea.Height,
                "El módulo con las ocho funciones de la captura cabe en el área útil");
            var viewport = island.FindControl<ScrollViewer>("PageScrollViewer")!;
            Check(viewport.Extent.Height <= viewport.Viewport.Height + 1, "El módulo combinado no necesita scroll vertical");
            // Forzar dos columnas con una escala alta reproduce la distribución crítica.
            model.SizePercent = 140; island.ApplySettings();
            Capture(island, "qa-island-composition-menu-large.png");
            model.SizePercent = 100; island.ApplySettings();
            island.FindControl<Button>("RecognitionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check(island.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "¿Qué está sonando?"), "Reconocer canción pertenece al árbol visual de la isla y no abre una ventana flotante");
            Capture(island, "qa-island-composition-recognition.png");
            Check(island.Width <= 333 && !island.FindControl<Grid>("IslandPageNav")!.IsVisible, "Reconocimiento usa una sola columna de la isla, sin navegación de módulos ajena a la tarea");
            var panel = island.GetVisualDescendants().OfType<SongRecognitionView>().Single();
            var source = (Button)typeof(SongRecognitionView).GetField("_computer", Flags)!.GetValue(panel)!;
            source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Check((bool)typeof(SongRecognitionView).GetField("_loopback", Flags)!.GetValue(panel)! &&
                typeof(SongRecognitionView).GetField("_request", Flags)!.GetValue(panel) is null, "Elegir audio del PC no inicia captura");
            Check(!panel.GetVisualDescendants().Any(c => c is ComboBox or TextBox), "La isla ofrece reconocimiento sin configuración de proveedores o claves");
            var back = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "‹  Volver al módulo");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Check(!island.GetVisualDescendants().OfType<SongRecognitionView>().Any() && Math.Abs(island.Width - menuWidth) < 1 && island.FindControl<Grid>("IslandPageNav")!.IsVisible,
                "Volver restaura el módulo, su ancho y sus controles");
            island.FindControl<Button>("RecognitionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Call(island, "SetExpanded", false);
            Check(!island.GetVisualDescendants().OfType<SongRecognitionView>().Any(), "Al contraerse, la isla cierra el reconocimiento");
        }
        catch (Exception ex) { failures.Add(ex); }
        finally
        {
            typeof(IslandWindow).GetMethod("DisposeMedia", Flags)!.Invoke(island, null);
            island.FindControl<NoktoEyesControl>("EyesVisual")!.StopActivation(); island.Close();
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static void Capture(IslandWindow island, string file)
    {
        Call(island, "ArrangeContentSections");
        double height = (double)typeof(IslandWindow).GetProperty("ExpandedHeightForContent", Flags)!.GetValue(island)!;
        double width = (double)typeof(IslandWindow).GetProperty("ExpandedWidth", Flags)!.GetValue(island)!;
        Call(island, "SetIslandDimensions", width, height); Call(island, "ApplyTransitionPresentation", 1d);
        // Solo presentación fuera de pantalla, sin observadores ni servicios del usuario.
        island.Position = new PixelPoint(-5000, -5000);
        island.InvalidateMeasure(); island.Measure(new Size(width, height)); island.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs();
        var surface = island.FindControl<Border>("IslandSurface")!;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width), (int)Math.Ceiling(height)), new Vector(96, 96));
        bitmap.Render(island.FindControl<Grid>("IslandScaleHost")!);
        bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", file));
        Console.WriteLine($"{file}: {width:0} × {height:0}");
    }

    private sealed class CompositionData(IslandViewModel model)
    {
        public IslandViewModel IslandVm => model;
        public bool IsInputMuted => true; public bool IsOutputMuted => false;
        public bool HasInputAudio => true; public bool HasOutputAudio => true;
        public string IslandInputState => "Silenciado"; public string IslandOutputState => "Activa";
        public string BatteryAndPowerText => "Red eléctrica · 100 %";
        public double MasterVolumePercent { get; set; } = 66;
        public string MasterVolumeText => "66 %";
    }
}
