using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows.Media;
using Nokto.UI.Controls;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;
using System.ComponentModel;
using System.Reflection;

internal static class IslandHarmonyProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Field(IslandWindow w, string key, object value) => typeof(IslandWindow).GetField(key, Flags)!.SetValue(w, value);
    private static void Call(IslandWindow w, string key, params object?[] args) => typeof(IslandWindow).GetMethod(key, Flags)!.Invoke(w, args);
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("[PASS] " + label); }

    public static void Run()
    {
        ((Nokto.UI.App)Application.Current!).SetLanguage("es");
        Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Check(Application.Current.Resources.TryGetResource("IslandOpenRoutines", Avalonia.Styling.ThemeVariant.Dark, out _), "Diccionario de idioma cargado para revisar etiquetas reales");
        string root = Path.Combine(Path.GetTempPath(), "nokto-harmony-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new PersistenceService(new StorageResolver(root));
        var model = new IslandViewModel(new AppConfig(), storage) { ReducedMotion = true };
        model.SelectStyle("CrimsonRing");
        string[] audio = ["media", "volume", "microphone", "output"], access = ["explorer", "folders", "files", "clipboard"], status = ["battery", "network", "weather", "routines"];
        foreach (var feature in model.PageFeatures)
            for (int slot = 0; slot < 3; slot++) model.SetFeatureInSlot(feature.Id, slot, (slot == 0 ? audio : slot == 1 ? access : status).Contains(feature.Id));
        string folder = Path.Combine(root, "Proyectos"); Directory.CreateDirectory(folder); model.AddQuickFolder(folder);
        string second = Path.Combine(root, "Publicaciones"); Directory.CreateDirectory(second); model.AddQuickFolder(second);
        foreach (string name in new[] { "Notas de diseño.txt", "Lista de pendientes.txt" })
        { string path = Path.Combine(root, name); File.WriteAllText(path, "fixture"); model.AddShelfFile(path); }
        var settingsView = new IslandSettingsView { DataContext = new { IslandVm = model } };
        var settingsHost = new Window { Content = settingsView, Width = 1200, Height = 3600 };
        settingsHost.Measure(new Size(1200, 3600)); settingsHost.Arrange(new Rect(0, 0, 1200, 3600)); Dispatcher.UIThread.RunJobs();
        settingsView.FindControl<CheckBox>("ShowLyricsButtonSetting")!.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        settingsView.FindControl<CheckBox>("ShowRecognitionButtonSetting")!.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        Dispatcher.UIThread.RunJobs();
        var saved = new IslandViewModel(storage.LoadConfig(), storage);
        Check(!saved.ShowLyricsButton && !saved.ShowRecognitionButton, "Casillas reales de Letras y Reconocer guardan visibilidad independiente");
        var board = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Top };
        for (int page = 0; page < 3; page++)
        {
            var data = new SceneData(model);
            var window = new IslandWindow { DataContext = data };
            Field(window, "_settings", model); Field(window, "_pageIndex", page); Field(window, "_shelfOpen", true);
            Field(window, "_mediaSnapshot", new MediaSessionSnapshot(true, "Luces de la ciudad", "Nokto · pista de prueba", "fixture", true, true, true, true, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(220), null) { CanSeek = true });
            Field(window, "_mediaUpdatedAt", DateTimeOffset.UtcNow); Field(window, "_expanded", true);
            window.ApplySettings();
            if (page == 0)
            {
                Check(!window.FindControl<Grid>("MediaToolsRow")!.IsVisible, "Ocultar ambos botones elimina la fila completa");
                double hidden = Height(window);
                model.ShowLyricsButton = true; Call(window, "RefreshMediaPresentation", false);
                Check(window.FindControl<Button>("LyricsButton")!.IsVisible && !window.FindControl<Button>("RecognitionButton")!.IsVisible &&
                    window.FindControl<Grid>("MediaToolsRow")!.ColumnDefinitions[1].Width.Value == 0, "Letras puede mostrarse sola sin columna vacía");
                model.ShowLyricsButton = false; model.ShowRecognitionButton = true; Call(window, "RefreshMediaPresentation", false);
                Check(!window.FindControl<Button>("LyricsButton")!.IsVisible && window.FindControl<Button>("RecognitionButton")!.IsVisible,
                    "Reconocer puede mostrarse solo, independientemente de Letras");
                model.ShowLyricsButton = true; Call(window, "RefreshMediaPresentation", false);
                Check(Height(window) > hidden, "La altura sigue el contenido al activar las acciones musicales");
            }
            window.FindControl<TextBlock>("WeatherText")!.Text = "Santiago · 19 °C · Despejado";
            window.FindControl<TextBlock>("WeatherDetail")!.Text = "Humedad 62 % · ciudad de ejemplo";
            window.FindControl<TextBlock>("ClipboardPreview")!.Text = "Texto de ejemplo leído bajo pulsación";
            window.FindControl<TextBlock>("ClockText")!.Text = "12:40";
            window.FindControl<StackPanel>("ExpandedTools")!.IsVisible = true;
            Call(window, "ArrangeContentSections");
            double width = (double)typeof(IslandWindow).GetProperty("ExpandedWidth", Flags)!.GetValue(window)!;
            double height = Height(window);
            Call(window, "SetIslandDimensions", width, height); Call(window, "ApplyTransitionPresentation", 1d);
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs();
            window.InvalidateMeasure(); window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs();
            if (page == 1)
            {
                var list = window.FindControl<ListBox>("ShelfList")!; list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
                Check(list.GetVisualDescendants().OfType<ListBoxItem>().Count() == 2 && window.FindControl<Button>("ShelfOpenButton")!.IsEnabled,
                    "La bandeja muestra todos los archivos y habilita acciones tras seleccionar");
            }
            if (page == 0)
            {
                var slider = window.GetVisualDescendants().OfType<Slider>().Single();
                var track = slider.GetVisualDescendants().OfType<Track>().Single();
                track.SetCurrentValue(Track.ValueProperty, 72d); Dispatcher.UIThread.RunJobs();
                Check(Math.Abs(slider.Value - 72) < .01 && Math.Abs(data.MasterVolumePercent - 72) < .01, "Plantilla de volumen conserva enlace bidireccional Track → Slider → modelo simulado");
                slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                Check(slider.Value > 72, "Teclado del volumen funciona con la plantilla nueva sin alterar audio real");
                track.SetCurrentValue(Track.ValueProperty, 42d); Dispatcher.UIThread.RunJobs();
            }
            var surface = window.FindControl<Border>("IslandSurface")!;
            Check(surface.GetVisualDescendants().OfType<Button>().All(b => b is IslandButton or IslandAudioButton or RepeatButton), "Cada acción visible usa la familia visual de la isla");
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width), (int)Math.Ceiling(height)), new Vector(96, 96)))
            { bitmap.Render(surface); bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", $"qa-island-harmony-0{page + 1}.png")); }
            surface.DataContext = data; ((Grid)surface.Parent!).Children.Remove(surface);
            board.Children.Add(new Border { Width = width, Height = height, Child = surface, VerticalAlignment = VerticalAlignment.Top });
            Call(window, "DisposeMedia"); window.FindControl<NoktoEyesControl>("EyesVisual")!.StopActivation();
        }
        var canvas = new Border { Background = Brush.Parse("#0B1017"), Padding = new Thickness(16), Child = board };
        // Adjuntar solo la escena sintética permite resolver DynamicResource como en producción.
        // Ventana fuera de pantalla, sin activación, observadores ni datos personales.
        var preview = new Window { Content = canvas, ShowActivated = false, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None,
            Position = new PixelPoint(-5000, -5000), Width = 1060, Height = 1000 };
        preview.Show(); Dispatcher.UIThread.RunJobs();
        var openFile = canvas.GetVisualDescendants().OfType<IslandButton>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "IslandShelfOpen");
        Check(openFile.IsEffectivelyEnabled && ((Border)typeof(IslandButton).GetField("_surface", Flags)!.GetValue(openFile)!).Opacity == 1,
            "La acción de archivo habilitada también abandona su apariencia desactivada");
        canvas.Measure(new Size(1600, 1100)); canvas.Arrange(new Rect(0, 0, canvas.DesiredSize.Width, canvas.DesiredSize.Height)); Dispatcher.UIThread.RunJobs();
        Check(canvas.GetVisualDescendants().OfType<Button>().Where(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) is "IslandRoutinesButton" or "IslandClipboardRead")
            .All(b => !string.IsNullOrWhiteSpace(b.Content?.ToString())), "Rutinas y portapapeles muestran sus etiquetas localizadas");
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(canvas.Bounds.Width), (int)Math.Ceiling(canvas.Bounds.Height)), new Vector(96, 96));
        image.Render(canvas); image.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-harmony-overview.png"));
        preview.Close();
        settingsHost.Close();
    }
    private static double Height(IslandWindow window) => (double)typeof(IslandWindow).GetProperty("ExpandedHeightForContent", Flags)!.GetValue(window)!;
    private sealed class SceneData(IslandViewModel model) : INotifyPropertyChanged
    {
        public IslandViewModel IslandVm => model;
        public bool IsInputMuted => true; public bool IsOutputMuted => false; public bool HasInputAudio => true; public bool HasOutputAudio => true;
        public string IslandInputState => "Silenciado"; public string IslandOutputState => "Activa";
        public string InputMuteButtonText => "Activar micrófono"; public string OutputMuteButtonText => "Silenciar salida";
        public string BatteryAndPowerText => "Red eléctrica · 100 %"; public string NetworkInterfaceText => "Wi-Fi conectado · 866 Mbps";
        private double _volume = 42;
        public double MasterVolumePercent { get => _volume; set { _volume = value; PropertyChanged?.Invoke(this, new(nameof(MasterVolumePercent))); PropertyChanged?.Invoke(this, new(nameof(MasterVolumeText))); } }
        public string MasterVolumeText => $"{_volume:0}%";
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
