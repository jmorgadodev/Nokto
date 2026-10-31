using System.Net;
using System.Text.Json;
using Nokto.Core.Services;

internal static class MusicServiceProbe
{
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception("[FAIL] " + message); Console.WriteLine("[PASS] " + message); }
    public static async Task Run()
    {
        var lines = IslandLyricsService.ParseLrc("[ar:Sintético]\n[offset:+500]\n[00:10.50][00:20.5]Uno\n[00:05.00]<00:05.0>Inicio\n[00:10.50]Dos\n[00:99.00]Inválido");
        Check(lines.Count == 3 && lines[0].Time == TimeSpan.FromSeconds(5.5) && lines[0].Text == "Inicio" && lines[1].Text == "Uno · Dos", "LRC: orden, múltiples marcas, offset y etiquetas de palabra");
        Check(IslandLyricsService.ActiveLine(lines, TimeSpan.Zero) == -1 && IslandLyricsService.ActiveLine(lines, TimeSpan.FromSeconds(11)) == 1 && IslandLyricsService.ActiveLine(lines, TimeSpan.FromHours(1)) == 2, "Línea activa antes, durante y después de las marcas");
        var handler = new FakeHandler(); using var client = new HttpClient(handler);
        using var lyrics = new IslandLyricsService(client);
        handler.Json = JsonSerializer.Serialize(new { syncedLyrics = "[00:01.00]Prueba", plainLyrics = "Prueba", instrumental = false });
        var first = await lyrics.GetAsync("Título & prueba", "Autor", TimeSpan.FromSeconds(180), default);
        var cached = await lyrics.GetAsync("Título & prueba", "Autor", TimeSpan.FromSeconds(180), default);
        Check(first.Lines.Count == 1 && ReferenceEquals(first, cached) && handler.Calls == 1 && handler.LastUri!.Query.Contains("%26"), "LRCLIB: metadatos escapados, sincronización y caché sin peticiones repetidas");
        handler.Code = HttpStatusCode.NotFound;
        Check((await lyrics.GetAsync("Sin letra", "Autor", TimeSpan.Zero, default)).Lines.Count == 0, "LRCLIB: pista sin letra no produce error");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool stopped = false; try { await lyrics.GetAsync("Cancelada", "Autor", TimeSpan.Zero, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Consulta de letra cancelable al cambiar de pista");

        handler.Code = HttpStatusCode.OK;
        handler.Json = new string(' ', 1_000_001);
        bool limited = false; try { await lyrics.GetAsync("Letra demasiado grande", "Autor", TimeSpan.Zero, default); } catch (InvalidDataException) { limited = true; }
        Check(limited, "Respuesta remota limitada antes de procesarla");
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls; public string Json = "{}"; public HttpStatusCode Code = HttpStatusCode.OK; public Uri? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Code) { Content = new StringContent(Json) });
        }
    }
}
