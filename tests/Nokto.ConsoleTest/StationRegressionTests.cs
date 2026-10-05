using Microsoft.Win32;
using System.Text.Json;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Serialization;
using Nokto.Core.Services;
using Nokto.Platform.Windows;
using Nokto.Platform.Windows.Audio;
using Nokto.Platform.Windows.Hardware;

namespace Nokto.ConsoleTest;

internal static class StationRegressionTests
{
    public static int Run()
    {
        try
        {
            VerifyGraphicsNames();
            VerifyDiskAlerts();
            VerifyDiskConfiguration();
            VerifyInstalledPhysicalGraphics();
            VerifyDefaultAudioDevices();
            VerifyAudioPresentation();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Estación: {ex.Message}");
            return 1;
        }
    }

    private static void VerifyGraphicsNames()
    {
        GraphicsAdapterIdentity[] hybrid =
        [
            new("Intel(R) Iris(R) Xe Graphics", 128UL << 20),
            new("NVIDIA GeForce RTX 4050 Laptop GPU", 6UL << 30),
            new("nvidia geforce rtx 4050 laptop gpu", 6UL << 30),
            new("Microsoft Basic Render Driver", 0),
            new("Virtual Display Adapter", 0),
            new("Software Rasterizer", 0),
            new("Remote Display Adapter", 0),
            new("  ", 0)
        ];
        string names = GraphicsAdapterDiscovery.FormatNames(hybrid, "No disponible");
        string[] physical = names.Split(" + ", StringSplitOptions.TrimEntries);
        Require(physical.Length == 2 && physical[0].Equals("NVIDIA GeForce RTX 4050 Laptop GPU", StringComparison.OrdinalIgnoreCase) &&
            physical[1] == "Intel(R) Iris(R) Xe Graphics",
            $"El perfil debe priorizar la dedicada, deduplicar nombres y excluir adaptadores no físicos: '{names}'.");
        string byMemory = GraphicsAdapterDiscovery.FormatNames(
            [new("Intel Graphics", 0), new("NVIDIA RTX", 6UL << 30), new("AMD Radeon", 8UL << 30)], "No disponible");
        Require(byMemory == "AMD Radeon + NVIDIA RTX + Intel Graphics",
            "La prioridad debe depender de memoria dedicada, sin privilegiar una marca.");
        Require(GraphicsAdapterDiscovery.FormatNames([], "Intel Graphics") == "Intel Graphics",
            "Si DXGI no devuelve adaptadores, debe conservarse la identidad disponible del colector.");
        Require(GraphicsAdapterDiscovery.FormatNames([new("Microsoft Basic Display Adapter", 0)], "Intel Graphics") == "Intel Graphics",
            "Una enumeración sin adaptadores físicos debe usar el fallback.");
        Require(GraphicsAdapterDiscovery.FormatNames([], "  ") == "No disponible",
            "La ausencia de GPU y fallback debe tener una presentación clara.");
        Console.WriteLine("[PASS] GPU: dedicadas primero, ambas identidades, deduplicación, filtros y fallback.");
    }

    private static void VerifyDiskAlerts()
    {
        Require(DiskSpaceAlert.IsLowSpace(15, 15), "El límite exacto debe activar la advertencia de disco.");
        Require(DiskSpaceAlert.IsLowSpace(14, 15) && DiskSpaceAlert.IsLowSpace(0, 15),
            "El espacio inferior al umbral, incluido cero, debe activar la advertencia.");
        Require(!DiskSpaceAlert.IsLowSpace(16, 15), "El espacio superior al umbral no debe advertir.");
        Require(!DiskSpaceAlert.IsLowSpace(null, 15) && !DiskSpaceAlert.IsLowSpace(-1, 15),
            "Las lecturas desconocidas no deben producir una falsa alarma.");
        Require(DiskSpaceAlert.IsLowSpace(30, 30) && !DiskSpaceAlert.IsLowSpace(30, 15),
            "Cambiar el umbral debe cambiar la decisión sin cambiar la lectura de disco.");
        Console.WriteLine("[PASS] Disco: límites inclusivos, cero libre, datos desconocidos y umbral configurable.");
    }

