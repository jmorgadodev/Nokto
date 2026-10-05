using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Nokto.Core.Services;

public record AiQuotaMetric(
    string ModelName,
    string WindowType,
    double RemainingPercent,
    string DetailText
)
{
    public string WindowLabel => WindowType.StartsWith(ModelName + " · ", StringComparison.Ordinal)
        ? WindowType[(ModelName.Length + 3)..] : WindowType;
    public bool HasModelLabel => WindowLabel != WindowType;
}

public record AiEnvironmentQuota(
    string Id,
    string Name,
    string IconKey,
    bool IsDetected,
    string? ExecutablePath,
    string? DbOrConfigPath,
    List<AiQuotaMetric> Metrics,
    double PrimaryWindowPercent,
    string ResetTimeText,
    bool IsRecommended,
    string StatusNote = "Entorno instalado y detectado en disco (Sesión activa)",
    bool HasExplicitQuotaMetrics = false,
    double? FiveHourUsagePercentage = null,
    double? WeeklyUsagePercentage = null
)
{
    public string ActivityBadgeText => Id == "codex" ? "Plus" : "Activo";
    public string LauncherBadgeText => Id switch
    {
        "opencode" => "[ BYOK / Local ]",
        "codex" or "antigravity" => "[ Sesión Lista ]",
        _ => "[ Instalado ]"
    };
}

public record AiQuotaSnapshot(
    bool AnyDetected,
    List<AiEnvironmentQuota> Environments,
    string? RecommendedEnvironmentName
);

public interface IAiQuotaService
{
    AiQuotaSnapshot InspectLocalQuotas();
    bool LaunchEnvironment(string environmentId);
}

/// <summary>Offline inspection of explicit quota telemetry; never supplies estimated percentages.</summary>
public class AiQuotaService : IAiQuotaService
{
    private const int MaxJsonBytes = 1024 * 1024;
    private const string ClientQuotaStatus = "[ Sesión Activa ] - Cuota en memoria / cliente";
    private readonly string _userProfile;
    private readonly string _appData;
    private readonly string _localAppData;
    private readonly LocalAiToolDiscovery _toolDiscovery;

    public AiQuotaService(string? userProfile = null, string? appData = null, string? localAppData = null)
    {
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _appData = appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _toolDiscovery = new(_userProfile, _appData, _localAppData,
            includeSystemInstalls: userProfile is null && appData is null && localAppData is null);
    }

    public AiQuotaSnapshot InspectLocalQuotas()
    {
        var environments = new List<AiEnvironmentQuota>();
        var antigravity = InspectAntigravity();
        if (antigravity != null) environments.Add(antigravity);
        var codex = InspectEnvironment("codex", "Codex Desktop / VS Code", "IconReticle", FindCodexExecutable(),
            [Path.Combine(_appData, "Codex", "User", "globalStorage", "state.vscdb"),
             Path.Combine(_appData, "Code", "User", "globalStorage", "state.vscdb")],
            Path.Combine(_userProfile, ".codex"));
        if (codex != null) environments.Add(codex);
        var openCode = InspectOpenCode();
        if (openCode != null) environments.Add(openCode);

        foreach (var tool in _toolDiscovery.Discover())
        {
            int existing = environments.FindIndex(e => e.Id == tool.Id);
            if (existing >= 0)
            {
                if (environments[existing].ExecutablePath is null)
                    environments[existing] = environments[existing] with { ExecutablePath = tool.ExecutablePath };
            }
            else environments.Add(new(tool.Id, tool.Name, "IconTerminal", true, tool.ExecutablePath,
                null, [], 0, "", false, "Herramienta instalada · acceso local"));
        }

        // An unknown quota cannot outrank a measured available quota as if it were 100%.
        var best = environments.Where(e => e.HasExplicitQuotaMetrics && e.FiveHourUsagePercentage.HasValue)
            .OrderByDescending(e => e.PrimaryWindowPercent).FirstOrDefault();
        if (best != null)
        {
            int index = environments.FindIndex(e => e.Id == best.Id);
            environments[index] = best with { IsRecommended = true };
        }
        return new AiQuotaSnapshot(environments.Count > 0,
            environments.OrderByDescending(e => e.HasExplicitQuotaMetrics).ToList(), best?.Name);
    }

