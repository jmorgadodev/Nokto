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
        _baseDirectory = Path.GetFullPath(customBaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory);

        // An explicit location is an isolation boundary, including for tests. Never
        // redirect a failed custom path to the user's installed application data.
        if (customBaseDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(customBaseDirectory);
            Directory.CreateDirectory(_baseDirectory);
            if (!IsDirectoryWritable(_baseDirectory))
                throw new IOException($"No se puede escribir en el almacenamiento indicado: {_baseDirectory}");
            _isPortableMode = true;
            _dataDirectory = Path.Combine(_baseDirectory, "data");
            EnsureDirectoriesExist();
            return;
        }

        // Detección nativa de modo portable:
        // 1. Archivo indicador explícito (portable.lock o config.json en el directorio base)
        // 2. O si el directorio base es escribible y no se encuentra en las rutas del sistema Program Files
        bool hasPortableLock = File.Exists(Path.Combine(_baseDirectory, "portable.lock"));
        bool hasConfigAdjacent = File.Exists(Path.Combine(_baseDirectory, "config.json"));
        bool hasInstalledMarker = File.Exists(Path.Combine(_baseDirectory, "installed.lock")) ||
            File.Exists(Path.Combine(_baseDirectory, "unins000.exe"));
        bool hasLegacyData = File.Exists(Path.Combine(_baseDirectory, "data", "config.json")) ||
            File.Exists(Path.Combine(_baseDirectory, "data", "presets.json"));

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

        _isPortableMode = ResolvePortableMode(Nokto.Core.BuildIdentity.IsDevelopment, hasPortableLock,
            hasInstalledMarker, hasConfigAdjacent, hasLegacyData, isWritable);

        _dataDirectory = _isPortableMode
            ? Path.Combine(_baseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nokto");

        EnsureDirectoriesExist();
    }

    /// <summary>Pure storage policy, separate from distribution/update identity.</summary>
    public static bool ResolvePortableMode(bool development, bool portableMarker, bool installedMarker,
        bool adjacentConfiguration, bool legacyData, bool writableOutsideProgramFiles) =>
        development || portableMarker || (installedMarker
            ? legacyData // Older per-user installs kept data beside the executable.
            : adjacentConfiguration || writableOutsideProgramFiles);

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
