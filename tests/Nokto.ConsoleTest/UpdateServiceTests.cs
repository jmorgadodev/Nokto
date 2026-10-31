using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Nokto.Core.Services;

namespace Nokto.ConsoleTest;

public static class UpdateServiceTests
{
    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<double>
    {
        public void Report(double value) => source.Cancel();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void ExpectFailure(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or OperationCanceledException) { return; }
        throw new InvalidOperationException(message);
    }
    private static byte[] Pe(bool x64 = true)
    {
        var bytes = new byte[512];
        using var stream = new MemoryStream(bytes); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x5a4d); stream.Position = 0x3c; writer.Write(128);
        stream.Position = 128; writer.Write(0x00004550u); writer.Write((ushort)(x64 ? 0x8664 : 0x14c)); writer.Write((ushort)1);
        stream.Position = 148; writer.Write((ushort)112); writer.Write((ushort)2); writer.Write((ushort)(x64 ? 0x20b : 0x10b));
        return bytes;
    }
    private const string Root = "https://github.com/jmorgadodev/Nokto/releases/download/v2.0.0/";
    private static UpdateReleaseInfo Release(string name, long size, bool installed = false, bool checksums = true) => new()
    {
        TagName = "v2.0.0", PortableDownloadUrl = installed ? null : Root + name, PortableSize = size,
        InstallerDownloadUrl = installed ? Root + name : null, InstallerSize = installed ? size : 0,
        ChecksumsDownloadUrl = checksums ? Root + "SHA256SUMS" : null
    };
    public static void RunAll()
    {
        var json = """
        { "tag_name":"v2.0.0", "assets":[
          {"name":"Nokto-v1.0.1-Portable-x64.zip","browser_download_url":"https://github.com/jmorgadodev/Nokto/releases/download/v2.0.0/Nokto-v1.0.1-Portable-x64.zip","size":4},
          {"name":"Nokto.exe","browser_download_url":"https://github.com/jmorgadodev/Nokto/releases/download/v2.0.0/Nokto.exe","size":5},
          {"name":"Nokto-Portable-x64.exe","browser_download_url":"https://github.com/jmorgadodev/Nokto/releases/download/v2.0.0/Nokto-Portable-x64.exe","size":6},
          {"name":"Nokto-Setup-x64.exe","browser_download_url":"https://github.com/jmorgadodev/Nokto/releases/download/v2.0.0/Nokto-Setup-x64.exe","size":7},
          {"name":"SHA256SUMS","browser_download_url":"https://github.com/jmorgadodev/Nokto/releases/download/v2.0.0/SHA256SUMS","size":8}] }
        """;
        var parsed = UpdateService.ParseReleaseJson(json, new Version(1, 0, 1));
        Check(parsed.IsUpdateAvailable && parsed.PortableSize == 6 && parsed.InstallerSize == 7 && parsed.ChecksumsDownloadUrl != null, "Selección o comparación de release incorrecta.");
        Check(!UpdateService.ParseReleaseJson(json, new Version(2, 0, 0)).IsUpdateAvailable, "Misma versión no es actualización.");
        string script = UpdateService.GenerateUpdateScriptContent(@"C:\Mi carpeta\Nokto's.exe", @"C:\Mi carpeta\staged.exe", 321);
        Check(script.Contains("Nokto''s.exe") && script.Contains("Wait-Process") && script.Contains("321") && script.Contains("Move-Item -LiteralPath $backup -Destination $exe") && script.Contains("$PSCommandPath"), "Script no conserva ruta, espera o rollback.");
        string dir = Path.Combine(Path.GetTempPath(), "nokto-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string exe = Path.Combine(dir, "Nokto.exe");
            File.WriteAllBytes(exe, Pe());
            File.WriteAllText(Path.Combine(dir, "installed.lock"), "");
            Check(!UpdateService.DetectIsPortable(exe), "Marcador instalado ignorado.");
            File.WriteAllText(Path.Combine(dir, "portable.lock"), "");
            Check(UpdateService.DetectIsPortable(exe), "Marcador portable no prevalece.");
            File.Delete(Path.Combine(dir, "portable.lock")); File.Delete(Path.Combine(dir, "installed.lock"));
            File.WriteAllText(Path.Combine(dir, "unins000.exe"), "");
            Check(!UpdateService.DetectIsPortable(exe), "Desinstalador legacy ignorado.");
            File.Delete(Path.Combine(dir, "unins000.exe")); File.WriteAllText(Path.Combine(dir, "portable.lock"), "");
            byte[] pe = Pe(); string name = "Nokto-Portable-x64.exe";
            byte[] content = pe; string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            string sums = $"{hash}  {name}\n";
            using var client = new HttpClient(new Handler(req => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = req.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS") ? new StringContent(sums) : new ByteArrayContent(content) }));
            using var service = new UpdateService(currentVersion: new Version(1, 0, 1), httpClient: client, executablePath: exe);
            var rel = Release(name, pe.Length);
            string result = service.DownloadUpdateAsync(rel).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(result).SequenceEqual(pe) && File.ReadAllBytes(exe).SequenceEqual(pe), "Descarga válida o archivo original incorrectos.");
            File.Delete(result);
            sums = new string('0', 64) + "  " + name + "\n";
            ExpectFailure(() => service.DownloadUpdateAsync(rel).GetAwaiter().GetResult(), "Checksum incorrecto fue aceptado.");
            sums = hash + "  otro.exe\n";
            ExpectFailure(() => service.DownloadUpdateAsync(rel).GetAwaiter().GetResult(), "Falta de checksum fue aceptada.");
            sums = hash + "  " + name + "\n";
            ExpectFailure(() => service.DownloadUpdateAsync(rel with { PortableSize = pe.Length + 1 }).GetAwaiter().GetResult(), "Tamaño declarado incorrecto fue aceptado.");
            ExpectFailure(() => service.DownloadUpdateAsync(rel with { PortableSize = 513L * 1024 * 1024 }).GetAwaiter().GetResult(), "Archivo excesivo fue aceptado.");
            content = [1, 2, 3];
            ExpectFailure(() => service.DownloadUpdateAsync(rel with { PortableSize = 3, ChecksumsDownloadUrl = null }).GetAwaiter().GetResult(), "Archivo no PE fue aceptado.");
            ExpectFailure(() => service.DownloadUpdateAsync(rel with { PortableDownloadUrl = "https://evil.example/Nokto.exe" }).GetAwaiter().GetResult(), "Host ajeno fue aceptado.");
            ExpectFailure(() => service.DownloadUpdateAsync(rel with { PortableDownloadUrl = Root.Replace("/Nokto/", "/Otro/") + name }).GetAwaiter().GetResult(), "Repositorio ajeno fue aceptado.");
            ExpectFailure(() => service.DownloadUpdateAsync(rel with { PortableDownloadUrl = null }).GetAwaiter().GetResult(), "Se utilizó otra distribución como fallback.");
            using (var archiveStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
                using (var entry = archive.CreateEntry("Nokto.exe").Open()) entry.Write(pe);
                content = archiveStream.ToArray();
            }
            string zipName = "Nokto-v1.0.1-Portable-x64.zip";
            sums = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() + "  " + zipName + "\n";
            string unpacked = service.DownloadUpdateAsync(Release(zipName, content.Length)).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(unpacked).SequenceEqual(pe), "ZIP legacy no extrajo Nokto.exe."); File.Delete(unpacked);
            using var cancelled = new CancellationTokenSource();
            content = new byte[128 * 1024]; pe.CopyTo(content, 0);
            ExpectFailure(() => service.DownloadUpdateAsync(Release("Nokto-Portable-x64.exe", content.Length, checksums: false), new CancelProgress(cancelled), cancelled.Token).GetAwaiter().GetResult(), "Cancelación durante descarga ignorada.");
            Check(!Directory.EnumerateFiles(dir, ".nokto-update-*").Any(), "Descarga parcial no limpiada.");
            File.Delete(Path.Combine(dir, "portable.lock")); File.WriteAllText(Path.Combine(dir, "installed.lock"), "");
            content = Pe(false); name = "Nokto-Setup-x64.exe";
            sums = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() + "  " + name + "\n";
            string setup = service.DownloadUpdateAsync(Release(name, content.Length, installed: true)).GetAwaiter().GetResult();
            Check(File.Exists(setup), "Instalador PE x86 no aceptado."); File.Delete(setup);
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("[PASS] Actualizador: versiones, marcadores, SHA256, PE, ZIP y descargas sintéticas.");
    }
}