    public bool LaunchEnvironment(string environmentId)
    {
        string? executable = environmentId switch
        {
            "antigravity" => FindAntigravityExecutable(),
            "codex" => FindCodexExecutable(),
            "opencode" => FindOpenCodeExecutable(),
            _ => null
        };
        executable ??= _toolDiscovery.Discover().FirstOrDefault(t => t.Id == environmentId)?.ExecutablePath;
        if (executable == null || !File.Exists(executable)) return false;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = executable, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private AiEnvironmentQuota? InspectEnvironment(string id, string name, string icon, string? executable,
        string[] databases, string configDirectory)
    {
        var parser = new AiQuotaParser();
        string? source = null;
        foreach (string database in databases.Where(File.Exists))
        {
            // Code's shared database alone is not evidence of a Codex installation.
            if (InspectDatabase(database, id, parser)) source ??= database;
        }
        bool hasConfig = Directory.Exists(configDirectory);
        if (hasConfig)
        {
            source ??= configDirectory;
            InspectJsonFiles(configDirectory, id == "codex", parser);
        }
        if (executable == null && source == null && !hasConfig) return null;
        var metrics = new List<AiQuotaMetric>();
        AddMetric(metrics, name, "Ventana 5h", parser.FiveHour);
        AddMetric(metrics, name, "Semanal", parser.Weekly);
        var reset = parser.FiveHour?.ResetsAt ?? parser.Weekly?.ResetsAt;
        return new AiEnvironmentQuota(id, name, icon, true, executable, source, metrics,
            parser.FiveHour is { } fiveHour ? 100 - fiveHour.UsedPercent : 0,
            reset is { } instant ? $"Restablece: {instant.ToLocalTime():g}" : "Cuota en memoria / cliente",
            false, metrics.Count > 0 ? "Cuotas extraídas de telemetría local" : ClientQuotaStatus,
            metrics.Count > 0, parser.FiveHour?.UsedPercent, parser.Weekly?.UsedPercent);
    }

    private static void AddMetric(List<AiQuotaMetric> metrics, string name, string window, AiQuotaParser.Window? value)
    {
        if (value == null) return;
        double remaining = 100 - value.UsedPercent;
        metrics.Add(new AiQuotaMetric(name, window, remaining, $"{remaining:0.#}% restante"));
    }

    private static bool InspectDatabase(string source, string environmentId, AiQuotaParser? parser, AiLocalQuotaTelemetry? telemetry = null)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"nokto_{environmentId}_{Guid.NewGuid():N}.vscdb");
        bool foundEnvironment = false;
        try
        {
            CopyShared(source, temporary);
            // Committed pages may still reside in the IDE's write-ahead log.
            if (File.Exists(source + "-wal")) CopyShared(source + "-wal", temporary + "-wal");
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = temporary,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 1
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT key, value FROM ItemTable
                WHERE (key LIKE '%usage%' OR key LIKE '%quota%' OR key LIKE '%ratelimit%'
                    OR key LIKE '%session%' OR key LIKE '%openai.chatgpt%' OR key LIKE '%codex%'
                    OR key = 'antigravityUnifiedStateSync.modelCredits' OR key = 'antigravityUnifiedStateSync.userStatus')
                  AND length(value) <= 1048576
                ORDER BY CASE WHEN key LIKE '%quota%' OR key LIKE '%usage%' OR key LIKE '%ratelimit%' THEN 0 ELSE 1 END
                LIMIT 256;
                """;
            using var reader = command.ExecuteReader();
            var observedAt = File.GetLastWriteTimeUtc(source);
            while (reader.Read())
            {
                string key = reader.GetString(0);
                bool belongs = environmentId == "codex"
                    ? key.Contains("codex", StringComparison.OrdinalIgnoreCase) || key.Contains("openai.chatgpt", StringComparison.OrdinalIgnoreCase)
                    : key.Contains("antigravity", StringComparison.OrdinalIgnoreCase);
                if (!belongs) continue;
                foundEnvironment = true;
                if (reader.IsDBNull(1)) continue;
                byte[] bytes = reader.GetValue(1) is byte[] blob ? blob : Encoding.UTF8.GetBytes(reader.GetString(1));
                if (telemetry != null) AntigravityStateDecoder.Read(key, bytes, observedAt, telemetry);
                else parser?.Read(Encoding.UTF8.GetString(bytes), observedAt, environmentId == "codex");
            }
            // Dedicated environment storage is evidence even if its schema/values are opaque.
            return foundEnvironment || !source.Contains(Path.Combine("Code", "User"), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            Debug.WriteLine($"Nokto: {environmentId} local SQLite telemetry unavailable ({ex.GetType().Name}).");
            return foundEnvironment || !source.Contains(Path.Combine("Code", "User"), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(temporary + suffix); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static void InspectJsonFiles(string directory, bool codex, AiQuotaParser parser)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var candidates = new List<string>();
            foreach (string filename in new[] { "session.json", "config.json", "state.json", "usage.json", "quota.json", ".codex-global-state.json" })
            {
                string path = Path.Combine(directory, filename);
                if (File.Exists(path)) candidates.Add(path);
            }
            foreach (string folder in new[] { "sessions", "telemetry", "state" })
            {
                string path = Path.Combine(directory, folder);
                if (!Directory.Exists(path)) continue;
                candidates.AddRange(Directory.EnumerateFiles(path, "*", options)
                    .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(8));
            }
            foreach (string path in candidates.OrderBy(File.GetLastWriteTimeUtc))
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var observedAt = File.GetLastWriteTimeUtc(path);
                    bool lines = path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
                    if (!lines && stream.Length > MaxJsonBytes) continue;
                    bool tail = lines && stream.Length > MaxJsonBytes;
                    if (tail) stream.Seek(-MaxJsonBytes, SeekOrigin.End);
                    using var reader = new StreamReader(stream);
                    if (tail) reader.ReadLine(); // Skip the potentially incomplete first line.
                    if (!lines) parser.Read(reader.ReadToEnd(), observedAt, codex);
                    else
                    {
                        int count = 0;
                        while (reader.ReadLine() is { } line && count++ < 4096)
                            parser.Read(line, observedAt, codex, sessionEvent: codex);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine("Nokto: local quota file temporarily unavailable.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Nokto: local quota directory temporarily unavailable.");
        }
    }

    private AiEnvironmentQuota? InspectAntigravity()
    {
        var telemetry = new AiLocalQuotaTelemetry("Antigravity IDE");
        string? source = null;
        foreach (string application in new[] { "Antigravity IDE", "Antigravity" })
        {
            string root = Path.Combine(_appData, application);
            string database = Path.Combine(root, "User", "globalStorage", "state.vscdb");
            if (File.Exists(database))
            {
                InspectDatabase(database, "antigravity", null, telemetry);
                source ??= database;
            }
            string levelDb = Path.Combine(root, "Local Storage", "leveldb");
            if (Directory.Exists(levelDb)) source ??= levelDb;
            ScanTelemetryFiles(levelDb, telemetry, [".log", ".ldb"], recursive: false);
            string logs = Path.Combine(root, "logs");
            if (Directory.Exists(logs)) source ??= logs;
            InspectLatestLogs(logs, telemetry);
        }
        string profile = Path.Combine(_userProfile, ".antigravity");
        if (Directory.Exists(profile)) source ??= profile;
        ScanTelemetryFiles(profile, telemetry, [".json", ".jsonl"]);
        InspectLatestLogs(Path.Combine(profile, "logs"), telemetry);
        ScanTelemetryFiles(profile, telemetry, [".log"], recursive: false);
        string? executable = FindAntigravityExecutable();
        return source == null && executable == null ? null : TelemetrySnapshot("antigravity", "Google Antigravity IDE", "IconRadar", executable, source, telemetry);
    }

    private AiEnvironmentQuota? InspectOpenCode()
    {
        var telemetry = new AiLocalQuotaTelemetry("OpenCode");
        string? source = null;
        // Inspect only these three roots, excluding dependency/assets/example trees and symlinks.
        foreach (string directory in new[] { Path.Combine(_userProfile, ".config", "opencode"), Path.Combine(_userProfile, ".opencode"), Path.Combine(_localAppData, "opencode") })
        {
            if (Directory.Exists(directory)) source ??= directory;
            ScanTelemetryFiles(directory, telemetry, [".json", ".jsonc"]);
        }
        string? executable = FindOpenCodeExecutable();
        return source == null && executable == null ? null : TelemetrySnapshot("opencode", "OpenCode", "IconChrono", executable, source, telemetry);
    }

    private static AiEnvironmentQuota TelemetrySnapshot(string id, string name, string icon, string? executable, string? source, AiLocalQuotaTelemetry telemetry)
    {
        var metrics = telemetry.Metrics;
        string usageDetailsText = id switch
        {
            "antigravity" => "Sesión lista",
            "opencode" => "Entorno detectado",
            _ => "Sesión activa"
        };
        return new AiEnvironmentQuota(id, name, icon, true, executable, source, metrics,
            telemetry.FiveHourUsage is { } used ? 100 - used : 0,
            telemetry.Reset is { } reset ? $"Restablece: {reset.ToLocalTime():g}" : "Cuota en memoria / cliente",
            false, usageDetailsText, metrics.Count > 0, telemetry.FiveHourUsage, telemetry.WeeklyUsage);
    }

    private static void InspectLatestLogs(string directory, AiLocalQuotaTelemetry telemetry)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            string? latest = Directory.EnumerateDirectories(directory)
                .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^\d{8}T\d{6}$", RegexOptions.CultureInvariant))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
            ScanTelemetryFiles(latest ?? directory, telemetry, [".log", ".json", ".jsonl"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine("Nokto: local quota logs unavailable."); }
    }

    private static void ScanTelemetryFiles(string directory, AiLocalQuotaTelemetry telemetry, string[] extensions, bool recursive = true)
    {
        try
        {
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            int budget = 16 * MaxJsonBytes;
            var files = EnumerateTelemetryFiles(directory, extensions, recursive)
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(64).OrderBy(File.GetLastWriteTimeUtc);
            foreach (string path in files)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    bool json = Path.GetExtension(path) is ".json" or ".jsonc";
                    if (json && stream.Length > MaxJsonBytes) continue;
                    int count = (int)Math.Min(stream.Length, Math.Min(MaxJsonBytes, budget));
                    if (count == 0) continue;
                    bool tail = stream.Length > count;
                    if (tail) stream.Seek(-count, SeekOrigin.End);
                    byte[] bytes = new byte[count];
                    int read = 0;
                    while (read < count)
                    {
                        int received = stream.Read(bytes, read, count - read);
                        if (received == 0) break;
                        read += received;
                    }
                    budget -= count;
                    var observedAt = File.GetLastWriteTimeUtc(path);
                    string text = Encoding.UTF8.GetString(bytes, 0, read);
                    if (json) telemetry.ReadJson(text, observedAt);
                    else
                    {
                        if (tail && Path.GetExtension(path) is ".log" or ".jsonl")
                            text = text[(text.IndexOf('\n') + 1)..];
                        telemetry.ReadText(text, observedAt);
                        // Chromium may persist UTF-16LE strings beside binary LevelDB record headers.
                        if (Path.GetExtension(path) is ".log" or ".ldb")
                        {
                            telemetry.ReadText(Encoding.Unicode.GetString(bytes, 0, read & ~1), observedAt);
                            if (read > 1) telemetry.ReadText(Encoding.Unicode.GetString(bytes, 1, (read - 1) & ~1), observedAt);
                        }
                    }
                    if (budget <= 0) break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine("Nokto: local quota snapshot unavailable."); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine("Nokto: local quota folder unavailable."); }
    }

    private static IEnumerable<string> EnumerateTelemetryFiles(string directory, string[] extensions, bool recursive, int depth = 0)
    {
        if (depth > 12) yield break;
        foreach (string path in Directory.EnumerateFiles(directory))
            if (extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) yield return path;
        if (!recursive) yield break;
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(child);
            if (new[] { "assets", "skills", "node_modules", ".git", "plugins" }.Contains(name, StringComparer.OrdinalIgnoreCase) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string file in EnumerateTelemetryFiles(child, extensions, true, depth + 1)) yield return file;
        }
    }

    private string? FindAntigravityExecutable()
    {
        string localAppData = _localAppData;
        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "Antigravity IDE.exe"),
            Path.Combine(localAppData, "Programs", "Antigravity", "Antigravity.exe"),
            Path.Combine(localAppData, "antigravity", "Antigravity.exe"),
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "bin", "antigravity-ide.cmd")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private string? FindCodexExecutable()
    {
        try
        {
            var processes = Process.GetProcessesByName("codex");
            try
            {
                foreach (var running in processes)
                {
                    try
                    {
                        string? exe = running.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe)) return exe;
                    }
                    catch { }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch { }

        string localAppData = _localAppData;
        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "Codex", "Codex.exe"),
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe")
        ];

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;

        try
        {
            string openAiCodexBin = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            if (Directory.Exists(openAiCodexBin))
            {
                var match = Directory.GetFiles(openAiCodexBin, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (match != null) return match;
            }
        }
        catch { }

        return null;
    }

    private string? FindOpenCodeExecutable()
    {
        string localAppData = _localAppData;
        string userProfile = _userProfile;
        string[] candidates =
        [
            Path.Combine(localAppData, "opencode", "OpenCode.exe"),
            Path.Combine(localAppData, "Programs", "@opencode-aidesktop", "OpenCode.exe"),
            Path.Combine(localAppData, "Programs", "OpenCode", "opencode.exe"),
            Path.Combine(userProfile, "AppData", "Roaming", "npm", "opencode.cmd")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}
