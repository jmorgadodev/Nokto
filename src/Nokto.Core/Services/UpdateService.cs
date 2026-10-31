using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nokto.Core.Persistence;

namespace Nokto.Core.Services;

public record UpdateReleaseInfo
{
    public string TagName { get; init; } = "";
    public Version? Version { get; init; }
    public string Name { get; init; } = "";
    public string Body { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public bool IsUpdateAvailable { get; init; }
    public string? InstallerDownloadUrl { get; init; }
    public long InstallerSize { get; init; }
    public string? PortableDownloadUrl { get; init; }
    public long PortableSize { get; init; }
    public string? ChecksumsDownloadUrl { get; init; }
    public string RepositoryOwner { get; init; } = "jmorgadodev";
    public string RepositoryName { get; init; } = "Nokto";
}

public interface IUpdateService : IDisposable
{
    Version CurrentVersion { get; }
    bool IsPortable { get; }
    Task<UpdateReleaseInfo?> CheckForUpdatesAsync(string? owner = null, string? repo = null, CancellationToken cancellationToken = default);
    Task<string> DownloadUpdateAsync(UpdateReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    void ApplyUpdate(UpdateReleaseInfo release, string downloadedFilePath);
}

public class UpdateService : IUpdateService
{
    private const long MaxAssetBytes = 512L * 1024 * 1024;
    private const int MaxChecksumsBytes = 256 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _executablePath;
    private string? _verifiedPath;
    private byte[]? _verifiedHash;
    public Version CurrentVersion { get; }
    public bool IsPortable => DetectIsPortable(_executablePath);

