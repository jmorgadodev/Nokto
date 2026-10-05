using Nokto.Core.Services;

namespace Nokto.ConsoleTest;

internal static class AiToolDiscoveryTests
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nokto_ai_tools_" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(directory, "profile"), roaming = Path.Combine(directory, "roaming"), local = Path.Combine(directory, "local");
        string installed = Path.Combine(directory, "installed"), bin = Path.Combine(directory, "bin");
        try
        {
            var files = new Dictionary<string, string>
            {
                ["claude"] = Path.Combine(local, "Claude", "app-1.2", "Claude.exe"),
                ["lmstudio"] = Path.Combine(local, "Programs", "LM Studio", "LM Studio.exe"),
                ["ollama"] = Path.Combine(local, "Programs", "Ollama", "ollama app.exe"),
                ["opencode"] = Path.Combine(local, "Programs", "@opencode-aidesktop", "OpenCode.exe"),
                ["gpt4all"] = Path.Combine(installed, "GPT4All", "bin", "chat.exe"),
                ["windsurf"] = Path.Combine(bin, "Windsurf.exe")
            };
            foreach (string path in files.Values)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "test fixture; never execute");
            }
            Directory.CreateDirectory(Path.Combine(local, "Programs", "Cursor"));
            Directory.CreateDirectory(Path.Combine(profile, ".ollama", "models"));
            File.WriteAllText(Path.Combine(bin, "opencode.cmd"), "test fixture; never execute");
            File.WriteAllText(Path.Combine(local, "Programs", "Ollama", "ollama.exe"), "test fixture; never execute");
            var discovery = new LocalAiToolDiscovery(profile, roaming, local, installRoots: [installed], pathDirectories: [bin]);
            var detected = discovery.Discover();
            Require(detected.Count == files.Count && detected.Select(tool => tool.Id).Distinct().Count() == files.Count,
                "Detección de escritorio, CLI, directorios versionados y raíz de instalación sin duplicados.");
            foreach (var tool in detected)
                Require(tool.ExecutablePath == files[tool.Id], "El acceso resuelve el ejecutable real: " + tool.Name);
            Require(!detected.Any(tool => tool.Id == "cursor"), "Una carpeta vacía no demuestra una instalación.");
            var snapshot = new AiQuotaService(profile, roaming, local).InspectLocalQuotas();
            foreach (string id in new[] { "claude", "lmstudio", "ollama" })
            {
                var tool = snapshot.Environments.Single(item => item.Id == id);
                Require(tool.ExecutablePath == files[id] && tool.Metrics.Count == 0 && !tool.HasExplicitQuotaMetrics &&
                    tool.FiveHourUsagePercentage is null && tool.WeeklyUsagePercentage is null,
                    "Una instalación sin telemetría solo ofrece acceso directo: " + id);
            }
            File.Delete(files["claude"]);
            string claudeCli = Path.Combine(profile, ".local", "bin", "claude.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(claudeCli)!);
            File.WriteAllText(claudeCli, "test fixture; never execute");
            Require(string.Equals(discovery.Discover().Single(tool => tool.Id == "claude").ExecutablePath, claudeCli, StringComparison.OrdinalIgnoreCase),
                "Claude Code instalado en el binario del perfil se detecta sin iniciarlo.");
            Console.WriteLine("[PASS] Herramientas IA offline: instalaciones, CLI, versiones, nombres con espacios, deduplicación y ausencia de cuotas inventadas.");
            var localSnapshot = new AiQuotaService().InspectLocalQuotas();
            Console.WriteLine("[INFO] Accesos locales detectados: " + string.Join(", ", localSnapshot.Environments.Select(tool => tool.Name)));
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[FAIL] Herramientas IA: " + ex); return 1; }
        finally
        {
            if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(directory).StartsWith("nokto_ai_tools_", StringComparison.Ordinal)) Directory.Delete(directory, true);
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
