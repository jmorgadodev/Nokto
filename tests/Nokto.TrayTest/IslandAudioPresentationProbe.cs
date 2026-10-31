using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows.Notifications;
using Nokto.UI.Controls;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;
using System.Reflection;

internal static class IslandAudioPresentationProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Field(IslandWindow w, string key, object value) => typeof(IslandWindow).GetField(key, Flags)!.SetValue(w, value);
    private static void Call(IslandWindow w, string key, params object?[] args) => typeof(IslandWindow).GetMethod(key, Flags)!.Invoke(w, args);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("[PASS] " + message); }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "nokto-audio-presentation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var model = new IslandViewModel(new AppConfig(), new PersistenceService(new StorageResolver(root))) { ReducedMotion = true };
        model.AddAudioToSlot(0);
        var window = new IslandWindow { DataContext = new
        {
            IslandVm = model, IsInputMuted = true, IsOutputMuted = false, HasInputAudio = true, HasOutputAudio = true,
            IslandInputState = "Silenciado", IslandOutputState = "Activa", MasterVolumeText = "42%", MasterVolumePercent = 42d
        } };
        Field(window, "_settings", model); Field(window, "_islandReady", true);
        Field(window, "_animationTimer", new DispatcherTimer()); window.ApplySettings(); Call(window, "SetExpanded", true);
        double width = window.Width, height = window.Height;
        string page = window.FindControl<TextBlock>("PageTitle")!.Text!;
        foreach (var notice in new[] { new IslandSystemNotice("volume", "Volumen", "", 50), new IslandSystemNotice("volume", "Sonido silenciado", "", 0), new IslandSystemNotice("keys", "Teclado", "Prueba") }) Call(window, "ShowSystemNotice", notice);
        Check(window.Width == width && window.Height == height && !window.FindControl<Border>("HudPanel")!.IsVisible && window.FindControl<TextBlock>("PageTitle")!.Text == page,
            "Volumen y silencio no añaden filas, cambian de página ni redimensionan el menú abierto");
        Call(window, "OnAudioInput", null, new RoutedEventArgs(InputElement.KeyDownEvent, window.FindControl<Grid>("VolumeRow")));
        Call(window, "SetExpanded", false);
        Call(window, "ShowSystemNotice", new IslandSystemNotice("volume", "Evento tardío", "", 70));
        Check(!(bool)typeof(IslandWindow).GetField("_expanded", Flags)!.GetValue(window)! && !window.FindControl<Border>("HudPanel")!.IsVisible,
            "Un evento tardío de los controles no reabre la isla al salir");
        Field(window, "_menuFeedbackUntil", DateTimeOffset.MinValue);
        Call(window, "ShowSystemNotice", new IslandSystemNotice("volume", "Evento propio", "", 70));
        Check(!window.FindControl<Border>("HudPanel")!.IsVisible, "La protección del audio local persiste tras finalizar el cierre");
        Field(window, "_audioFeedbackUntil", DateTimeOffset.MinValue);
        Call(window, "ShowSystemNotice", new IslandSystemNotice("volume", "Volumen externo", "", 70));
        Check(window.FindControl<Border>("HudPanel")!.IsVisible, "Los cambios externos siguen mostrando un vistazo cuando la isla está compacta");
        Call(window, "PromoteNoticeToMenu");
        Check(!window.FindControl<Border>("HudPanel")!.IsVisible && window.FindControl<StackPanel>("ExpandedTools")!.IsVisible,
            "Entrar en un vistazo abre un menú limpio sin duplicar el aviso");

        var sections = (Dictionary<string, Control>)typeof(IslandWindow).GetField("_sections", Flags)!.GetValue(window)!;
        var card = sections["audioPair"];
        card.Measure(new Size(308, 200)); card.Arrange(new Rect(0, 0, 308, card.DesiredSize.Height)); Dispatcher.UIThread.RunJobs();
        // Un segundo layout permite medir la plantilla de los botones creada por Avalonia.
        card.InvalidateMeasure(); card.Measure(new Size(308, 200)); card.Arrange(new Rect(0, 0, 308, card.DesiredSize.Height));
        var mic = window.FindControl<IslandAudioButton>("MicMuteButton")!;
        var output = window.FindControl<IslandAudioButton>("OutputMuteButton")!;
        Check(mic.IsMuted && !output.IsMuted && mic.Bounds.Size == new Size(48, 48) && output.Bounds.Size == new Size(48, 48),
            "Botones de 48 px, estados enlazados y plantilla propia independiente del tema genérico");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(308, (int)Math.Ceiling(card.Bounds.Height)), new Vector(96, 96)))
        {
            bitmap.Render(card); bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-audio-controls.png"));
        }
        mic.SetCurrentValue(IslandAudioButton.IsMutedProperty, false); output.SetCurrentValue(IslandAudioButton.IsMutedProperty, true);
        Check(mic.Width == 48 && mic.Height == 48 && output.Width == 48 && output.Height == 48, "Cambiar silencio conserva la geometría de ambos controles");
        Call(window, "DisposeSystemNotices"); Field(window, "_islandReady", false); Call(window, "DisposeMedia");
        window.FindControl<NoktoEyesControl>("EyesVisual")!.StopActivation(); window.Close();
    }
}
