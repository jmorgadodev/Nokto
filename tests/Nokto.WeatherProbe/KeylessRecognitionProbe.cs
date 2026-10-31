using System.Net;
using System.Text.Json;
using Nokto.Core.Services;

internal static class KeylessRecognitionProbe
{
    private static void Check(bool value, string text)
    { if (!value) throw new Exception("[FAIL] " + text); Console.WriteLine("[PASS] " + text); }
    public static float[] SyntheticAudio(int sampleRate = 16000, int seconds = 8)
    {
        var samples = new float[sampleRate * seconds]; var random = new Random(741);
        double phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double frequency = 330 + 1400 * (.5 + .5 * Math.Sin(i / (sampleRate * .31)));
            phase += 2 * Math.PI * frequency / sampleRate;
            double envelope = .5 + .4 * Math.Sin(i / (sampleRate * .047));
            samples[i] = (float)(envelope * (.35 * Math.Sin(phase) + .16 * Math.Sin(phase * 1.73)) + .03 * (random.NextDouble() - .5));
        }
        return samples;
    }
    public static async Task Run()
    {
        var time = System.Diagnostics.Stopwatch.StartNew();
        var signature = AcousticSignatureGenerator.Create(SyntheticAudio());
        Check(signature.Milliseconds == 8000 && signature.PeakCount > 10 && signature.Data.Length < 10_000,
            $"Huella de audio sintético: {signature.PeakCount} picos, {signature.Data.Length} bytes, {time.ElapsedMilliseconds} ms");
        Check(BitConverter.ToUInt32(signature.Data, 0) == 0xCAFE2580 && BitConverter.ToInt32(signature.Data, 8) == signature.Data.Length - 48 &&
            BitConverter.ToInt32(signature.Data, 40) == 128000, "Firma binaria: cabecera, duración y longitud de protocolo");
        var again = AcousticSignatureGenerator.Create(SyntheticAudio());
        Check(signature.Data.SequenceEqual(again.Data), "La huella es determinista y se calcula sin red");
        var silence = AcousticSignatureGenerator.Create(new float[128000]);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); bool stopped = false;
        try { AcousticSignatureGenerator.Create(SyntheticAudio(), canceled.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(silence.PeakCount == 0 && stopped, "Silencio y cancelación de análisis se resuelven localmente");
        var handler = new Handler(); using var client = new HttpClient(handler); using var service = new ShazamRecognitionService(client);
        bool rejected = false; try { await service.RecognizeAsync(silence); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && handler.Calls == 0, "El silencio no inicia ninguna consulta");
        var song = await service.RecognizeAsync(signature);
        Check(song?.Title == "Canción sintética" && song.Artist == "Prueba" && handler.NoCredentials && handler.OnlySignature,
            "Consulta sin clave: envía huella, no WAV, y lee metadatos simulados");
        handler.Response = "{\"results\":{\"matches\":[]}}";
        Check(await service.RecognizeAsync(signature) is null, "Sin coincidencia no se convierte en un error");
        handler.Status = HttpStatusCode.TooManyRequests; int before = handler.Calls;
        rejected = false; try { await service.RecognizeAsync(signature); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && handler.Calls == before + 1, "Límite del servicio sin reintentos automáticos");
        handler.Status = HttpStatusCode.Forbidden; rejected = false;
        try { await service.RecognizeAsync(signature); } catch (InvalidOperationException ex) { rejected = ex.Message.Contains("no está disponible"); }
        Check(rejected, "Conexión no oficial no disponible muestra estado explícito");
        handler.Status = HttpStatusCode.OK; handler.Response = "{}"; rejected = false;
        try { await service.RecognizeAsync(signature); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Cambio de formato del servicio no se presenta como canción sin coincidencia");
        Check(typeof(AcousticSignatureGenerator).Assembly.GetManifestResourceNames().Any(name => name.EndsWith("shazam-for-real.txt")),
            "El ensamblado incluye la licencia y atribución de la adaptación");
    }
    public static async Task SmokeAsync()
    {
        // Acción separada, exclusivamente voluntaria de desarrollo: señal sintetizada,
        // nunca micrófono, loopback ni archivo personal. Se envía únicamente su huella.
        var signature = AcousticSignatureGenerator.Create(SyntheticAudio());
        using var service = new ShazamRecognitionService();
        var result = await service.RecognizeAsync(signature);
        Console.WriteLine("[PASS] Endpoint sin credenciales aceptó la huella sintética. Resultado: " + (result is null ? "sin coincidencia" : "coincidencia devuelta"));
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public bool NoCredentials, OnlySignature;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = "{\"results\":{\"matches\":[{\"id\":\"123\"}]},\"resources\":{\"shazam-songs\":{\"123\":{\"attributes\":{\"title\":\"Canción sintética\",\"artist\":\"Prueba\",\"webUrl\":\"https://www.shazam.com/song/123\"}}}}}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Calls++;
            NoCredentials = request.Headers.Authorization is null && !request.RequestUri!.Query.Contains("key");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            OnlySignature = json.RootElement.GetProperty("signatures")[0].GetProperty("uri").GetString()!.StartsWith("data:audio/vnd.shazam.sig;base64,") &&
                !json.RootElement.TryGetProperty("file", out _);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }
}
