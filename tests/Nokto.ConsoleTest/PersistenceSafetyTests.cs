using Nokto.Core.Models;
using Nokto.Core.Persistence;

namespace Nokto.ConsoleTest;

internal static class PersistenceSafetyTests
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nokto_persistence_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Require(StorageResolver.ResolvePortableMode(true, false, true, false, false, false) &&
                StorageResolver.ResolvePortableMode(false, true, true, false, false, false), "DEV y portable.lock preceden a instalado.");
            Require(!StorageResolver.ResolvePortableMode(false, false, true, false, false, true),
                "Una instalación nueva por usuario usa AppData aunque el directorio sea escribible.");
            Require(StorageResolver.ResolvePortableMode(false, false, true, false, true, true),
                "Una instalación antigua conserva los datos junto al ejecutable.");
            Require(StorageResolver.ResolvePortableMode(false, false, false, false, false, true) &&
                !StorageResolver.ResolvePortableMode(false, false, false, false, false, false), "La detección anterior sigue funcionando sin marcadores.");
            var storage = new StorageResolver(directory);
            var service = new PersistenceService(storage);
            const string broken = "{\"presets\":[{\"id\":\"recover-me\"}";
            File.WriteAllText(storage.PresetsFilePath, broken);
            Require(service.LoadPresets().Presets.Count > 0, "La recuperación ofrece rutinas predeterminadas en memoria.");
            Require(File.ReadAllText(storage.PresetsFilePath) == broken, "Leer un archivo corrupto debe preservar el original.");
            service.SavePresets(new PresetsFile { Presets = [new() { Id = "custom", Name = "Guardada" }] });
            Require(Directory.GetFiles(storage.DataDirectory, "presets.json.corrupt.*").Any(p => File.ReadAllText(p) == broken),
                "Guardar después de recuperar conserva evidencia del archivo corrupto.");
            service.SavePresets(new PresetsFile { Presets = [new() { Id = "custom", Name = "Actualizada" }] });
            File.WriteAllText(storage.PresetsFilePath, broken);
            Require(service.LoadPresets().Presets.Any(p => p.Id == "custom" && p.Name == "Guardada"),
                "Un archivo inválido recupera el último respaldo válido.");
            Require(File.ReadAllText(storage.PresetsFilePath) == broken, "Recuperar respaldo no sobrescribe el original.");

            File.WriteAllText(storage.ConfigFilePath, "{\"settings\":null,\"keepAlive\":null,\"lanServer\":null,\"island\":null}");
            var config = service.LoadConfig();
            Require(config.Settings is not null && config.KeepAlive is not null && config.LanServer is not null && config.Island?.Productivity is not null,
                "Objetos de configuración nulos no llegan al consumidor.");
            File.WriteAllText(storage.ConfigFilePath, "{\"settings\":{\"hiddenAiEnvironmentIds\":null,\"aiEnvironmentOrder\":null},\"island\":{\"shelfFiles\":null,\"quickFolders\":null,\"pageOrder\":null,\"productivity\":{\"alarms\":[null],\"tasks\":null}}}");
            config = service.LoadConfig();
            Require(config.Settings.HiddenAiEnvironmentIds is not null && config.Settings.AiEnvironmentOrder is not null &&
                config.Island.ShelfFiles is not null && config.Island.QuickFolders is not null && config.Island.PageOrder is not null &&
                config.Island.Productivity.Alarms.Count == 0 && config.Island.Productivity.Tasks.Count == 0,
                "Colecciones anidadas nulas se normalizan.");
            service.SaveConfig(new AppConfig { Settings = new() { Language = "es" } });
            service.SaveConfig(new AppConfig { Settings = new() { Language = "en" } });
            File.WriteAllText(storage.ConfigFilePath, "{");
            Require(service.LoadConfig().Settings.Language == "es", "La configuración recupera su respaldo válido.");
            service.SaveConfig(new AppConfig());
            Require(Directory.GetFiles(storage.DataDirectory, "config.json.corrupt.*").Any(p => File.ReadAllText(p) == "{"),
                "Guardar configuración preserva el original inválido.");
            File.WriteAllText(storage.PresetsFilePath, "{\"presets\":[null,{\"id\":\"nullable\",\"trigger\":null,\"pipeline\":[null],\"terminalAction\":null,\"actions\":[null]}]}");
            var routine = service.LoadPresets().Presets.Single(p => p.Id == "nullable");
            Require(routine.Trigger is not null && routine.TerminalAction is not null && routine.Pipeline.Count == 0 && routine.Actions?.Count == 0,
                "Rutinas y pasos nulos no causan fallos al abrir el editor.");

            string newBase = Path.Combine(directory, "not-created", "nested");
            var isolated = new StorageResolver(newBase);
            Require(isolated.IsPortableMode && isolated.DataDirectory == Path.Combine(newBase, "data") && Directory.Exists(isolated.DataDirectory),
                "Una ruta explícita inexistente se crea y permanece aislada.");
            string blocked = Path.Combine(directory, "file-instead-of-directory");
            File.WriteAllText(blocked, "fixture");
            try { _ = new StorageResolver(blocked); throw new InvalidOperationException("Una ruta inválida no debe caer a AppData."); }
            catch (IOException) { }

            Parallel.For(0, 30, i => new PersistenceService(storage).SaveConfig(new AppConfig { Settings = new() { Language = "test-" + i } }));
            Require(service.LoadConfig().Settings.Language.StartsWith("test-", StringComparison.Ordinal) &&
                Directory.GetFiles(storage.DataDirectory, "*.tmp").Length == 0,
                "Escrituras concurrentes producen JSON completo sin temporales abandonados.");
            Console.WriteLine("[PASS] Persistencia: aislamiento, escritura atómica, respaldo, conservación de corrupción y objetos nulos.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[FAIL] Persistencia: " + ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
