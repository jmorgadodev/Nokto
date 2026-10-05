using System.Net;
using System.Text;
using Nokto.Core.Models;
using Nokto.Core.Persistence;
using Nokto.Core.Services;

namespace Nokto.ConsoleTest;

public static class UpdateServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    public static void RunAll()
    {
        TestVersionComparison();
        TestJsonParsingWithAssets();
        TestScriptGeneration();
        TestPortableDetection();
        TestMockApiSuccess();
        TestMockApiErrorGraceful();
        Console.WriteLine("[PASS] Actualizador: comparación semver, parseo GitHub API, assets de release, script de hot swap portable y tolerancia a fallos.");
    }

    private static void TestVersionComparison()
    {
        var v104 = new Version(1, 0, 4);

        string jsonHigher = """
        {
            "tag_name": "v1.0.5",
            "name": "Nokto v1.0.5 - UI Refinements",
            "body": "- Nueva interfaz de rutinas con sticky footer\r\n- Módulo de actualizaciones",
            "html_url": "https://github.com/nokto/Nokto/releases/tag/v1.0.5",
            "assets": []
        }
        """;
        var relHigher = UpdateService.ParseReleaseJson(jsonHigher, v104);
        if (!relHigher.IsUpdateAvailable) throw new InvalidOperationException("1.0.5 debería considerarse actualización respecto a 1.0.4");
        if (relHigher.TagName != "v1.0.5") throw new InvalidOperationException("Tag name incorrecto");

        string jsonSame = """
        {
            "tag_name": "v1.0.4",
            "name": "Nokto v1.0.4",
            "body": "Línea base",
            "html_url": "https://github.com/nokto/Nokto/releases/tag/v1.0.4",
            "assets": []
        }
        """;
        var relSame = UpdateService.ParseReleaseJson(jsonSame, v104);
        if (relSame.IsUpdateAvailable) throw new InvalidOperationException("1.0.4 no debería considerarse actualización respecto a 1.0.4");

        string jsonOlder = """
        {
            "tag_name": "v1.0.3",
            "name": "Nokto v1.0.3",
            "body": "Anterior",
            "html_url": "https://github.com/nokto/Nokto/releases/tag/v1.0.3",
            "assets": []
        }
        """;
        var relOlder = UpdateService.ParseReleaseJson(jsonOlder, v104);
        if (relOlder.IsUpdateAvailable) throw new InvalidOperationException("1.0.3 no debería considerarse actualización respecto a 1.0.4");

        // Sin prefijo 'v'
        string jsonNoPrefix = """
        {
            "tag_name": "1.0.6",
            "name": "Nokto 1.0.6",
            "body": "Notas",
            "html_url": "https://github.com/nokto/Nokto/releases/tag/1.0.6",
            "assets": []
        }
        """;
        var relNoPrefix = UpdateService.ParseReleaseJson(jsonNoPrefix, v104);
        if (!relNoPrefix.IsUpdateAvailable || relNoPrefix.Version != new Version(1, 0, 6))
            throw new InvalidOperationException("Debe parsear tags sin prefijo v.");
    }

    private static void TestJsonParsingWithAssets()
    {
        string json = """
        {
            "tag_name": "v1.0.5",
            "name": "Nokto v1.0.5",
            "body": "Novedades de la versión",
            "html_url": "https://github.com/nokto/Nokto/releases/tag/v1.0.5",
            "assets": [
                {
                    "name": "Nokto-Setup-x64.exe",
                    "browser_download_url": "https://github.com/nokto/Nokto/releases/download/v1.0.5/Nokto-Setup-x64.exe",
                    "size": 55123456
                },
                {
                    "name": "Nokto.exe",
                    "browser_download_url": "https://github.com/nokto/Nokto/releases/download/v1.0.5/Nokto.exe",
                    "size": 52123456
                }
            ]
        }
        """;

        var rel = UpdateService.ParseReleaseJson(json, new Version(1, 0, 4));
        if (rel.InstallerDownloadUrl != "https://github.com/nokto/Nokto/releases/download/v1.0.5/Nokto-Setup-x64.exe")
            throw new InvalidOperationException("No se resolvió el instalador correctamente.");
        if (rel.InstallerSize != 55123456)
            throw new InvalidOperationException("Tamaño del instalador inconsistente.");

        if (rel.PortableDownloadUrl != "https://github.com/nokto/Nokto/releases/download/v1.0.5/Nokto.exe")
            throw new InvalidOperationException("No se resolvió el binario portable correctamente.");
        if (rel.PortableSize != 52123456)
            throw new InvalidOperationException("Tamaño del portable inconsistente.");
    }

    private static void TestScriptGeneration()
    {
        string script = UpdateService.GenerateUpdateScriptContent();
        if (!script.Contains("timeout /t 1 /nobreak > nul"))
            throw new InvalidOperationException("El script debe incluir timeout de espera.");
        if (!script.Contains("move /y \"Nokto.exe.new\" \"Nokto.exe\""))
            throw new InvalidOperationException("El script debe reemplazar Nokto.exe.");
        if (!script.Contains("start \"\" \"Nokto.exe\""))
            throw new InvalidOperationException("El script debe relanzar Nokto.exe.");
        if (!script.Contains("del \"%~f0\""))
            throw new InvalidOperationException("El script debe eliminarse tras ejecutar.");
    }

    private static void TestPortableDetection()
    {
        bool isPortableDir = UpdateService.DetectIsPortable(@"C:\Users\jorge\Proyectos\Nokto\artifacts\Nokto-Portable-x64\Nokto.exe");
        if (!isPortableDir)
            throw new InvalidOperationException("Debería detectar portable en directorio de artifacts.");

        string progFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nokto", "Nokto.exe");
        bool isInstalled = UpdateService.DetectIsPortable(progFilesPath);
        if (isInstalled)
            throw new InvalidOperationException("No debería marcar Program Files como portable.");
    }

    private static void TestMockApiSuccess()
    {
        string json = """
        {
            "tag_name": "v1.0.5",
            "name": "Nokto v1.0.5",
            "body": "Notas",
            "html_url": "https://github.com/nokto/Nokto/releases/tag/v1.0.5",
            "assets": [
                {
                    "name": "Nokto.exe",
                    "browser_download_url": "https://example.com/Nokto.exe",
                    "size": 1024
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Headers.UserAgent.Count == 0)
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        using var service = new UpdateService(currentVersion: new Version(1, 0, 4), httpClient: client);

        var release = service.CheckForUpdatesAsync("nokto", "Nokto").GetAwaiter().GetResult();
        if (release == null) throw new InvalidOperationException("Release esperado no fue nulo.");
        if (!release.IsUpdateAvailable) throw new InvalidOperationException("Debería haber actualización disponible.");
        if (release.PortableDownloadUrl != "https://example.com/Nokto.exe") throw new InvalidOperationException("URL de descarga incorrecta.");
    }

    private static void TestMockApiErrorGraceful()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        using var service = new UpdateService(currentVersion: new Version(1, 0, 4), httpClient: client);

        var release = service.CheckForUpdatesAsync("nokto", "Nokto").GetAwaiter().GetResult();
        if (release != null) throw new InvalidOperationException("API error 404 debería retornar null de forma segura.");
    }
}
