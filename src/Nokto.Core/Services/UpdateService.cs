using System.Diagnostics;
using System.Reflection;
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
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly StorageResolver? _storageResolver;

    public Version CurrentVersion { get; }
    public bool IsPortable => _storageResolver?.IsPortableMode ?? DetectIsPortable(GetExecutablePath());

    public UpdateService(
        StorageResolver? storageResolver = null,
        Version? currentVersion = null,
        HttpClient? httpClient = null)
    {
        _storageResolver = storageResolver;
        CurrentVersion = currentVersion ?? GetCurrentAssemblyVersion();

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Nokto-UpdateClient");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
            _ownsHttpClient = true;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static Version GetCurrentAssemblyVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var version = asm.GetName().Version;
        return version != null ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) : new Version(1, 0, 4);
    }

    public static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Nokto.exe");
    }

    public static bool DetectIsPortable(string exePath)
    {
        try
        {
            string baseDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(baseDir, "portable.lock")) || File.Exists(Path.Combine(baseDir, "config.json")))
                return true;

            if (baseDir.Contains("artifacts", StringComparison.OrdinalIgnoreCase) &&
                baseDir.Contains("Portable", StringComparison.OrdinalIgnoreCase))
                return true;

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf) && baseDir.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(pfx86) && baseDir.StartsWith(pfx86, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<UpdateReleaseInfo?> CheckForUpdatesAsync(string? owner = null, string? repo = null, CancellationToken cancellationToken = default)
    {
        string targetOwner = string.IsNullOrWhiteSpace(owner) ? "nokto" : owner;
        string targetRepo = string.IsNullOrWhiteSpace(repo) ? "Nokto" : repo;
        string url = $"https://api.github.com/repos/{targetOwner}/{targetRepo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            request.Headers.Add("User-Agent", "Nokto-UpdateClient");
        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            request.Headers.Add("Accept", "application/vnd.github.v3+json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseReleaseJson(json, CurrentVersion);
    }

    public static UpdateReleaseInfo ParseReleaseJson(string json, Version currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";
        string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
        string htmlUrl = root.TryGetProperty("html_url", out var htmlElem) ? htmlElem.GetString() ?? "" : "";

        string cleanTag = tagName.TrimStart('v', 'V').Trim();
        Version? releaseVersion = null;
        if (Version.TryParse(cleanTag, out var parsed))
        {
            releaseVersion = parsed;
        }

        bool isUpdate = releaseVersion != null && releaseVersion > currentVersion;

        string? installerUrl = null;
        long installerSize = 0;
        string? portableUrl = null;
        long portableSize = 0;

        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElem.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                string downloadUrl = asset.TryGetProperty("browser_download_url", out var aUrl) ? aUrl.GetString() ?? "" : "";
                long size = asset.TryGetProperty("size", out var aSize) ? aSize.GetInt64() : 0;

                if (assetName.EndsWith("-Setup-x64.exe", StringComparison.OrdinalIgnoreCase) ||
                    assetName.Equals("Nokto-Setup-x64.exe", StringComparison.OrdinalIgnoreCase) ||
                    assetName.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = downloadUrl;
                    installerSize = size;
                }
                else if (assetName.Equals("Nokto.exe", StringComparison.OrdinalIgnoreCase) ||
                         (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !assetName.Contains("setup", StringComparison.OrdinalIgnoreCase)))
                {
                    portableUrl = downloadUrl;
                    portableSize = size;
                }
                else if (portableUrl == null && assetName.EndsWith("-Portable-x64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    portableUrl = downloadUrl;
                    portableSize = size;
                }
            }
        }

        return new UpdateReleaseInfo
        {
            TagName = tagName,
            Version = releaseVersion,
            Name = string.IsNullOrWhiteSpace(name) ? tagName : name,
            Body = body,
            HtmlUrl = htmlUrl,
            IsUpdateAvailable = isUpdate,
            InstallerDownloadUrl = installerUrl,
            InstallerSize = installerSize,
            PortableDownloadUrl = portableUrl,
            PortableSize = portableSize
        };
    }

    public async Task<string> DownloadUpdateAsync(UpdateReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        bool portable = IsPortable;
        string? downloadUrl = portable ? release.PortableDownloadUrl : release.InstallerDownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            downloadUrl = release.PortableDownloadUrl ?? release.InstallerDownloadUrl;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("No se encontró ningún binario compatible en los activos del release.");
        }

        string destinationPath;
        if (portable)
        {
            string currentExe = GetExecutablePath();
            string exeDir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
            destinationPath = Path.Combine(exeDir, "Nokto.exe.new");
        }
        else
        {
            destinationPath = Path.Combine(Path.GetTempPath(), "Nokto-Setup-x64.exe");
        }

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, useAsync: true);

        byte[] buffer = new byte[16384];
        long totalRead = 0;
        int read;
        while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            totalRead += read;
            if (totalBytes.HasValue && totalBytes.Value > 0 && progress != null)
            {
                progress.Report(Math.Min(1.0, (double)totalRead / totalBytes.Value));
            }
        }

        return destinationPath;
    }

    public static string GenerateUpdateScriptContent()
    {
        return "@echo off\r\ntimeout /t 1 /nobreak > nul\r\nmove /y \"Nokto.exe.new\" \"Nokto.exe\"\r\nstart \"\" \"Nokto.exe\"\r\ndel \"%~f0\"\r\n";
    }

    public void ApplyUpdate(UpdateReleaseInfo release, string downloadedFilePath)
    {
        if (IsPortable)
        {
            string currentExe = GetExecutablePath();
            string exeDir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(Path.GetTempPath(), "nokto_update.cmd");

            File.WriteAllText(scriptPath, GenerateUpdateScriptContent());

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                WorkingDirectory = exeDir,
                UseShellExecute = true,
                CreateNoWindow = true
            };

            Process.Start(psi);
            Environment.Exit(0);
        }
        else
        {
            var psi = new ProcessStartInfo
            {
                FileName = downloadedFilePath,
                UseShellExecute = true
            };

            Process.Start(psi);
            Environment.Exit(0);
        }
    }
}
