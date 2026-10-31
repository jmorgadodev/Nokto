using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows.Notifications;
using Nokto.UI.Controls;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

internal static class IslandNoticesProbe
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("[PASS] " + message); }
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static void Call(IslandWindow target, string method, params object?[] args) => typeof(IslandWindow).GetMethod(method, Flags)!.Invoke(target, args);
    public static void Run()
    {
        ((Nokto.UI.App)Application.Current!).SetLanguage("es"); Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        string root = Path.Combine(Path.GetTempPath(), "nokto-notice-choices-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new PersistenceService(new StorageResolver(root));
        var config = new AppConfig(); config.Island.ShowConnectionNotices = false;
        var model = new IslandViewModel(config, storage) { ReducedMotion = true };
        Check(model.IsNoticeEnabled("volume") && model.IsNoticeEnabled("capsLock") && !model.IsNoticeEnabled("network") &&
            model.NoticeOptions.Where(x => x.Group == "extra").All(x => !x.IsEnabled), "Migración conserva grupos previos y deja avisos nuevos apagados");
        var view = new IslandSettingsView { DataContext = new { IslandVm = model } };
        var settingsHost = new Window { Content = view, Width = 1200, Height = 5200, Position = new PixelPoint(-5000, -5000), ShowActivated = false, ShowInTaskbar = false };
        settingsHost.Show(); settingsHost.Measure(new Size(1200, 5200)); settingsHost.Arrange(new Rect(0, 0, 1200, 5200)); Dispatcher.UIThread.RunJobs();
        var choices = view.FindControl<ItemsControl>("IslandNoticeChoices")!;
        var boxes = choices.GetVisualDescendants().OfType<CheckBox>().ToArray();
        Check(boxes.Length == 13 && boxes.All(b => b.DataContext is IslandNoticeOption), "Ajustes muestra trece opciones reales de avisos");
        var bluetooth = boxes.Single(b => ((IslandNoticeOption)b.DataContext!).Id == "bluetooth");
        bluetooth.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Dispatcher.UIThread.RunJobs();
        var restored = new IslandViewModel(storage.LoadConfig(), storage);
        Check(restored.IsNoticeEnabled("bluetooth") && !restored.IsNoticeEnabled("storage") && !restored.IsNoticeEnabled("network"), "Casilla Bluetooth guarda su estado sin activar otras categorías");
        Check(model.NoticeOptions.All(x => x.Available) &&
            !view.GetVisualDescendants().OfType<Expander>().Any(x => x.Header?.ToString()?.Contains("pendientes", StringComparison.OrdinalIgnoreCase) == true),
            "Ajustes solo muestra avisos implementados, sin integraciones pendientes");
        Check(!view.GetVisualDescendants().OfType<TextBox>().Any(b => b.Watermark?.Contains("API", StringComparison.OrdinalIgnoreCase) == true), "Ajustes no permite introducir claves de reconocimiento");
        var noticeCard = choices.GetVisualAncestors().OfType<Border>().First();
        noticeCard.Measure(new Size(1100, double.PositiveInfinity)); noticeCard.Arrange(new Rect(0, 0, 1100, noticeCard.DesiredSize.Height)); Dispatcher.UIThread.RunJobs();
        using (var image = new RenderTargetBitmap(new PixelSize(1100, (int)Math.Ceiling(noticeCard.Bounds.Height)), new Vector(96, 96)))
        { image.Render(noticeCard); image.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-notice-settings.png")); }
        settingsHost.Close();

        using (var empty = new IslandSystemObserver([]))
        {
            empty.Start();
            Check(new[] { "_keys", "_brightness", "_storage", "_enumerator" }.All(name => typeof(IslandSystemObserver).GetField(name, Flags)!.GetValue(empty) is null),
                "Sin opciones no se abren temporizadores, WMI ni audio");
        }
        var battery = new IslandBatteryMilestones();
        Check(battery.Observe(21, false) is null && battery.Observe(20, false)?.Level == 20 && battery.Observe(19, false) is null &&
            battery.Observe(10, false)?.Level == 10 && battery.Observe(9, false) is null, "Batería avisa al cruzar umbrales sin repetir cada porcentaje");
        Check(battery.Observe(99, true) is null && battery.Observe(100, true)?.Title == "Batería cargada" &&
            battery.Observe(99, true) is null && battery.Observe(100, true) is null, "Carga completa tiene histéresis para evitar avisos repetidos");
        var device = new IslandConnectionTransitions();
        Check(device.Observe("fixture", true, false) is null && device.Observe("fixture", true, true) is null &&
            device.Observe("fixture", false, true) == false && device.Observe("fixture", false, true) is null &&
            device.Observe("fixture", true, true) == true && device.Remove("fixture", true) == false && device.Remove("fixture", true) is null,
            "Bluetooth establece base inicial y emite solo conexiones/desconexiones reales");

        foreach (var option in model.AvailableNoticeOptions) option.IsEnabled = true;
        var island = new IslandWindow { DataContext = new { IslandVm = model } };
        Field(island, "_settings", model); Field(island, "_animationTimer", new DispatcherTimer()); Field(island, "_islandReady", true);
        island.ApplySettings();
        foreach (var definition in IslandNoticeCatalog.All.Where(x => x.Available))
        {
            Call(island, "ShowSystemNotice", new IslandSystemNotice(definition.Id, definition.Title, "Estado de prueba", definition.Id is "volume" or "brightness" or "battery" ? 42 : null));
            Check(island.FindControl<Border>("HudPanel")!.IsVisible && island.FindControl<TextBlock>("HudTitle")!.Text == definition.Title,
                "Vistazo disponible: " + definition.Title);
            Call(island, "OnNoticeExpired", null, EventArgs.Empty);
        }
        model.NoticeOptions.Single(x => x.Id == "bluetooth").IsEnabled = false;
        Call(island, "ShowSystemNotice", new IslandSystemNotice("bluetooth", "No debe mostrarse", ""));
        Check(!island.FindControl<Border>("HudPanel")!.IsVisible, "Desactivar una opción bloquea su aviso inmediatamente");
        model.PrivacyMode = true;
        Call(island, "ShowSystemNotice", new IslandSystemNotice("storage", "Unidad privada", "X:"));
        Check(!island.FindControl<Border>("HudPanel")!.IsVisible, "Modo privado protege nombres de dispositivos y unidades");
        Call(island, "DisposeSystemNotices"); Field(island, "_islandReady", false);
        Call(island, "DisposeMedia"); island.FindControl<NoktoEyesControl>("EyesVisual")!.StopActivation(); island.Close();
    }
}
