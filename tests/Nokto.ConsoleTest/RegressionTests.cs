using Nokto.Core.Models;
using Nokto.Core.Persistence;

namespace Nokto.ConsoleTest;

internal static class RegressionTests
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nokto_regression_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var service = new PersistenceService(new StorageResolver(directory));
            var editedSystem = Presets.GetDefaultPresets()[0] with
            {
                Description = "Personalización conservada",
                IsSystemPreset = false,
                Trigger = new TriggerDefinition { Type = TriggerType.Schedule }
            };
            var custom = new PresetDefinition
            {
                Id = "custom_blender",
                Name = "Render Nocturno Blender",
                Description = "Rutina del usuario"
            };
            service.SavePresets(new PresetsFile { Presets = [editedSystem, custom] });
            var loaded = service.LoadPresets();
            Require(loaded.Presets.Count == 6, "Debe fusionar las cinco IDs del sistema y conservar la rutina personalizada con el mismo nombre.");
            Require(loaded.Presets.Single(p => p.Id == editedSystem.Id).Trigger.Type == TriggerType.Schedule,
                "La carga debe conservar la personalización de la rutina del sistema.");
            Require(!loaded.Presets.Single(p => p.Id == custom.Id).IsSystemPreset,
                "Una coincidencia de nombre no convierte una rutina del usuario en rutina del sistema.");
            Require(Presets.GetDefaultPresets().All(p => loaded.Presets.Any(x => x.Id == p.Id && x.IsSystemPreset)),
                "Las cinco rutinas del sistema deben estar protegidas.");
            Require(service.LoadPresets().Presets.Count == 6, "La fusión persistida debe ser idempotente.");
            Console.WriteLine("[PASS] Fusión portable: cinco IDs, personalizaciones e idempotencia.");
            var restored = Presets.RestoreFactoryPresets([.. loaded.Presets, editedSystem]);
            Require(restored.Count == 6 && restored.Single(p => p.Id == editedSystem.Id).Trigger.Type == TriggerType.UserIdle,
                "Restaurar debe reemplazar ajustes del sistema y eliminar IDs de fábrica duplicadas.");
            var restoredCustom = restored.Single(p => p.Id == custom.Id);
            Require(restoredCustom.Name == custom.Name && restoredCustom.Description == custom.Description && !restoredCustom.IsSystemPreset,
                "Restaurar debe conservar la rutina personalizada que comparte nombre.");
            Require(Presets.GetDefaultPresets().All(p => restored.Any(x => x.Id == p.Id && x.IsSystemPreset)),
                "Restaurar debe devolver exactamente las cinco rutinas protegidas.");
            service.SavePresets(new PresetsFile { Presets = restored });
            Require(service.LoadPresets().Presets.Count == 6, "La restauración debe persistir sin duplicados.");
            Console.WriteLine("[PASS] Restauración: cinco rutinas de fábrica, sin duplicados y conservando las personalizadas.");
            string? repo = AppContext.BaseDirectory;
            while (repo != null && !File.Exists(Path.Combine(repo, "Nokto.sln")))
                repo = Directory.GetParent(repo)?.FullName;
            Require(repo != null, "No se encontró el proyecto para verificar el layout XAML.");
            var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(repo!, "src", "Nokto.UI", "Views", "MainWindow.axaml"));
            System.Xml.Linq.XNamespace ns = "https://github.com/avaloniaui";
            var status = xaml.Descendants(ns + "TextBlock").Single(e => (string?)e.Attribute("Text") == "{Binding WorkModeStatusText}");
            var card = status.Ancestors(ns + "Border").First(e => e.Attribute("Grid.Column") != null);
            Require((string?)card.Attribute("Grid.Column") == "2" && (string?)card.Attribute("Grid.Row") == "0",
                "La tarjeta de energía debe estar en la primera fila y usar su columna de contenido reactiva.");
            var dashboard = card.Parent;
            Require((string?)dashboard?.Attribute("ColumnDefinitions") == "*,*,*" && (string?)dashboard?.Attribute("RowDefinitions") == "Auto,Auto,*",
                "Inicio debe usar tres columnas y reservar el espacio restante para tareas, sin scroll de toda la cabina.");
            Console.WriteLine("[PASS] Layout Inicio: tarjeta de energía en columna de contenido.");
            System.Xml.Linq.XNamespace views = "using:Nokto.UI.Views";
            var audioPanel = xaml.Descendants(views + "HomeAudioView").Single();
            Require(audioPanel.Ancestors(ns + "TabItem").First() == xaml.Descendants(ns + "TabItem").First() &&
                (string?)audioPanel.Attribute("IsVisible") == "{Binding Config.ShowAudioControlCardInHome}",
                "El control de audio debe estar únicamente en Inicio y respetar su preferencia de visibilidad.");
            QuotaRegressionTests.Run();
            AdvancedQuotaRegressionTests.Run();
            Require(CabinRegressionTests.Run() == 0, "Fallaron las verificaciones de cabina y hardware.");
            Require(StationRegressionTests.Run() == 0, "Fallaron las verificaciones de GPU híbrida, audio y alerta de disco.");
            AudioControlRegressionTests.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            return 1;
        }
        finally
        {
            // This directory was created by this test under the OS temporary directory.
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
