using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Pointer = Avalonia.Input.Pointer;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.ConsoleTest;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.UI.Controls;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

internal static class IslandInteractionSafetyProbe
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(IslandWindow window, string name, object value) => typeof(IslandWindow).GetField(name, Private)!.SetValue(window, value);
    private static void Call(IslandWindow window, string name, params object?[] args) => typeof(IslandWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); Console.WriteLine("[PASS] " + label); }

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "nokto-island-interaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var persistence = new PersistenceService(new StorageResolver(root));
        var config = new AppConfig();
        var model = new IslandViewModel(config, persistence) { ReducedMotion = true };
        using var adapter = new WorkflowTestAdapter();
        var scene = new AudioScene(model, adapter);
        var window = new IslandWindow { DataContext = scene, ShowActivated = false, Position = new PixelPoint(-12000, -12000) };
        var ordinary = new Window { Width = 100, Height = 100, ShowActivated = false, Position = new PixelPoint(-12000, -12000) };
        Window? settingsHost = null;
        try
        {
            Check(model.SelectedStyle == "StaticDot" && model.StaticDotColor == "#74E5A3", "Nueva configuración comienza con punto verde estático.");
            model.AddAudioToSlot(0);
            Set(window, "_settings", model);
            Set(window, "_animationTimer", new DispatcherTimer());
            foreach (string timer in new[] { "_cursorTimer", "_blinkTimer", "_eyeFrameTimer", "_demoTimer" }) Set(window, timer, new DispatcherTimer());
            window.ApplySettings();
            Call(window, "SetExpanded", true);
            window.Show(); ordinary.Show(); Pump();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height)); Pump();
            var slider = window.FindControl<Slider>("VolumeSlider")!;
            slider.ApplyTemplate(); Pump();
            var track = slider.GetVisualDescendants().OfType<Track>().Single();
            Check(track.Thumb is not null && track.Bounds.Width > 40, "La plantilla conecta un Thumb real con espacio para arrastrarlo.");
            var thumb = track.Thumb!;
            var pointer = new Pointer(99, PointerType.Mouse, true);
            var press = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var move = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var release = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            double initial = slider.Value;
            Point start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            thumb.RaiseEvent(new PointerPressedEventArgs(thumb, pointer, window, start, 1, press, KeyModifiers.None));
            Point end = start + new Vector(35, 0);
            thumb.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, thumb, pointer, window, end, 2, move, KeyModifiers.None));
            thumb.RaiseEvent(new PointerReleasedEventArgs(thumb, pointer, window, end, 3, release, KeyModifiers.None, MouseButton.Left));
            Pump();
            Check(slider.Value > initial && Math.Abs(adapter.Volume * 100 - slider.Value) < .1,
                "Arrastre con eventos reales de puntero cambia Track, binding y adaptador falso.");
            int page = (int)typeof(IslandWindow).GetField("_pageIndex", Private)!.GetValue(window)!;
            slider.SetCurrentValue(Slider.ValueProperty, 50d); Pump();
            var sections = (Dictionary<string, Control>)typeof(IslandWindow).GetField("_sections", Private)!.GetValue(window)!;
            var card = sections["volume"];
            card.RaiseEvent(new PointerWheelEventArgs(card, pointer, window, default, 4, new(), KeyModifiers.None, new Vector(0, 1)));
            Pump();
            Check(slider.Value == 55 && Math.Abs(adapter.Volume - .55f) < .001 &&
                (int)typeof(IslandWindow).GetField("_pageIndex", Private)!.GetValue(window)! == page,
                "La rueda sobre la tarjeta ajusta 5 puntos y conserva la página.");
            slider.RaiseEvent(new PointerWheelEventArgs(slider, pointer, window, default, 5, new(), KeyModifiers.None, new Vector(0, -1)));
            Pump();
            Check(slider.Value == 50, "La rueda sobre el slider se procesa una sola vez.");
            slider.SetCurrentValue(Slider.ValueProperty, 99d);
            card.RaiseEvent(new PointerWheelEventArgs(card, pointer, window, default, 6, new(), KeyModifiers.None, new Vector(0, 1)));
            Check(slider.Value == 100, "La rueda respeta el límite de volumen.");

            var settingsView = new IslandSettingsView { DataContext = scene };
            settingsHost = new Window { Content = settingsView, Width = 1100, Height = 2000 };
            settingsHost.Measure(new Size(1100, 2000)); settingsHost.Arrange(new Rect(0, 0, 1100, 2000)); Pump();
            settingsView.FindControl<ComboBox>("HeaderLeftChoice")!.SelectedIndex = 2;
            settingsView.FindControl<ComboBox>("HeaderRightChoice")!.SelectedIndex = 1;
            settingsView.FindControl<TextBox>("StaticDotColorInput")!.Text = "#FF8844"; Pump();
            var restored = persistence.LoadConfig();
            Check(restored.Island.HeaderLeftMode == "Timer" && restored.Island.HeaderRightMode == "Clock" && restored.Island.StaticDotColor == "#FF8844",
                "Selectores reales de cabecera y color guardan sus elecciones.");
            model.Productivity.Engine.StartTimer(5, DateTimeOffset.UtcNow);
            window.ApplySettings(); Call(window, "UpdateContextHeader");
            Check(window.FindControl<TextBlock>("HeaderLeftText")!.IsVisible && window.FindControl<TextBlock>("HeaderLeftText")!.Text!.StartsWith("Quedan") &&
                window.FindControl<TextBlock>("ClockText")!.IsVisible, "La cabecera expandida refleja temporizador a la izquierda y reloj a la derecha.");
            Call(window, "SetExpanded", false); Pump();
            Check(window.FindControl<TextBlock>("HeaderLeftText")!.IsVisible && window.FindControl<TextBlock>("HeaderLeftText")!.Opacity == 1 &&
                window.FindControl<TextBlock>("ClockText")!.IsVisible && window.Width >= 320,
                "La selección explícita también aparece en compacto con espacio a ambos lados.");
            Call(window, "SetExpanded", true);
            settingsView.FindControl<ComboBox>("HeaderLeftChoice")!.SelectedIndex = 4;
            settingsView.FindControl<ComboBox>("HeaderRightChoice")!.SelectedIndex = 4; Pump();
            window.ApplySettings(); Call(window, "UpdateContextHeader");
            Check(!window.FindControl<TextBlock>("HeaderLeftText")!.IsVisible && !window.FindControl<TextBlock>("HeaderRightText")!.IsVisible &&
                !window.FindControl<TextBlock>("ClockText")!.IsVisible, "Nada oculta cada lado de la cabecera.");
            Set(window, "_islandReady", true); Call(window, "ConfigureEyeMotion");
            var eyes = window.FindControl<NoktoEyesControl>("EyesVisual")!;
            eyes.PlayActivation();
            Check(new[] { "_cursorTimer", "_blinkTimer", "_eyeFrameTimer", "_demoTimer" }.All(name =>
                !((DispatcherTimer)typeof(IslandWindow).GetField(name, Private)!.GetValue(window)!).IsEnabled) &&
                typeof(NoktoEyesControl).GetField("_activationTimer", Private)!.GetValue(eyes) is null,
                "Punto estático no inicia seguimiento, parpadeo, demo ni animación de activación.");
            var preview = new NoktoEyesControl { Width = 60, Height = 60, StyleId = "StaticDot", StaticDotColor = "#FF8844" };
            preview.Measure(new Size(60, 60)); preview.Arrange(new Rect(0, 0, 60, 60));
            byte[] before = Render(preview);
            preview.SetPose(1, -1, 1, true); preview.PlayActivation();
            byte[] after = Render(preview);
            using var decoded = SkiaSharp.SKBitmap.Decode(before);
            var pixel = decoded.GetPixel(30, 30);
            Check(pixel.Red == 255 && pixel.Green == 136 && pixel.Blue == 68 && before.SequenceEqual(after),
                "El color se renderiza y el punto no se mueve ni parpadea ante cambios de pose.");
            var classic = new NoktoEyesControl { Width = 60, Height = 60, StyleId = "Eyes", StaticDotColor = "#FF8844" };
            classic.Measure(new Size(60, 60)); classic.Arrange(new Rect(0, 0, 60, 60));
            byte[] originalEye = Render(classic); classic.StaticDotColor = "#0099FF";
            Check(originalEye.SequenceEqual(Render(classic)), "El color configurable no altera otros estilos.");
            nint islandStyle = GetWindowLongPtr(window.TryGetPlatformHandle()!.Handle, -20);
            nint ordinaryStyle = GetWindowLongPtr(ordinary.TryGetPlatformHandle()!.Handle, -20);
            Check((islandStyle.ToInt64() & 0x80) != 0 && (islandStyle.ToInt64() & 0x40000) == 0 &&
                (ordinaryStyle.ToInt64() & 0x80) == 0,
                "La isla usa TOOLWINDOW sin APPWINDOW; una ventana normal conserva su presencia en Alt+Tab.");
        }
        finally
        {
            Set(window, "_islandReady", false);
            foreach (string name in new[] { "_animationTimer", "_cursorTimer", "_blinkTimer", "_eyeFrameTimer", "_demoTimer", "_outsideTimer" })
                (typeof(IslandWindow).GetField(name, Private)!.GetValue(window) as DispatcherTimer)?.Stop();
            model.Productivity.Dispose(); settingsHost?.Close(); ordinary.Close(); window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Render(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(60, 60), new Vector(96, 96));
        bitmap.Render(control); using var memory = new MemoryStream(); bitmap.Save(memory); return memory.ToArray();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    private sealed class AudioScene(IslandViewModel model, WorkflowTestAdapter adapter) : INotifyPropertyChanged
    {
        public IslandViewModel IslandVm => model;
        public bool HasOutputAudio => true;
        public bool HasInputAudio => true;
        public bool IsInputMuted => false;
        public bool IsOutputMuted => false;
        public string IslandInputState => "Activo";
        public string IslandOutputState => "Activa";
        public string MasterVolumeText => $"{MasterVolumePercent:0}%";
        public double MasterVolumePercent
        {
            get => adapter.Volume * 100;
            set
            {
                adapter.SetMasterVolumeAsync((float)value / 100).GetAwaiter().GetResult();
                PropertyChanged?.Invoke(this, new(nameof(MasterVolumePercent)));
                PropertyChanged?.Invoke(this, new(nameof(MasterVolumeText)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
