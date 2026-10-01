namespace Nokto.Core.Persistence;

/// <summary>
/// Resuelve de forma determinista las rutas de almacenamiento según el modo de ejecución (Portable vs Instalado).
/// </summary>
public sealed class StorageResolver
{
    private readonly string _baseDirectory;
    private readonly bool _isPortableMode;
    private readonly string _dataDirectory;

    public bool IsPortableMode => _isPortableMode;
    public string DataDirectory => _dataDirectory;
    public string ConfigFilePath => Path.Combine(_dataDirectory, "config.json");
    public string PresetsFilePath => Path.Combine(_dataDirectory, "presets.json");
    public string AuditLogFilePath => Path.Combine(_dataDirectory, "audit.jsonl");
    public string SnapshotsDirectory => Path.Combine(_dataDirectory, "snapshots");

    public StorageResolver(string? customBaseDirectory = null)
    {
        _baseDirectory = customBaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

        // Detección nativa de modo portable:
        // 1. Archivo indicador explícito (portable.lock o config.json en el directorio base)
        // 2. O si el directorio base es escribible y no se encuentra en las rutas del sistema Program Files
        bool hasPortableLock = File.Exists(Path.Combine(_baseDirectory, "portable.lock"));
        bool hasConfigAdjacent = File.Exists(Path.Combine(_baseDirectory, "config.json"));

        bool isProgramFiles = false;
        try
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf) && _baseDirectory.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
                isProgramFiles = true;
            if (!string.IsNullOrEmpty(pfx86) && _baseDirectory.StartsWith(pfx86, StringComparison.OrdinalIgnoreCase))
                isProgramFiles = true;
        }
        catch { }

        bool isWritable = !isProgramFiles && IsDirectoryWritable(_baseDirectory);

        _isPortableMode = hasPortableLock || hasConfigAdjacent || isWritable;

        _dataDirectory = _isPortableMode
            ? Path.Combine(_baseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nokto");

        EnsureDirectoriesExist();
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            string testFile = Path.Combine(dir, ".perm_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(testFile, "1");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }

        if (!Directory.Exists(SnapshotsDirectory))
        {
            Directory.CreateDirectory(SnapshotsDirectory);
        }
    }
}
