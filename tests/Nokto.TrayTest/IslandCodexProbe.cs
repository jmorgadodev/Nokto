using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows.Usage;
using Nokto.UI.Controls;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

internal static class IslandCodexProbe
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("[PASS] " + message); }
    private static void Field(object instance, string name, object value) => instance.GetType().GetField(name, Flags)!.SetValue(instance, value);
    private static void Call(IslandWindow window, string method, params object?[] args) => typeof(IslandWindow).GetMethod(method, Flags)!.Invoke(window, args);
    public static void Smoke()
    {
        var result = new CodexQuotaService().ReadAsync(default).GetAwaiter().GetResult();
        Check(result.Available, "Codex instalado entrega cuotas mediante el protocolo de solo lectura");
        Console.WriteLine($"Ventana corta: {result.Primary?.RemainingPercent:0.#}% disponible / {result.Primary?.WindowMinutes} min; semanal: {result.Weekly?.RemainingPercent:0.#}%. Sin datos de identidad ni credenciales.");
    }
    public static void Run()
    {
        ((Nokto.UI.App)Application.Current!).SetLanguage("es"); Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        using var document = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":27,"windowDurationMins":300,"resetsAt":1793372400},"secondary":{"usedPercent":100,"windowDurationMins":10080,"resetsAt":1793545200}}}}""");
        var quota = CodexUsageSnapshot.Parse(document.RootElement, now);
        Check(quota.Primary?.RemainingPercent == 73 && quota.Weekly?.RemainingPercent == 0 && quota.Primary.WindowMinutes == 300,
            "Se prefiere el bucket Codex y se convierte consumo en cuota restante sin confundir las ventanas");
        using var unknown = JsonDocument.Parse("""{"rateLimits":{"primary":null,"secondary":{"windowDurationMins":10080}}}""");
        Check(!CodexUsageSnapshot.Parse(unknown.RootElement, now).Available, "Datos ausentes no se presentan como cuota cero ni cien por ciento");
        var zone = TimeZoneInfo.CreateCustomTimeZone("QA Chile", TimeSpan.FromHours(-3), "QA Chile", "QA Chile");
        var exhausted = new CodexQuotaWindow(0, 300, now.AddHours(3));
        Check(exhausted.ResetLabel(now, zone).Contains("12:00") && exhausted.ResetLabel(now.AddHours(4), zone).Contains("Esperando") && exhausted.RemainingPercent == 0,
            "El restablecimiento usa hora local y nunca inventa una recarga al pasar la fecha");

        string root = Path.Combine(Path.GetTempPath(), "nokto-codex-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new PersistenceService(new StorageResolver(root)); var config = new AppConfig(); config.Island.EyeCount = 1;
        var model = new IslandViewModel(config, storage) { ReducedMotion = true, ShowSlot2 = false, ShowSlot3 = false, ShowMainButton = false, ShowHideButton = false };
        model.SelectStyle("CrimsonRing");
        foreach (var feature in model.PageFeatures) model.SetFeatureInSlot(feature.Id, 0, feature.Id == "codex");
        var reader = new FixtureReader();
        var island = new IslandWindow { DataContext = new { IslandVm = model }, ShowActivated = false, Position = new PixelPoint(-5000, -5000) };
        Field(island, "_settings", model); Field(island, "_animationTimer", new DispatcherTimer()); Field(island, "_codexQuotaReader", reader); Field(island, "_islandReady", true);
        island.ApplySettings(); Call(island, "SetExpanded", true); Dispatcher.UIThread.RunJobs();
        Check(reader.Calls == 1 && !island.FindControl<Grid>("IslandPageNav")!.IsVisible && island.FindControl<TextBlock>("PageTitle")!.Text == "",
            "Un único módulo muestra cuota realista sin fila de navegación ni contador 1/3");
        double singleHeight = island.Height;
        model.ShowSlot2 = true; Call(island, "RefreshPages");
        Check(island.FindControl<Grid>("IslandPageNav")!.IsVisible && island.FindControl<TextBlock>("PageTitle")!.Text!.EndsWith("/02"), "Con dos módulos el contador usa el total real");
        Check(Math.Abs(island.Height - singleHeight - 40) < 1, "Ocultar la navegación recupera su altura completa");
        model.ShowSlot2 = false; Call(island, "RefreshPages");
        var surface = island.FindControl<Border>("IslandSurface")!;
        // Adjuntar solo la presentación sintética, sin Opened/observadores de IslandWindow.
        surface.DataContext = island.DataContext; ((Grid)surface.Parent!).Children.Remove(surface);
        var preview = new Window { Content = surface, Width = island.Width, Height = island.Height, Position = new PixelPoint(-5000, -5000), ShowActivated = false, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None };
        preview.Show(); Dispatcher.UIThread.RunJobs();
        // Las plantillas Fluent ya están creadas: medir su tamaño efectivo como tras Opened en producción.
        Call(island, "ArrangeContentSections");
        double attachedHeight = (double)typeof(IslandWindow).GetProperty("ExpandedHeightForContent", Flags)!.GetValue(island)!;
        Call(island, "SetIslandDimensions", island.Width, attachedHeight); preview.Height = attachedHeight;
        surface.Measure(new Size(island.Width, island.Height)); surface.Arrange(new Rect(0, 0, island.Width, island.Height)); Dispatcher.UIThread.RunJobs();
        Check(surface.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Cuota de Codex" && t.Bounds.Width > 0), "El panel Codex se renderiza con sus etiquetas reales dentro de la isla");
        var status = (TextBlock)typeof(IslandWindow).GetField("_codexStatus", Flags)!.GetValue(island)!;
        var statusBottom = status.TranslatePoint(new Point(0, status.Bounds.Height), surface);
        Check(statusBottom.HasValue && statusBottom.Value.Y <= surface.Bounds.Height - 6, "La última línea del apartado Codex cabe completa, sin recorte inferior");
        var eye = island.FindControl<NoktoEyesControl>("EyesVisual")!;
        var header = island.FindControl<Grid>("IslandHeader")!;
        var center = eye.TranslatePoint(new Point(eye.Bounds.Width / 2, 0), header);
        Check(center.HasValue && Math.Abs(center.Value.X - header.Bounds.Width / 2) < 1 &&
            island.FindControl<TextBlock>("HeaderLeftText")!.IsVisible && island.FindControl<TextBlock>("HeaderRightText")!.IsVisible && !island.FindControl<TextBlock>("ClockText")!.IsVisible,
            "Cabecera útil a ambos lados con ojo centrado y sin duplicar el reloj");
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(island.Width), (int)Math.Ceiling(island.Height)), new Vector(96, 96)))
        { bitmap.Render(surface); bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", "qa-island-codex-quota.png")); }
        model.HeaderLeftIndex = 4; model.HeaderRightIndex = 4; island.ApplySettings();
        Check(!island.FindControl<TextBlock>("HeaderLeftText")!.IsVisible && !island.FindControl<TextBlock>("HeaderRightText")!.IsVisible && !island.FindControl<TextBlock>("ClockText")!.IsVisible,
            "Se pueden dejar ambos lados vacíos y conservar solo el ojo");
        var saved = new IslandViewModel(storage.LoadConfig(), storage);
        Check(saved.HeaderLeftMode == "None" && saved.HeaderRightMode == "None" && saved.FeatureInSlot("codex", 0), "Preferencias de cabecera y módulo Codex persisten");
        Check(reader.Calls == 1, "Cambios de presentación no repiten consultas dentro de la caché de cinco minutos");
        preview.Close(); Call(island, "DisposeCodexQuota"); Field(island, "_islandReady", false); Call(island, "DisposeMedia"); eye.StopActivation(); island.Close();
        VerifyDragOrdering(model, storage);
    }
    private static void VerifyDragOrdering(IslandViewModel model, PersistenceService storage)
    {
        var view = new IslandSettingsView { DataContext = new { IslandVm = model } };
        var host = new Window { Content = view, Width = 1200, Height = 4000, Position = new PixelPoint(-5000, -5000), ShowActivated = false, ShowInTaskbar = false };
        host.Show(); host.Measure(new Size(1200, 4000)); host.Arrange(new Rect(0, 0, 1200, 4000)); Dispatcher.UIThread.RunJobs();
        var matrix = view.FindControl<ItemsControl>("PageFeatureMatrix")!;
        var source = model.PageFeatures.Single(f => f.Id == "volume");
        Border Target() => matrix.GetVisualDescendants().OfType<Border>().Single(b => !b.Focusable && b.Tag?.ToString() == "0" && DragDrop.GetAllowDrop(b) && b.DataContext is IslandPageFeature { Id: "weather" });
        var target = Target();
        var otherOrder = model.Slot2Features.Select(f => f.Id).ToArray();
        Field(view, "_draggedContent", source); Field(view, "_draggedSlot", 0);
        var over = new DragEventArgs(DragDrop.DragOverEvent, new DataObject(), target, new Point(10, Math.Max(1, target.Bounds.Height - 1)), KeyModifiers.None);
        target.RaiseEvent(over);
        Check(over.DragEffects == DragDropEffects.Move, "La fila acepta el arrastre local y presenta el punto de inserción");
        target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, new DataObject(), target, new Point(10, Math.Max(1, target.Bounds.Height - 1)), KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        var order = model.Slot1Features.Select(f => f.Id).ToList();
        Check(order.IndexOf("volume") == order.IndexOf("weather") + 1 && model.Slot2Features.Select(f => f.Id).SequenceEqual(otherOrder),
            "Soltar en la fila visible mueve el contenido al lugar indicado y conserva los otros módulos");
        Check(new IslandViewModel(storage.LoadConfig(), storage).Slot1Features.Select(f => f.Id).SequenceEqual(order), "El orden por arrastre se conserva al volver a cargar");
        Check(!matrix.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() is "↑" or "↓"), "Se quitaron los botones repetidos de subir y bajar");
        target = Target();
        Field(view, "_draggedContent", null!); target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, new DataObject(), target, new Point(10, 1), KeyModifiers.None));
        Check(model.Slot1Features.Select(f => f.Id).SequenceEqual(order), "Un arrastre externo no altera el orden");
        host.Close();
    }
    private sealed class FixtureReader : ICodexUsageReader
    {
        public int Calls;
        public Task<CodexUsageSnapshot> ReadAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(new CodexUsageSnapshot(new(73, 300, DateTimeOffset.UtcNow.AddHours(2)), new(0, 10080, DateTimeOffset.UtcNow.AddDays(2)), DateTimeOffset.UtcNow));
        }
    }
}