    private static void VerifyDiskConfiguration()
    {
        var legacy = JsonSerializer.Deserialize("""{"settings":{"theme":"Light"}}""", NoktoJsonContext.Default.AppConfig)!;
        Require(legacy.Settings.DiskAlertThresholdGb == 15,
            "La configuración anterior debe recibir el umbral de 15 GB por defecto.");
        string directory = Path.Combine(Path.GetTempPath(), $"nokto_station_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var persistence = new PersistenceService(new StorageResolver(directory));
            var config = new AppConfig { Settings = new AppSettings { DiskAlertThresholdGb = 27, StartInWorkMode = true, Theme = "Light" } };
            persistence.SaveConfig(config);
            var restored = persistence.LoadConfig();
            Require(restored.Settings.DiskAlertThresholdGb == 27 && restored.Settings.StartInWorkMode && restored.Settings.Theme == "Light",
                "El umbral debe persistir con JSON AOT conservando las otras preferencias.");
            config.Settings = new AppSettings();
            persistence.SaveConfig(config);
            Require(persistence.LoadConfig().Settings.DiskAlertThresholdGb == 15,
                "Restaurar los ajustes debe persistir de nuevo el umbral de 15 GB.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("[PASS] Ajustes de disco: compatibilidad, persistencia portable y restauración por defecto.");
    }

    private static void VerifyDefaultAudioDevices()
    {
        using ISystemAdapter adapter = new WindowsSystemAdapter();
        AudioDeviceProfile devices = adapter.GetAudioDevices();
        Require(!string.IsNullOrWhiteSpace(devices.OutputName) && !string.IsNullOrWhiteSpace(devices.InputName),
            "La estación debe informar salida y entrada de audio, o indicar su ausencia explícitamente.");
        if (devices.OutputName != "Sin dispositivo activo")
        {
            Require(devices.OutputVolumePercent is >= 0 and <= 100 && devices.OutputMuted.HasValue,
                "La salida activa debe proporcionar porcentaje maestro y silencio del sistema.");
            using var controller = new WasapiAudioController();
            Require(Math.Abs(controller.GetMasterVolume() * 100 - devices.OutputVolumePercent.GetValueOrDefault()) <= 1 &&
                controller.GetMute() == devices.OutputMuted,
                "La telemetría debe coincidir con el endpoint usado por el controlador de audio.");
        }
        if (devices.InputName != "Sin dispositivo activo")
            Require(devices.InputMuted.HasValue, "El micrófono activo debe proporcionar su silencio del sistema.");
        Console.WriteLine($"[PASS] Audio local predeterminado: salida '{devices.OutputText}', entrada '{devices.InputText}'.");
    }

    private static void VerifyAudioPresentation()
    {
        var devices = new AudioDeviceProfile
        {
            OutputName = "HDMI1 (NVIDIA High Definition Audio)", OutputVolumePercent = 45, OutputMuted = false,
            InputName = "Varios micrófonos (2- Intel® Smart Sound Technology for Digital Microphones)", InputMuted = false
        };
        Require(devices.OutputText == "HDMI1 (NVIDIA High Definition Audio) — 45%" && devices.OutputCompactText == "HDMI1 — 45%",
            "La salida debe conservar su nombre completo y presentar un nombre compacto con volumen.");
        Require((devices with { OutputMuted = true }).OutputCompactText == "HDMI1 — [Silenciado]",
            "El silencio maestro debe tener prioridad sobre el porcentaje.");
        Require((devices with { OutputVolumePercent = 0 }).OutputCompactText.EndsWith("— 0%") &&
            (devices with { OutputVolumePercent = 100 }).OutputCompactText.EndsWith("— 100%"),
            "Los extremos de volumen son lecturas válidas.");
        Require(devices.InputText == "Intel® Smart Sound Mic — 🟢 Listo" &&
            (devices with { InputMuted = true }).InputText.EndsWith("— 🔴 Silenciado"),
            "El micrófono debe conservar su identidad comercial compacta y reflejar su silencio.");
        Require((devices with { InputMuted = null }).InputText.EndsWith("Estado no disponible"),
            "Una lectura fallida de silencio no debe afirmar que el micrófono está listo.");
        Require((devices with { InputName = new string('x', 80) }).InputDisplayName.Length == 32,
            "Los nombres largos de otros fabricantes deben acortarse sin modificar el nombre original.");
        Require(new AudioDeviceProfile().InputText == "Sin dispositivo activo" && new AudioDeviceProfile().OutputText == "Sin dispositivo activo",
            "Los equipos sin endpoints no deben mostrar porcentajes ni estados inventados.");
        Require(devices != devices with { OutputVolumePercent = 46 } && devices != devices with { InputMuted = true },
            "Los cambios de volumen y silencio deben participar en la actualización reactiva del perfil.");
        Console.WriteLine("[PASS] Presentación de audio: volumen, silencio, nombres compactos y lecturas desconocidas.");
    }

    private static void VerifyInstalledPhysicalGraphics()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var videoClass = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        var installedPhysical = new List<string>();
        foreach (string keyName in videoClass?.GetSubKeyNames() ?? [])
        {
            if (keyName.Length != 4 || !keyName.All(char.IsDigit)) continue;
            using var driver = videoClass!.OpenSubKey(keyName);
            if (driver?.GetValue("DriverDesc") is not string name || string.IsNullOrWhiteSpace(name)) continue;
            if (new[] { "Basic", "Virtual", "Software", "Remote" }.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            installedPhysical.Add(name.Trim());
        }
        using ISystemAdapter adapter = new WindowsSystemAdapter();
        string graphics = adapter.GetHardwareProfile().GraphicsAdapterName;
        foreach (string physical in installedPhysical.Distinct(StringComparer.OrdinalIgnoreCase))
            Require(graphics.Contains(physical, StringComparison.OrdinalIgnoreCase),
                $"La GPU física instalada '{physical}' debe aparecer en la estación; actual: '{graphics}'.");
        Console.WriteLine($"[PASS] Identidad de GPU híbrida local: {graphics}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
