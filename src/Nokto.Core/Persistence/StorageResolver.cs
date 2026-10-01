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

        // Regla 1.1: Verifica si existe portable.lock o config.json en el directorio base
        bool hasPortableLock = File.Exists(Path.Combine(_baseDirectory, "portable.lock"));
        bool hasConfigAdjacent = File.Exists(Path.Combine(_baseDirectory, "config.json"));

        _isPortableMode = hasPortableLock || hasConfigAdjacent;

        _dataDirectory = _isPortableMode
            ? Path.Combine(_baseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nokto");

        EnsureDirectoriesExist();
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
