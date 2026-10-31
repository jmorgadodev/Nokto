using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;
using Nokto.UI.Controls;
using Nokto.Platform.Windows.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reflection;

internal static class IslandLayoutProbe
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "nokto-island-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var storage = new PersistenceService(new StorageResolver(root));
        var model = new IslandViewModel(new AppConfig(), storage);
        VerifyTransition(model);
        var media = model.PageFeatures.Single(x => x.Id == "media");
        var volume = model.PageFeatures.Single(x => x.Id == "volume");
        var files = model.PageFeatures.Single(x => x.Id == "files");
        var weather = model.PageFeatures.Single(x => x.Id == "weather");
        Require(media.Page1 && !media.Page2 && volume.Page2 && !volume.Page1, "Distribución anterior por defecto");
        Require(!weather.Page1 && !model.ShowWeather, "Función desactivada no se presenta como activa");
        VerifySettingsBindings(model);
        volume.Page2 = true;
        weather.Page1 = true;
        Require(weather.Page1 && model.ShowWeather, "Marcar clima activa la función aunque ya tuviera página predeterminada");
        model.ShowWeather = false;
        Require(!weather.Page1, "Apagar la función actualiza la matriz");
        volume.Page1 = true;
        files.Page1 = true;
        Require(model.PageHasFeature("volume", 0) && model.PageHasFeature("volume", 1) && model.ShowFileShelf,
            "Combinar sonido y archivos en la primera página habilita las funciones");
        model.PageOne = 1;
        Require(volume.Page1 && volume.Page2 && media.Page2 && !media.Page1,
            "La matriz sigue el orden visible al intercambiar páginas");
        var restored = new IslandViewModel(storage.LoadConfig(), storage);
        Require(restored.PageOne == 1 && restored.FeatureInSlot("volume", 1) && restored.FeatureInSlot("files", 1),
            "Orden y elementos sobreviven al guardar y cargar");
        restored.AddAudioToSlot(0);
        Require(new[] { "media", "volume", "microphone", "output" }.All(id => restored.FeatureInSlot(id, 0)),
            "Audio completo incluye reproductor, volumen y ambos botones en un módulo");
        restored.AddAudioToSlot(1);
        restored.PageOne = 0;
        restored.AddAudioToSlot(0);
        string folder = Path.Combine(root, "carpeta-prueba");
        Directory.CreateDirectory(folder);
        restored.ShowQuickActions = true;
        Require(restored.AddQuickFolder(folder), "Crear acceso de carpeta");
        restored.SetFeatureInSlot("folders", 0, true);
        restored.SetFeatureInSlot("explorer", 0, true);
        while (restored.ContentOrderForPage(0).ToList().IndexOf("explorer") > 1) restored.MoveContent("explorer", 0, -1);
        while (restored.ContentOrderForPage(0).ToList().IndexOf("folders") > 2) restored.MoveContent("folders", 0, -1);
        var reordered = new IslandViewModel(storage.LoadConfig(), storage);
        Require(reordered.ContentOrderForPage(0).Take(3).SequenceEqual(new[] { "media", "explorer", "folders" }),
            "El orden de contenidos por módulo sobrevive al guardar y cargar");
        VerifyIslandLayout(restored, folder);
        Require(Directory.Exists(folder), "Quitar acceso conserva la carpeta");
        Console.WriteLine("[PASS] Configuración de páginas, compatibilidad y acceso local verificados.");
    }

    private static void VerifyTransition(IslandViewModel model)
    {
        var window = new IslandWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Field(string name, object value) => typeof(IslandWindow).GetField(name, flags)!.SetValue(window, value);
        void Expand(bool value) => typeof(IslandWindow).GetMethod("SetExpanded", flags)!.Invoke(window, [value]);
        void Frame(double elapsed)
        {
            Field("_animationStart", System.Diagnostics.Stopwatch.GetTimestamp() - (long)(elapsed * System.Diagnostics.Stopwatch.Frequency));
            typeof(IslandWindow).GetMethod("AdvanceAnimation", flags)!.Invoke(window, [true]);
        }
        Field("_settings", model);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        Field("_animationTimer", timer);
        window.ApplySettings();
        var surface = window.FindControl<Border>("IslandSurface")!;
        var tools = window.FindControl<StackPanel>("ExpandedTools")!;
        Expand(true);
        Require(surface.Background!.Opacity == 0, "El fondo no aparece de golpe al abrir");
        double contentWidth = tools.Width;
        Frame(.055);
        Require(surface.Background.Opacity > 0 && surface.Background.Opacity < 1, "El fondo tiene fotogramas intermedios de opacidad");
        Require(tools.Width == contentWidth && tools.Opacity < 1, "El contenido conserva su ancho y aparece gradualmente");
        double before = surface.Background.Opacity;
        Expand(false);
        Require(Math.Abs(surface.Background.Opacity - before) < .2, "Invertir la transición no apaga el fondo de golpe");
        Frame(1.1);
        Require(surface.Background.Opacity == 0 && !tools.IsVisible, "El cierre termina transparente y sin controles visibles");
        timer.Stop(); window.FindControl<NoktoEyesControl>("EyesVisual")!.StopActivation();
        Console.WriteLine("[PASS] Apertura y cierre: opacidad gradual, ancho estable e inversión continua.");
    }

    private static void VerifySettingsBindings(IslandViewModel model)
    {
        var view = new IslandSettingsView { DataContext = new { IslandVm = model } };
        var window = new Window { Content = view, Width = 1200, Height = 3500 };
        window.Measure(new Size(1200, 3500));
        window.Arrange(new Rect(0, 0, 1200, 3500));
        Dispatcher.UIThread.RunJobs();
        var count = view.FindControl<ComboBox>("EyeCountSelector")!;
        count.SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 0);
        Dispatcher.UIThread.RunJobs();
        Require(model.EyeCount == 1 && model.EyeStyleOptions.All(option => option.EyeCount == 1),
            "El selector real permite un ojo y actualiza todas las tarjetas de la galería");
        var gallery = view.FindControl<ItemsControl>("EyeGallery")!;
        var previews = gallery.GetVisualDescendants().OfType<NoktoEyesControl>().ToArray();
        Require(previews.Length > 0 && previews.All(preview => preview.EyeCount == 1),
            "La galería muestra realmente un ojo al seleccionar esa opción");
        var matrix = view.FindControl<ItemsControl>("PageFeatureMatrix")!;
        var volumeFeature = model.PageFeatures.Single(f => f.Id == "volume");
        var handle = matrix.GetVisualDescendants().OfType<Border>().Single(b => b.Focusable && ReferenceEquals(b.DataContext, volumeFeature));
        int beforeOrder = model.ContentOrderForPage(model.PageOne).ToList().IndexOf("volume");
        handle.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Up, KeyModifiers = Avalonia.Input.KeyModifiers.Alt });
        Require(model.ContentOrderForPage(model.PageOne).ToList().IndexOf("volume") == beforeOrder - 1,
            "El asa de arrastre también permite ordenar mediante Alt + flecha");
        volumeFeature.Down1.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.InvalidateMeasure();
        window.Measure(new Size(1200, 3500)); window.Arrange(new Rect(0, 0, 1200, 3500));
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine($"Matriz: fuente={matrix.ItemsSource is System.Collections.IEnumerable}, casillas={matrix.GetVisualDescendants().OfType<CheckBox>().Count()}, filas={model.PageFeatures.Count}");
        var volume = matrix.GetVisualDescendants().OfType<CheckBox>()
            .Single(box => box.DataContext is IslandPageFeature { Id: "volume" });
        volume.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Dispatcher.UIThread.RunJobs();
        Require(model.FeatureInSlot("volume", 0), "La casilla visible agrega volumen al módulo 01");
        var audioButton = view.FindControl<Button>("AudioToPage1")!;
        model.ShowSlot1 = false;
        Require(audioButton.Command is not null && audioButton.Command.CanExecute(audioButton.CommandParameter), "Botón de audio completo disponible");
        audioButton.Command!.Execute(audioButton.CommandParameter);
        Require(model.ShowSlot1 && new[] { "media", "volume", "microphone", "output" }.All(id => model.FeatureInSlot(id, 0)),
            "El botón real asigna todos los controles de audio al módulo 01");
        Require(new[] { "media", "volume", "microphone", "output" }.All(id => !model.FeatureInSlot(id, 1) && !model.FeatureInSlot(id, 2)),
            "El audio completo se quita de los otros módulos");
        var island = new IslandWindow { DataContext = new { IslandVm = model } };
        typeof(IslandWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(island, model);
        typeof(IslandWindow).GetMethod("RefreshPages", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(island, null);
        Require(island.FindControl<Border>("MediaEmptyState")!.IsVisible && island.FindControl<Grid>("VolumeRow")!.IsVisible &&
            island.FindControl<StackPanel>("MicControl")!.IsVisible && island.FindControl<StackPanel>("OutputControl")!.IsVisible,
            "El módulo 01 muestra reproductor, volumen, micrófono y salida simultáneamente");
        VerifyMediaPlayer(island);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(1200, 2400), new Vector(96, 96)))
        {
            bitmap.Render(view);
            bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-settings-modules.png"));
        }
        Console.WriteLine("[PASS] Los controles de Ajustes cambian cantidad de ojos y contenido del módulo.");
    }

    private static void VerifyMediaPlayer(IslandWindow island)
    {
        var snapshot = new MediaSessionSnapshot(true, "Canción de prueba", "Artista de prueba", "test",
            true, true, true, true, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(220), null);
        var snapshotField = typeof(IslandWindow).GetField("_mediaSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var refresh = typeof(IslandWindow).GetMethod("RefreshMediaPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        snapshotField.SetValue(island, snapshot);
        typeof(IslandWindow).GetField("_mediaUpdatedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(island, DateTimeOffset.UtcNow);
        refresh.Invoke(island, [false]);
        Require(island.FindControl<Avalonia.Controls.Shapes.Path>("MediaPauseIcon")!.IsVisible &&
            !island.FindControl<Avalonia.Controls.Shapes.Path>("MediaPlayIcon")!.IsVisible &&
            island.FindControl<TextBlock>("MediaPlayLabel")!.Text == "Pausar" &&
            island.FindControl<TextBlock>("MediaTime")!.Text == "00:45" &&
            island.FindControl<TextBlock>("MediaDuration")!.Text == "03:40", "Reproductor muestra pausa y tiempos separados");
        snapshotField.SetValue(island, snapshot with { IsPlaying = false, CanNext = false });
        refresh.Invoke(island, [false]);
        Require(island.FindControl<Avalonia.Controls.Shapes.Path>("MediaPlayIcon")!.IsVisible &&
            !island.FindControl<Avalonia.Controls.Shapes.Path>("MediaPauseIcon")!.IsVisible &&
            island.FindControl<TextBlock>("MediaPlayLabel")!.Text == "Reproducir" &&
            !island.FindControl<Button>("MediaNextButton")!.IsEnabled, "Reproductor pausado y acción no disponible");
        var panel = island.FindControl<Border>("MediaPanel")!;
        panel.Measure(new Size(280, 400));
        panel.Arrange(new Rect(0, 0, 280, panel.DesiredSize.Height));
        using var image = new RenderTargetBitmap(new PixelSize(280, (int)Math.Ceiling(panel.DesiredSize.Height)), new Vector(96, 96));
        image.Render(panel);
        image.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-player.png"));
        Console.WriteLine("[PASS] Reproductor: iconos vectoriales, estados, controles y tiempos simulados.");
    }

    private static void VerifyIslandLayout(IslandViewModel model, string folder)
    {
        var window = new IslandWindow { DataContext = new { IslandVm = model }, Width = 332, Height = 600 };
        typeof(IslandWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, model);
        typeof(IslandWindow).GetField("_pageIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 0);
        window.FindControl<Border>("IslandSurface")!.Width = 332;
        window.FindControl<Border>("IslandSurface")!.Height = 600;
        var tools = window.FindControl<StackPanel>("ExpandedTools")!;
        tools.IsVisible = true;
        tools.Opacity = 1;
        typeof(IslandWindow).GetMethod("RefreshPages", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Require(window.FindControl<StackPanel>("OverviewPage")!.IsVisible && window.FindControl<StackPanel>("AudioPage")!.IsVisible,
            "Resumen y volumen se muestran juntos en la página elegida");
        window.Measure(new Size(332, 600));
        window.Arrange(new Rect(0, 0, 332, 600));
        Dispatcher.UIThread.RunJobs();
        double actualHeight = (double)typeof(IslandWindow).GetProperty("ExpandedHeightForContent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Require(actualHeight is > 200 and < 1100, "La altura expandida se adapta al contenido");
        double actualWidth = (double)typeof(IslandWindow).GetProperty("ExpandedWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.Width = actualWidth;
        window.FindControl<Border>("IslandSurface")!.Width = actualWidth;
        window.Height = actualHeight;
        window.FindControl<Border>("IslandSurface")!.Height = actualHeight;
        window.Measure(new Size(actualWidth, actualHeight));
        window.Arrange(new Rect(0, 0, actualWidth, actualHeight));
        Dispatcher.UIThread.RunJobs();
        var eye = window.FindControl<NoktoEyesControl>("EyesVisual")!;
        var shortcuts = window.FindControl<Button>("ExplorerButton")!;
        var viewport = window.FindControl<ScrollViewer>("PageScrollViewer")!;
        var mic = window.FindControl<StackPanel>("MicControl")!;
        var output = window.FindControl<StackPanel>("OutputControl")!;
        Require(ReferenceEquals(mic.Parent, output.Parent) && mic.Parent is Grid,
            "Micrófono y salida comparten una fila");
        var micPosition = mic.TranslatePoint(default, viewport);
        var outputPosition = output.TranslatePoint(default, viewport);
        Require(micPosition.HasValue && outputPosition.HasValue && micPosition.Value.X < outputPosition.Value.X &&
            Math.Abs(micPosition.Value.Y-outputPosition.Value.Y) < 1, "Micrófono a la izquierda y salida a la derecha");
        Require(shortcuts.HorizontalContentAlignment == HorizontalAlignment.Center &&
            window.FindControl<TextBlock>("BatteryStatusText")!.TextAlignment == Avalonia.Media.TextAlignment.Center,
            "Explorador y estado eléctrico están centrados");
        Require(window.FindControl<Button>("ExplorerButton")!.IsVisible && window.FindControl<Button>("AddFolderButton")!.IsVisible && shortcuts.IsEffectivelyVisible,
            "Explorador y carpetas efectivos al combinar accesos, audio y reproductor");
        var accessOrigin = shortcuts.TranslatePoint(default, viewport);
        Require(shortcuts.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Abrir Explorador" && t.Bounds.Width > 0),
            "La etiqueta del Explorador se renderiza después de reorganizar el módulo");
        var mediaOrigin = window.FindControl<Border>("MediaEmptyState")!.TranslatePoint(default, viewport);
        Require(accessOrigin.HasValue && mediaOrigin.HasValue && accessOrigin.Value.Y > mediaOrigin.Value.Y,
            "El Explorador aparece debajo del reproductor como eligió el usuario");
        var origin = eye.TranslatePoint(new Point(eye.Bounds.Width / 2, 0), window.FindControl<Border>("IslandSurface")!);
        Require(origin.HasValue && Math.Abs(origin.Value.X - actualWidth/2) < 1, "Ojos centrados en la isla expandida");
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(actualWidth), (int)Math.Ceiling(actualHeight)), new Vector(96, 96)))
        {
            bitmap.Render(window.FindControl<Border>("IslandSurface")!);
            bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-custom-pages.png"));
        }
        eye.Width = 42;
        eye.EyeCount = 1;
        var eyeHeader = window.FindControl<Grid>("IslandHeader")!;
        var clock = window.FindControl<TextBlock>("ClockText")!;
        clock.IsVisible = true;
        clock.Text = "15:24";
        eyeHeader.InvalidateMeasure();
        eyeHeader.Measure(new Size(308, 34));
        eyeHeader.Arrange(new Rect(0, 0, 308, 34));
        Require(Math.Abs(eye.Bounds.X + eye.Bounds.Width / 2 - eyeHeader.Bounds.Width / 2) < 1,
            "Un solo ojo permanece centrado aunque se muestre el reloj");
        eyeHeader.InvalidateMeasure();
        eyeHeader.Measure(new Size(162, 34));
        eyeHeader.Arrange(new Rect(0, 0, 162, 34));
        Require(Math.Abs(eye.Bounds.X + eye.Bounds.Width / 2 - eyeHeader.Bounds.Width / 2) < 1,
            "Un ojo permanece centrado en la cabecera compacta con reloj");
        var clockPosition = clock.TranslatePoint(new Point(0, 0), eyeHeader);
        Require(clockPosition.HasValue && clockPosition.Value.X >= eye.Bounds.Right,
            "El reloj no se superpone al ojo centrado");
        eye.Width = 74;
        eye.EyeCount = 2;
        eyeHeader.InvalidateMeasure();
        eyeHeader.Measure(new Size(194, 34));
        eyeHeader.Arrange(new Rect(0, 0, 194, 34));
        clockPosition = clock.TranslatePoint(new Point(0, 0), eyeHeader);
        Require(Math.Abs(eye.Bounds.X + eye.Bounds.Width / 2 - eyeHeader.Bounds.Width / 2) < 1 &&
            clockPosition.HasValue && clockPosition.Value.X >= eye.Bounds.Right,
            "Dos ojos permanecen centrados y separados del reloj");
        var list = window.FindControl<ItemsControl>("QuickFolderList")!;
        var buttons = list.GetVisualDescendants().OfType<Button>()
            .Where(b => b.CommandParameter is IslandViewModel.QuickFolderOption item && item.Path == folder).ToArray();
        Require(buttons.Length == 2 && buttons.Any(b => b.Content?.ToString() == "×"), "Cada carpeta tiene acción directa y X");
        buttons.Single(b => b.Content?.ToString() == "×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!model.QuickFolders.Any(f => f.Path == folder), "La X quita solo el acceso seleccionado");
        for (int i = 0; i < 8; i++)
        {
            string extra = Path.Combine(Path.GetDirectoryName(folder)!, "carpeta-extra-" + i);
            Directory.CreateDirectory(extra); model.AddQuickFolder(extra);
        }
        foreach (var feature in model.PageFeatures) model.SetFeatureInSlot(feature.Id, 0, true);
        Require(model.PageFeatures.All(f => model.FeatureInSlot(f.Id, 0)), "Todas las funciones se asignaron a la página de prueba");
        typeof(IslandWindow).GetMethod("RefreshPages", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        double longHeight = (double)typeof(IslandWindow).GetProperty("ExpandedHeightForContent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.Height = longHeight;
        double longWidth = (double)typeof(IslandWindow).GetProperty("ExpandedWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.Width = longWidth;
        window.FindControl<Border>("IslandSurface")!.Width = longWidth;
        window.FindControl<Border>("IslandSurface")!.Height = longHeight;
        window.InvalidateMeasure();
        window.Measure(new Size(longWidth, longHeight));
        window.Arrange(new Rect(0, 0, longWidth, longHeight));
        var surface = window.FindControl<Border>("IslandSurface")!;
        foreach (var element in surface.GetVisualDescendants().OfType<Layoutable>()) element.InvalidateMeasure();
        surface.InvalidateMeasure();
        surface.Measure(new Size(longWidth, longHeight));
        surface.Arrange(new Rect(0, 0, longWidth, longHeight));
        Dispatcher.UIThread.RunJobs();
        var scroll = window.FindControl<ScrollViewer>("PageScrollViewer")!;
        Console.WriteLine($"Página: ventana={longHeight:0.0}, extensión={scroll.Extent.Height:0.0}, área={scroll.Viewport.Height:0.0}, máximo={scroll.MaxHeight:0.0}, alto={scroll.Height:0.0}, bounds={scroll.Bounds.Height:0.0}");
        Console.WriteLine($"Secciones: resumen={window.FindControl<StackPanel>("OverviewPage")!.Bounds.Height:0.0}, sonido={window.FindControl<StackPanel>("AudioPage")!.Bounds.Height:0.0}, accesos={window.FindControl<StackPanel>("QuickPage")!.Bounds.Height:0.0}");
        Console.WriteLine($"Visible: clima={window.FindControl<Border>("WeatherPanel")!.IsVisible}, mic={window.FindControl<StackPanel>("MicControl")!.IsVisible}, rutinas={window.FindControl<StackPanel>("ProductivityPanel")!.IsVisible}, portapapeles={window.FindControl<StackPanel>("ClipboardPanel")!.IsVisible}");
        Require(scroll.Extent.Height <= scroll.Viewport.Height + 1 && scroll.Viewport.Height > 0,
            "Todos los contenidos caben sin desplazamiento vertical");
        scroll.Offset = new Vector(0, 100);
        Require(scroll.Offset.Y == 0, "El módulo no permite desplazamiento vertical");
        Require(longWidth > 332 && longHeight < 1200, "La combinación grande pasa a columnas dentro de la pantalla");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
