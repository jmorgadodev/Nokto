using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nokto.Core.Services;

internal static class LyricsLookupProbe
{
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception("[FAIL] " + message); Console.WriteLine("[PASS] " + message); }

    private static object Track(string title, string artist, double duration, string? synced = "[00:01.00]Línea de prueba", string? plain = "Texto sintético")
        => new { trackName = title, artistName = artist, duration, instrumental = false, syncedLyrics = synced, plainLyrics = plain };

    public static async Task Run()
    {
        const string title = "Los Malaventurados No Lloran", artist = "PXNDX";
        var handler = new SequenceHandler(
            Reply(HttpStatusCode.ServiceUnavailable, new { }, 0),
            Reply(HttpStatusCode.OK, new[] {
                Track(title, artist, 239, "[00:02.00]Versión larga"),
                Track(title, "Otro artista", 207),
                Track(title, artist, 207, null),
                Track(title, artist, 208, "[00:03.00]Versión cercana"),
                Track(title, artist, 207, "[00:04.00]Versión correcta") }));
        using var http = new HttpClient(handler);
        using var lyrics = new IslandLyricsService(http);
        var result = await lyrics.GetAsync(title, artist, TimeSpan.FromSeconds(207), default);
        Check(result.Lines.Single().Time == TimeSpan.FromSeconds(4) && handler.Requests.Count == 2 &&
            handler.Requests[1].AbsolutePath == "/api/search", "503 de la consulta directa usa búsqueda y elige letra sincronizada del artista y duración correctos");
        Check(ReferenceEquals(result, await lyrics.GetAsync(title, artist, TimeSpan.FromSeconds(207), default)) && handler.Requests.Count == 2,
            "La coincidencia alternativa se guarda en caché sin repetir consultas");

        using var missingHttp = new HttpClient(new SequenceHandler(Reply(HttpStatusCode.NotFound, new { }),
            Reply(HttpStatusCode.OK, new[] { Track("Canción de prueba", "Intérprete", 207.76) })));
        using var missing = new IslandLyricsService(missingHttp);
        Check((await missing.GetAsync("Cancion de prueba", "Interprete", TimeSpan.FromSeconds(207), default)).Lines.Count == 1,
            "404 busca metadatos equivalentes y acepta la diferencia documentada de dos segundos");

        using var wrongHttp = new HttpClient(new SequenceHandler(Reply(HttpStatusCode.NotFound, new { }), Reply(HttpStatusCode.OK,
            new[] { Track(title + " (En Vivo)", artist, 207), Track(title, "Otro artista", 207), Track(title, artist, 239) })));
        using var wrong = new IslandLyricsService(wrongHttp);
        var absent = await wrong.GetAsync(title, artist, TimeSpan.FromSeconds(207), default);
        Check(absent.Lines.Count == 0 && absent.PlainText is null, "No muestra letras de otro intérprete, versión en vivo o duración incompatible");

        var limitedHandler = new SequenceHandler(Reply(HttpStatusCode.TooManyRequests, new { }, 60));
        using var limitedHttp = new HttpClient(limitedHandler);
        using var limited = new IslandLyricsService(limitedHttp);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool rateLimited = false;
            try { await limited.GetAsync(title, artist, TimeSpan.FromSeconds(207), default); }
            catch (HttpRequestException ex) { rateLimited = ex.StatusCode == HttpStatusCode.TooManyRequests; }
            Check(rateLimited, "El límite de LRCLIB se distingue de un error de conexión");
        }
        Check(limitedHandler.Requests.Count == 1, "Retry-After impide búsqueda alternativa y nuevas peticiones mientras dura el límite");

        var unavailableHandler = new SequenceHandler(Reply(HttpStatusCode.ServiceUnavailable, new { }, 0), Reply(HttpStatusCode.ServiceUnavailable, new { }, 0),
            Reply(HttpStatusCode.OK, Track(title, artist, 207)));
        using var unavailableHttp = new HttpClient(unavailableHandler);
        using var unavailable = new IslandLyricsService(unavailableHttp);
        bool busy = false;
        try { await unavailable.GetAsync(title, artist, TimeSpan.FromSeconds(207), default); }
        catch (HttpRequestException ex) { busy = ex.StatusCode == HttpStatusCode.ServiceUnavailable; }
        Check(busy, "Fallo de ambas consultas conserva el estado de servicio no disponible");
        Check((await unavailable.GetAsync(title, artist, TimeSpan.FromSeconds(207), default)).Lines.Count == 1,
            "Un error temporal no se guarda como ausencia de letra");

        var malformedHandler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        using var malformedHttp = new HttpClient(malformedHandler);
        using var malformed = new IslandLyricsService(malformedHttp);
        bool format = false;
        try { await malformed.GetAsync(title, artist, TimeSpan.FromSeconds(207), default); }
        catch (InvalidDataException) { format = true; }
        Check(format, "Respuesta con formato inválido se distingue de una pista sin letra");

        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        int before = handler.Requests.Count; bool stopped = false;
        try { await lyrics.GetAsync("Otra canción", artist, TimeSpan.Zero, canceled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && handler.Requests.Count == before, "Cancelar una consulta no envía búsquedas adicionales");
    }

    public static async Task SmokeAsync()
    {
        using var http = new HttpClient(new AuditHandler()) { Timeout = TimeSpan.FromSeconds(15) };
        using var lyrics = new IslandLyricsService(http);
        var result = await lyrics.GetAsync("Los Malaventurados No Lloran", "PXNDX", TimeSpan.FromSeconds(207), default);
        Check(result.Lines.Count > 0, $"Caso real de la captura: {result.Lines.Count} líneas sincronizadas recibidas (sin imprimir la letra)");
    }

    private sealed class AuditHandler() : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            Console.WriteLine($"LRCLIB {request.RequestUri!.AbsolutePath}: HTTP {(int)response.StatusCode}");
            return response;
        }
    }

    private static HttpResponseMessage Reply(HttpStatusCode code, object body, int? retryAfter = null)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        if (retryAfter.HasValue) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfter.Value));
        return response;
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!);
            Check(request.Headers.UserAgent.ToString().Contains("NoktoDev"), "Consulta identifica a Nokto ante LRCLIB");
            if (Requests.Count > responses.Length) throw new Exception("Más peticiones de las esperadas");
            return Task.FromResult(responses[Requests.Count - 1]);
        }
    }
}