    public UpdateService(StorageResolver? storageResolver = null, Version? currentVersion = null,
        HttpClient? httpClient = null, string? executablePath = null)
    {
        _ = storageResolver; // El directorio de datos legacy no determina la distribución.
        _executablePath = Path.GetFullPath(executablePath ?? GetExecutablePath());
        CurrentVersion = currentVersion ?? GetCurrentAssemblyVersion();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _ownsHttpClient = httpClient is null;
    }
    public void Dispose() { if (_ownsHttpClient) _httpClient.Dispose(); }
    public static Version GetCurrentAssemblyVersion()
    {
        var version = (Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetName().Version;
        return version != null ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) : new Version(1, 0, 1);
    }
    public static string GetExecutablePath() => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Nokto.exe");
    public static bool DetectIsPortable(string exePath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(dir, "portable.lock"))) return true;
        if (File.Exists(Path.Combine(dir, "installed.lock")) || File.Exists(Path.Combine(dir, "unins000.exe"))) return false;
        if (File.Exists(Path.Combine(dir, "config.json"))) return true;
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        static bool Under(string path, string root) => !string.IsNullOrEmpty(root) &&
            (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return !Under(dir, pf) && !Under(dir, pfx86);
    }
    private static bool ValidRepositoryPart(string value) => value.Length is > 0 and <= 100 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    public async Task<UpdateReleaseInfo?> CheckForUpdatesAsync(string? owner = null, string? repo = null, CancellationToken cancellationToken = default)
    {
        if (BuildIdentity.IsDevelopment) return null;
        string targetOwner = string.IsNullOrWhiteSpace(owner) || owner.Equals("nokto", StringComparison.OrdinalIgnoreCase) ? "jmorgadodev" : owner;
        string targetRepo = string.IsNullOrWhiteSpace(repo) ? "Nokto" : repo;
        if (!ValidRepositoryPart(targetOwner) || !ValidRepositoryPart(targetRepo)) throw new ArgumentException("Repositorio no válido.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{targetOwner}/{targetRepo}/releases/latest");
        request.Headers.UserAgent.ParseAdd("Nokto-UpdateClient");
        request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseReleaseJson(json, CurrentVersion) with { RepositoryOwner = targetOwner, RepositoryName = targetRepo };
    }
    public static UpdateReleaseInfo ParseReleaseJson(string json, Version currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string Text(JsonElement obj, string key) => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string tag = Text(root, "tag_name"), name = Text(root, "name");
        Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var version);
        string? installer = null, portable = null, checksums = null;
        long installerSize = 0, portableSize = 0;
        int portableRank = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                string assetName = Text(asset, "name"), url = Text(asset, "browser_download_url");
                long size = asset.TryGetProperty("size", out var value) && value.TryGetInt64(out long n) ? n : 0;
                if (assetName.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase)) checksums = url;
                else if (assetName.Equals("Nokto-Setup-x64.exe", StringComparison.OrdinalIgnoreCase) ||
                    assetName.EndsWith("-Setup-x64.exe", StringComparison.OrdinalIgnoreCase)) { installer = url; installerSize = size; }
                else
                {
                    int rank = assetName.Equals("Nokto-Portable-x64.exe", StringComparison.OrdinalIgnoreCase) ? 3 :
                        assetName.Equals("Nokto.exe", StringComparison.OrdinalIgnoreCase) ? 2 :
                        assetName.EndsWith("-Portable-x64.zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    if (rank > portableRank) { portable = url; portableSize = size; portableRank = rank; }
                }
            }
        return new UpdateReleaseInfo { TagName = tag, Version = version, Name = string.IsNullOrWhiteSpace(name) ? tag : name,
            Body = Text(root, "body"), HtmlUrl = Text(root, "html_url"), IsUpdateAvailable = version != null && version > currentVersion,
            InstallerDownloadUrl = installer, InstallerSize = installerSize, PortableDownloadUrl = portable, PortableSize = portableSize,
            ChecksumsDownloadUrl = checksums };
    }
    private static Uri ValidateAssetUrl(UpdateReleaseInfo release, string url)
    {
        if (!ValidRepositoryPart(release.RepositoryOwner) || !ValidRepositoryPart(release.RepositoryName) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("URL de actualización no válida.");
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length != 6 || !segments[0].Equals(release.RepositoryOwner, StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals(release.RepositoryName, StringComparison.OrdinalIgnoreCase) || segments[2] != "releases" ||
            segments[3] != "download" || segments[4] != release.TagName || segments[5].Contains('/') || segments[5].Contains('\\'))
            throw new InvalidDataException("El archivo no pertenece al release esperado.");
        return uri;
    }
    private async Task<HttpResponseMessage> GetAssetAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Nokto-UpdateClient");
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            var final = response.RequestMessage?.RequestUri;
            if (final is not null && (final.Scheme != Uri.UriSchemeHttps || !final.IsDefaultPort ||
                !((final.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && final.AbsoluteUri.Equals(uri.AbsoluteUri, StringComparison.Ordinal)) || final.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Redirección de descarga no válida.");
            return response;
        }
        catch { response.Dispose(); throw; }
    }
    private static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long limit, IProgress<double>? progress, long? total, CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024]; long count = 0; int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            count += read;
            if (count > limit) throw new InvalidDataException("El archivo de actualización supera el límite permitido.");
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (total is > 0) progress?.Report(Math.Min(1, (double)count / total.Value));
        }
        return count;
    }
    private async Task<byte[]?> ReadExpectedHashAsync(UpdateReleaseInfo release, string assetName, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(release.ChecksumsDownloadUrl)) return null;
        Uri uri = ValidateAssetUrl(release, release.ChecksumsDownloadUrl);
        if (!Uri.UnescapeDataString(uri.Segments[^1]).Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archivo de checksums no válido.");
        using var response = await GetAssetAsync(uri, token).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        await CopyBoundedAsync(input, output, MaxChecksumsBytes, null, null, token).ConfigureAwait(false);
        string text = Encoding.UTF8.GetString(output.ToArray());
        byte[]? found = null;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 67 || !char.IsWhiteSpace(trimmed[64])) continue;
            string filename = trimmed[65..].TrimStart().TrimStart('*');
            if (!filename.Equals(assetName, StringComparison.Ordinal)) continue;
            if (found is not null) throw new InvalidDataException("Checksum duplicado.");
            try { found = Convert.FromHexString(trimmed[..64]); }
            catch (FormatException) { throw new InvalidDataException("Checksum no válido."); }
        }
        return found ?? throw new InvalidDataException("Falta el checksum del archivo de actualización.");
    }
    private static void ValidatePe(string path, bool requireX64)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 256 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("El archivo descargado no es un ejecutable Windows.");
        stream.Position = 0x3c; int offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 24) throw new InvalidDataException("Cabecera PE no válida.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550) throw new InvalidDataException("Firma PE no válida.");
        ushort machine = reader.ReadUInt16();
        ushort sections = reader.ReadUInt16();
        stream.Position = offset + 20;
        ushort optionalSize = reader.ReadUInt16(), flags = reader.ReadUInt16();
        if ((machine != 0x8664 && (requireX64 || machine != 0x14c)) || sections == 0 || sections > 96 ||
            (flags & 0x0002) == 0 || optionalSize < 96 || offset + 24L + optionalSize + sections * 40L > stream.Length)
            throw new InvalidDataException("Cabecera PE no compatible.");
        ushort magic = reader.ReadUInt16();
        if (magic != (machine == 0x8664 ? 0x20b : 0x10b)) throw new InvalidDataException("Formato PE no compatible.");
    }
    public async Task<string> DownloadUpdateAsync(UpdateReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (BuildIdentity.IsDevelopment) throw new InvalidOperationException("Las actualizaciones públicas están deshabilitadas en Nokto DEV.");
        bool portable = IsPortable;
        string url = (portable ? release.PortableDownloadUrl : release.InstallerDownloadUrl)
            ?? throw new InvalidOperationException("No se encontró un archivo compatible con esta instalación.");
        Uri uri = ValidateAssetUrl(release, url);
        string assetName = Uri.UnescapeDataString(uri.Segments[^1]);
        bool zip = portable && assetName.EndsWith("-Portable-x64.zip", StringComparison.OrdinalIgnoreCase);
        if ((!portable && !assetName.EndsWith("-Setup-x64.exe", StringComparison.OrdinalIgnoreCase)) ||
            (portable && !zip && !assetName.Equals("Nokto.exe", StringComparison.OrdinalIgnoreCase) && !assetName.Equals("Nokto-Portable-x64.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Nombre de archivo de actualización no compatible.");
        byte[]? expected = await ReadExpectedHashAsync(release, assetName, cancellationToken).ConfigureAwait(false);
        string dir = portable ? Path.GetDirectoryName(_executablePath)! : Path.GetTempPath();
        string prefix = Path.Combine(dir, ".nokto-update-" + Guid.NewGuid().ToString("N"));
        string partial = prefix + ".part", candidate = prefix + ".exe";
        try
        {
            using var response = await GetAssetAsync(uri, cancellationToken).ConfigureAwait(false);
            long? contentLength = response.Content.Headers.ContentLength;
            long declared = portable ? release.PortableSize : release.InstallerSize;
            if (contentLength is > MaxAssetBytes || declared > MaxAssetBytes || declared < 0) throw new InvalidDataException("Archivo de actualización demasiado grande.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                long copied = await CopyBoundedAsync(input, output, MaxAssetBytes, progress, contentLength, cancellationToken).ConfigureAwait(false);
                if ((contentLength.HasValue && copied != contentLength.Value) || (declared > 0 && copied != declared))
                    throw new InvalidDataException("La descarga está incompleta o tiene un tamaño inesperado.");
            }
            await using (var stream = File.OpenRead(partial))
            {
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                if (expected is not null && !CryptographicOperations.FixedTimeEquals(hash, expected)) throw new InvalidDataException("El checksum de la actualización no coincide.");
            }
            if (zip)
            {
                using var archive = ZipFile.OpenRead(partial);
                var entries = archive.Entries.Where(e => e.FullName.Equals("Nokto.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (entries.Length != 1 || entries[0].Length > MaxAssetBytes) throw new InvalidDataException("El ZIP portable no contiene un único Nokto.exe válido.");
                await using var source = entries[0].Open();
                await using var dest = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                long extracted = await CopyBoundedAsync(source, dest, MaxAssetBytes, null, null, cancellationToken).ConfigureAwait(false);
                if (extracted != entries[0].Length) throw new InvalidDataException("ZIP portable incompleto.");
            }
            else File.Move(partial, candidate);
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePe(candidate, portable);
            await using var verified = File.OpenRead(candidate);
            _verifiedHash = await SHA256.HashDataAsync(verified, cancellationToken).ConfigureAwait(false);
            _verifiedPath = candidate;
            return candidate;
        }
        catch { try { File.Delete(candidate); } catch (IOException) { } throw; }
        finally { try { File.Delete(partial); } catch (IOException) { } }
    }
    public static string GenerateUpdateScriptContent() => GenerateUpdateScriptContent("Nokto.exe", "Nokto.exe.new", 0);
    public static string GenerateUpdateScriptContent(string executablePath, string downloadedPath, int processId)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        string backup = executablePath + ".nokto-backup-" + Guid.NewGuid().ToString("N");
        return "$ErrorActionPreference = 'Stop'\r\n" +
            "$exe = " + Quote(executablePath) + "\r\n$new = " + Quote(downloadedPath) + "\r\n$backup = " + Quote(backup) + "\r\n" +
            "$pidToWait = " + processId + "\r\n" +
            "try {\r\n  if ($pidToWait -gt 0 -and (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { try { Wait-Process -Id $pidToWait -Timeout 120 -ErrorAction Stop } catch { if (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { throw } } }\r\n" +
            "  $moved = $false\r\n  for ($i = 0; $i -lt 60; $i++) { try { Move-Item -LiteralPath $exe -Destination $backup -ErrorAction Stop; $moved = $true; break } catch { Start-Sleep -Milliseconds 500 } }\r\n  if (-not $moved) { throw 'No se pudo desbloquear el ejecutable.' }\r\n" +
            "  try { Move-Item -LiteralPath $new -Destination $exe -ErrorAction Stop } catch { Move-Item -LiteralPath $backup -Destination $exe -ErrorAction Stop; throw }\r\n" +
            "  try { Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ErrorAction Stop } catch { Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue; Move-Item -LiteralPath $backup -Destination $exe -ErrorAction Stop; throw }\r\n" +
            "  Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue\r\n" +
            "} finally { Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue }\r\n";
    }
    public void ApplyUpdate(UpdateReleaseInfo release, string downloadedFilePath)
    {
        if (BuildIdentity.IsDevelopment) throw new InvalidOperationException("Nokto DEV no puede aplicar actualizaciones públicas.");
        string path = Path.GetFullPath(downloadedFilePath);
        if (_verifiedPath is null || _verifiedHash is null || !path.Equals(_verifiedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La descarga no fue verificada en esta sesión.");
        ValidatePe(path, IsPortable);
        using (var stream = File.OpenRead(path))
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), _verifiedHash)) throw new InvalidDataException("La descarga cambió después de verificarse.");
        ProcessStartInfo psi;
        if (IsPortable)
        {
            string script = Path.Combine(Path.GetTempPath(), "nokto-update-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(script, GenerateUpdateScriptContent(_executablePath, path, Environment.ProcessId), new UTF8Encoding(true));
            psi = new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive"); psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass"); psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(script);
            try { if (Process.Start(psi) is null) throw new InvalidOperationException("No se pudo iniciar el actualizador."); }
            catch { File.Delete(script); throw; }
        }
        else
        {
            psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
            if (Process.Start(psi) is null) throw new InvalidOperationException("No se pudo iniciar el instalador.");
        }
        Environment.Exit(0);
    }
}
