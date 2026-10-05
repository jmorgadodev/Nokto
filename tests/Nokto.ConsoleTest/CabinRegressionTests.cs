using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Serialization;
using Nokto.Core.Services;
using Nokto.Platform.Windows;

namespace Nokto.ConsoleTest;

internal static class CabinRegressionTests
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nokto_cabin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string[] names = ["ShowNetworkCardInHome", "ShowEnergyStatusCardInHome", "ShowHardwareCardInHome",
                "ShowAiRadarCardInHome", "ShowQuickActionsInHome", "ShowAudioControlCardInHome"];
            var properties = names.Select(name => typeof(AppSettings).GetProperty(name)
                ?? throw new InvalidOperationException($"Falta la opción de cabina {name}.")).ToArray();
            var legacy = JsonSerializer.Deserialize("""{"settings":{"language":"en"}}""", NoktoJsonContext.Default.AppConfig)!;
            Require(properties.All(p => Equals(p.GetValue(legacy.Settings), true)),
                "Una configuración anterior debe mostrar las seis tarjetas por defecto.");
            Require(legacy.Settings.AiEnvironmentOrder.SequenceEqual(new[] { "codex" }),
                "Una configuración anterior debe dar prioridad inicial a Codex sin alterar la selección.");
            var ordered = JsonSerializer.Deserialize("""{"settings":{"aiEnvironmentOrder":["antigravity","codex"],"hiddenAiEnvironmentIds":["ollama"]}}""", NoktoJsonContext.Default.AppConfig)!;
            Require(ordered.Settings.AiEnvironmentOrder.SequenceEqual(new[] { "antigravity", "codex" }) && ordered.Settings.HiddenAiEnvironmentIds.Contains("ollama"),
                "El contexto JSON conserva el orden elegido y los accesos ocultos de forma independiente.");
            var hiddenRadar = JsonSerializer.Deserialize("""{"settings":{"showAiRadarInHome":false}}""", NoktoJsonContext.Default.AppConfig)!;
            Require(Equals(properties[3].GetValue(hiddenRadar.Settings), false),
                "La preferencia anterior de ocultar el Radar debe conservarse.");
            var persistence = new PersistenceService(new StorageResolver(directory));
            var config = new AppConfig { Settings = new AppSettings { Theme = "Light", StartInWorkMode = true } };
            for (int mask = 0; mask < 1 << properties.Length; mask++)
            {
                for (int i = 0; i < properties.Length; i++) properties[i].SetValue(config.Settings, (mask & (1 << i)) != 0);
                persistence.SaveConfig(config);
                var restored = persistence.LoadConfig();
                for (int i = 0; i < properties.Length; i++)
                    Require(Equals(properties[i].GetValue(restored.Settings), (mask & (1 << i)) != 0),
                        $"La visibilidad de {names[i]} debe persistir independientemente (combinación {mask}).");
                Require(restored.Settings.Theme == "Light" && restored.Settings.StartInWorkMode,
                    "Cambiar la cabina no debe alterar el tema ni el arranque en modo trabajo.");
            }
            Console.WriteLine("[PASS] Cabina: valores por defecto, compatibilidad del Radar y 64 combinaciones persistidas.");
            int saves = 0;
            var visibility = new HomeDashboardSettings(config.Settings, () => { saves++; persistence.SaveConfig(config); });
            var changed = new List<string?>();
            visibility.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            visibility.ShowNetworkCardInHome = false;
            visibility.ShowEnergyStatusCardInHome = false;
            visibility.ShowHardwareCardInHome = false;
            visibility.ShowAiRadarCardInHome = false;
            visibility.ShowQuickActionsInHome = false;
            visibility.ShowAudioControlCardInHome = false;
            Require(saves == 6 && changed.SequenceEqual(names), "Cada cambio debe notificar la UI y guardarse una sola vez.");
            visibility.ShowQuickActionsInHome = false;
            visibility.ShowAudioControlCardInHome = false;
            Require(saves == 6, "Repetir un valor no debe producir escrituras adicionales.");
            Require(properties.All(p => Equals(p.GetValue(persistence.LoadConfig().Settings), false)),
                "La persistencia real debe reflejar los cambios de los controles reactivos.");
            config.Settings = new AppSettings();
            visibility.Reload(config.Settings);
            Require(visibility.ShowNetworkCardInHome && visibility.ShowEnergyStatusCardInHome && visibility.ShowHardwareCardInHome &&
                visibility.ShowAiRadarCardInHome && visibility.ShowQuickActionsInHome && visibility.ShowAudioControlCardInHome && saves == 6,
                "Restablecer ajustes debe actualizar las seis tarjetas sin sobrescribir la configuración durante la carga.");
            Console.WriteLine("[PASS] Cabina reactiva: notificación, guardado inmediato y restauración de valores por defecto.");
            using ISystemAdapter adapter = new WindowsSystemAdapter();
            var hardware = adapter.GetHardwareProfile();
            Require(ReferenceEquals(hardware, adapter.GetHardwareProfile()), "El perfil debe reutilizar el snapshot estático.");
            Require(hardware.InstalledMemoryGigabytes > 0 && hardware.MonitorCount > 0 &&
                hardware.PrimaryScreenWidth > 0 && hardware.PrimaryScreenHeight > 0, "Win32 debe detectar RAM y pantallas reales.");
            Require(hardware.GraphicsAdapterName.Contains(adapter.GetCurrentMetrics().GpuAdapterName, StringComparison.OrdinalIgnoreCase),
                "El perfil de múltiples GPU debe incluir la identidad del colector existente.");
            Require(hardware.OperatingSystemName.Contains("Build") && hardware.ProcessorName != "No disponible",
                "El perfil debe incluir el build del SO y el nombre comercial de la CPU.");
            Console.WriteLine($"[PASS] Hardware local: {hardware.OperatingSystemName}; {hardware.ProcessorName}; {hardware.MemoryText}; {hardware.GraphicsAdapterName}; {hardware.MonitorsText}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            return 1;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
