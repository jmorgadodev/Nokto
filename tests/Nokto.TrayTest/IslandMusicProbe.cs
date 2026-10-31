using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;
using Nokto.Platform.Windows.Notifications;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;
using System.Reflection;

internal static class IslandMusicProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("[FAIL] " + message); Console.WriteLine("[PASS] " + message); }
    private static void Call(IslandWindow window, string method, params object?[] args) => typeof(IslandWindow).GetMethod(method, Flags)!.Invoke(window, args);
    private static void Set(IslandWindow window, string name, object value) => typeof(IslandWindow).GetField(name, Flags)!.SetValue(window, value);

    public static void Run()
    {
        // Ventanas separadas de la app: no se abren servicios Windows ni se usa audio real.
        var lyrics = new IslandLyricsWindow(reducedMotion: true);
        lyrics.Measure(new Size(340, 440)); lyrics.Arrange(new Rect(0, 0, 340, 440)); Dispatcher.UIThread.RunJobs();
        var sample = new SongLyrics(new[] { new LyricLine(TimeSpan.FromSeconds(1), "Una línea de prueba"), new LyricLine(TimeSpan.FromSeconds(3), "La siguiente frase"), new LyricLine(TimeSpan.FromSeconds(5), "Última línea sintética") }, null, false);
        lyrics.Loading("Canción de prueba · Artista"); lyrics.SetLyrics(sample, canSeek: true);
        lyrics.UpdatePosition(TimeSpan.FromSeconds(3.5));
        var lines = (StackPanel)typeof(IslandLyricsWindow).GetField("_lines", Flags)!.GetValue(lyrics)!;
        var active = lines.Children.OfType<Button>().Single(b => ((TextBlock)b.Content!).FontSize == 15);
        Check(((TextBlock)active.Content!).Text == "La siguiente frase", "Letra ilumina la línea correspondiente al tiempo simulado");
        TimeSpan? sought = null; lyrics.SeekRequested += time => sought = time;
        active.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(sought == TimeSpan.FromSeconds(3), "Clic en letra comunica el instante exacto, sin controlar un reproductor real");
        Capture((Control)lyrics.Content!, "qa-island-lyrics.png", 340, lyrics.Height);
        lyrics.SetLyrics(sample, canSeek: false);
        Check(lines.Children.OfType<Button>().All(button => !button.IsEnabled), "Letras consultables con salto deshabilitado si el reproductor no lo permite");
        lyrics.SetLyrics(new SongLyrics([], null, true), false);
        Check(((TextBlock)typeof(IslandLyricsWindow).GetField("_status", Flags)!.GetValue(lyrics)!).Text == "Pista instrumental", "Estado instrumental explícito");
        int retries = 0; lyrics.RetryRequested += () => retries++;
        var retry = (Button)typeof(IslandLyricsWindow).GetField("_retry", Flags)!.GetValue(lyrics)!;
        var message = (string)typeof(IslandWindow).GetMethod("LyricsErrorMessage", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new HttpRequestException("Prueba", null, System.Net.HttpStatusCode.ServiceUnavailable)])!;
        lyrics.Error(message);
        Check(retry.IsVisible && message.Contains("temporalmente") && !message.Contains("conexión"), "Error 503 se presenta como fallo temporal del servicio con opción de reintentar");
        Capture((Control)lyrics.Content!, "qa-island-lyrics-retry.png", 340, lyrics.Height);
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(retries == 1, "Reintentar letra solicita otra consulta sin controlar Spotify ni capturar audio");
        lyrics.Loading("Pista sintética");
        Check(!retry.IsVisible, "El botón de reintento desaparece mientras busca");
        lyrics.Close();

        var recognition = new SongRecognitionView();
        var recognize = (Button)typeof(SongRecognitionView).GetField("_recognize", Flags)!.GetValue(recognition)!;
        var microphone = (Button)typeof(SongRecognitionView).GetField("_microphone", Flags)!.GetValue(recognition)!;
        var computer = (Button)typeof(SongRecognitionView).GetField("_computer", Flags)!.GetValue(recognition)!;
        Check(recognize.IsEnabled && microphone.IsEnabled && computer.IsEnabled && typeof(SongRecognitionView).GetField("_request", Flags)!.GetValue(recognition) is null,
            "Reconocimiento integrado ofrece ambas fuentes y abrirlo no captura");
        Capture((Control)recognition.Content!, "qa-island-recognition.png", 308, 510);
        Check(!recognition.GetVisualDescendants().Any(c => c is ComboBox or TextBox), "Reconocimiento no ofrece proveedor ni entrada de claves");
        recognition.Dispose();

        string root = Path.Combine(Path.GetTempPath(), "nokto-notice-probe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var model = new IslandViewModel(new AppConfig(), new PersistenceService(new StorageResolver(root))) { ReducedMotion = true };
        var window = new IslandWindow(); Set(window, "_settings", model); Set(window, "_islandReady", true);
        Set(window, "_animationTimer", new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) });
        window.ApplySettings();
        // ApplySettings no registra observadores; se inyecta únicamente la presentación del aviso.
        Call(window, "ShowSystemNotice", new IslandSystemNotice("volume", "Volumen", "Salida de prueba", 42));
        Check(window.FindControl<Border>("HudPanel")!.IsVisible && window.FindControl<TextBlock>("HudValue")!.Text == "42%" && !window.FindControl<StackPanel>("ExpandedTools")!.IsVisible,
            "Aviso compacto presenta nivel sin desplegar todos los módulos");
        Capture(window.FindControl<Border>("IslandSurface")!, "qa-island-volume-hud.png", window.Width, window.Height);
        Call(window, "OnNoticeExpired", null, EventArgs.Empty);
        Check(!window.FindControl<Border>("HudPanel")!.IsVisible && !(bool)typeof(IslandWindow).GetField("_noticeOnly", Flags)!.GetValue(window)!, "Aviso caduca y vuelve al ojo incluso con movimiento reducido");
        model.ShowSystemOverlays = false;
        Call(window, "ShowSystemNotice", new IslandSystemNotice("volume", "Ignorado", "", 80));
        Check(!window.FindControl<Border>("HudPanel")!.IsVisible, "Preferencia de avisos desactivados se respeta");
        model.PrivacyMode = true;
        Call(window, "ShowSystemNotice", new IslandSystemNotice("connection", "Dispositivo privado", "Nombre"));
        Check(!window.FindControl<Border>("HudPanel")!.IsVisible, "Modo privado oculta nombres de dispositivos y conexión");
        model.PrivacyMode = false; model.ShowSystemOverlays = true; model.ReducedMotion = false;
        Call(window, "ShowSystemNotice", new IslandSystemNotice("volume", "Volumen", "Prueba animada", 55));
        Set(window, "_animationStart", System.Diagnostics.Stopwatch.GetTimestamp() - (long)(.07 * System.Diagnostics.Stopwatch.Frequency));
        Call(window, "AdvanceAnimation", true);
        double opacity = window.FindControl<Border>("IslandSurface")!.Background!.Opacity;
        Check(opacity > 0 && opacity < 1, "HUD usa la apertura gradual de la isla, sin aparición instantánea");
        Call(window, "DisposeSystemNotices");
        Set(window, "_animationStart", System.Diagnostics.Stopwatch.GetTimestamp() - 2 * System.Diagnostics.Stopwatch.Frequency);
        Call(window, "AdvanceAnimation", true);
        Check(!window.FindControl<Border>("HudPanel")!.IsVisible && typeof(IslandWindow).GetField("_systemObserver", Flags)!.GetValue(window) is null,
            "Desactivar avisos limpia el HUD y la prueba no abrió observadores de hardware");
        Call(window, "DisposeSystemNotices"); Set(window, "_islandReady", false); Call(window, "DisposeMedia");
        window.FindControl<Nokto.UI.Controls.NoktoEyesControl>("EyesVisual")!.StopActivation(); window.Close();
    }

    private static void Capture(Control control, string name, double width, double height)
    {
        control.Measure(new Size(width, double.PositiveInfinity));
        if (name == "qa-island-recognition.png") height = control.DesiredSize.Height;
        control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width), (int)Math.Ceiling(height)), new Vector(96, 96));
        bitmap.Render(control); bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", name));
    }
}
