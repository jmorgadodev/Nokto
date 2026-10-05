using System.Text;
using System.Text.Json;
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
        if (!File.Exists(path))
        {
            var defaultConfig = new AppConfig();
            SaveConfig(defaultConfig);
            return defaultConfig;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var config = JsonSerializer.Deserialize(bytes, NoktoJsonContext.Default.AppConfig);
            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void SaveConfig(AppConfig config)
    {
        string path = _storageResolver.ConfigFilePath;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(config, NoktoJsonContext.Default.AppConfig);
        File.WriteAllBytes(path, bytes);
    }

    public PresetsFile LoadPresets()
    {
        string path = _storageResolver.PresetsFilePath;
        if (!File.Exists(path))
        {
            var defaultPresets = CreateDefaultPresets();
            SavePresets(defaultPresets);
            return defaultPresets;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var file = JsonSerializer.Deserialize(bytes, NoktoJsonContext.Default.PresetsFile);
            if (file != null && file.Presets != null && file.Presets.Count > 0)
            {
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
                if (mutated)
                {
                    SavePresets(file);
                }
                return file;
            }
            var def = CreateDefaultPresets();
            SavePresets(def);
            return def;
        }
        catch
        {
            var def = CreateDefaultPresets();
            SavePresets(def);
            return def;
        }
    }

    public void SavePresets(PresetsFile presetsFile)
    {
        string path = _storageResolver.PresetsFilePath;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(presetsFile, NoktoJsonContext.Default.PresetsFile);
        File.WriteAllBytes(path, bytes);
    }

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
