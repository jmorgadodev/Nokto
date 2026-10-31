using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Collections.Concurrent;
using Nokto.Core.Models;
using Nokto.Core.Serialization;

namespace Nokto.Core.Persistence;

/// <summary>
/// Servicio de persistencia basado en System.Text.Json con contexto AOT (NoktoJsonContext).
/// Soporta config.json, presets.json y audit.jsonl (append-only secuencial).
/// </summary>
public sealed class PersistenceService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly StorageResolver _storageResolver;
    private readonly object _auditLock = new();

    public StorageResolver Storage => _storageResolver;

    public PersistenceService(StorageResolver? storageResolver = null)
    {
        _storageResolver = storageResolver ?? new StorageResolver();
    }

    public AppConfig LoadConfig()
    {
        string path = _storageResolver.ConfigFilePath;
        lock (FileLocks.GetOrAdd(path, _ => new()))
        {
            if (!File.Exists(path))
            {
                var defaultConfig = new AppConfig();
                SaveConfig(defaultConfig);
                return defaultConfig;
            }
            return NormalizeConfig(ReadRecoverable(path, NoktoJsonContext.Default.AppConfig) ?? new AppConfig());
        }
    }

    public void SaveConfig(AppConfig config)
    {
        string path = _storageResolver.ConfigFilePath;
        WriteAtomic(path, NormalizeConfig(config), NoktoJsonContext.Default.AppConfig);
    }

    public PresetsFile LoadPresets()
    {
        string path = _storageResolver.PresetsFilePath;
        lock (FileLocks.GetOrAdd(path, _ => new()))
        {
            if (!File.Exists(path))
            {
                var defaultPresets = CreateDefaultPresets();
                SavePresets(defaultPresets);
                return defaultPresets;
            }
            bool recovered;
            var file = NormalizePresets(ReadRecoverable(path, NoktoJsonContext.Default.PresetsFile, out recovered) ?? CreateDefaultPresets());
            bool mutated = false;
            foreach (var defPreset in Presets.GetDefaultPresets())
            {
                int existingIdx = file.Presets.FindIndex(p => p.Id == defPreset.Id);
                if (existingIdx >= 0)
                {
                    var p = file.Presets[existingIdx];
                    if (!p.IsSystemPreset)
                    {
                        file.Presets[existingIdx] = p with { IsSystemPreset = true };
                        mutated = true;
                    }
                }
                else
                {
                    file.Presets.Add(defPreset with { IsSystemPreset = true });
                    mutated = true;
                }
            }
            // Recovery is read-only: leave the damaged original available for repair.
            if (mutated && !recovered) SavePresets(file);
            return file;
        }
    }

    public void SavePresets(PresetsFile presetsFile)
    {
        string path = _storageResolver.PresetsFilePath;
        WriteAtomic(path, NormalizePresets(presetsFile), NoktoJsonContext.Default.PresetsFile);
    }

    private static T? ReadRecoverable<T>(string path, JsonTypeInfo<T> type) where T : class =>
        ReadRecoverable(path, type, out _);

    private static T? ReadRecoverable<T>(string path, JsonTypeInfo<T> type, out bool recovered) where T : class
    {
        recovered = false;
        try { return DeserializeRequired(File.ReadAllBytes(path), type); }
        catch (JsonException)
        {
            recovered = true;
            string backup = path + ".bak";
            if (!File.Exists(backup)) return null;
            try { return DeserializeRequired(File.ReadAllBytes(backup), type); }
            catch (JsonException) { return null; }
        }
    }

    private static T DeserializeRequired<T>(byte[] bytes, JsonTypeInfo<T> type) where T : class =>
        JsonSerializer.Deserialize(bytes, type) ?? throw new JsonException("El documento no puede ser null.");

    private static void WriteAtomic<T>(string path, T value, JsonTypeInfo<T> type) where T : class
    {
        lock (FileLocks.GetOrAdd(path, _ => new()))
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (!File.Exists(path)) File.Move(temporary, path);
                else
                {
                    string? backup = path + ".bak";
                    try { _ = DeserializeRequired(File.ReadAllBytes(path), type); }
                    catch (JsonException)
                    {
                        // Preserve damaged bytes before replacing them, and retain any
                        // previous healthy backup instead of replacing it with corruption.
                        File.Copy(path, path + ".corrupt." + Guid.NewGuid().ToString("N"));
                        backup = null;
                    }
                    File.Replace(temporary, path, backup);
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static AppConfig NormalizeConfig(AppConfig config)
    {
        config.Settings ??= new();
        config.KeepAlive ??= new();
        config.LanServer ??= new();
        config.Island ??= new();
        config.Settings.HiddenAiEnvironmentIds = config.Settings.HiddenAiEnvironmentIds?.Where(x => x is not null).ToList() ?? [];
        config.Settings.AiEnvironmentOrder = config.Settings.AiEnvironmentOrder?.Where(x => x is not null).ToList() ?? ["codex"];
        config.Island.ShelfFiles = config.Island.ShelfFiles?.Where(x => x is not null).ToList() ?? [];
        config.Island.QuickFolders = config.Island.QuickFolders?.Where(x => x is not null).ToList() ?? [];
        config.Island.PageOrder ??= [0, 1, 2];
        if (config.Island.ContentOrder is { } order)
            foreach (int key in order.Keys.ToArray()) order[key] = order[key]?.Where(x => x is not null).ToList() ?? [];
        config.Island.Productivity ??= new();
        config.Island.Productivity.Alarms = config.Island.Productivity.Alarms?.Where(x => x is not null).ToList() ?? [];
        config.Island.Productivity.Tasks = config.Island.Productivity.Tasks?.Where(x => x is not null).ToList() ?? [];
        return config;
    }

    private static PresetsFile NormalizePresets(PresetsFile file) => file with
    {
        Presets = (file.Presets ?? []).Where(p => p is not null).Select(p => p with
        {
            Trigger = p.Trigger ?? new() { Type = TriggerType.Manual },
            Pipeline = (p.Pipeline ?? []).Where(step => step is not null).ToList(),
            Actions = p.Actions is null ? null : new(p.Actions.Where(step => step is not null)),
            TerminalAction = p.TerminalAction ?? new()
        }).ToList()
    };

    public void AppendAuditLog(AuditLogEntry entry)
    {
        lock (_auditLock)
        {
            string path = _storageResolver.AuditLogFilePath;
            string jsonLine = JsonSerializer.Serialize(entry, NoktoCompactJsonContext.Default.AuditLogEntry);
            // Append line with UTF-8 strict without BOM (strict single line JSONL)
            File.AppendAllText(path, jsonLine + Environment.NewLine, Utf8NoBom);
        }
    }

    public List<AuditLogEntry> ReadRecentAuditLogs(int maxCount = 50)
    {
        lock (_auditLock)
        {
            string path = _storageResolver.AuditLogFilePath;
            if (!File.Exists(path)) return [];

            var list = new List<AuditLogEntry>();
            try
            {
                var lines = File.ReadAllLines(path, Utf8NoBom);
                int count = 0;
                for (int i = lines.Length - 1; i >= 0 && count < maxCount; i--)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var entry = JsonSerializer.Deserialize(line, NoktoCompactJsonContext.Default.AuditLogEntry);
                        if (entry != null)
                        {
                            list.Add(entry);
                            count++;
                        }
                    }
                    catch
                    {
                        // Skip corrupted/multiline lines
                    }
                }
            }
            catch
            {
                // Graceful fallback
            }

            return list;
        }
    }

    public static PresetsFile CreateDefaultPresets() => PresetsFile.CreateDefault();
}
