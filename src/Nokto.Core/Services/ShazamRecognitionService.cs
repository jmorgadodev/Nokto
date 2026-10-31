using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nokto.Core.Services;

/// <summary>Cliente no oficial: consulta una huella acústica, sin clave ni envío del WAV.</summary>
public sealed class ShazamRecognitionService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private static readonly string SessionId = Guid.NewGuid().ToString(); // Efímero: no se escribe un ID de instalación.
    public ShazamRecognitionService(HttpClient? http = null)
    { _ownsClient = http is null; _http = http ?? new HttpClient(); }

    public async Task<RecognizedSong?> RecognizeAsync(AcousticSignature signature, CancellationToken cancellation = default)
    {
        if (signature.PeakCount == 0) throw new InvalidOperationException("No se detectó suficiente señal musical. Acerca la fuente o aumenta su volumen.");
        if (signature.Data.Length is < 88 or > 100_000 || signature.Milliseconds is < 2000 or > 16000)
            throw new InvalidDataException("La huella acústica no es válida.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://amp.shazam.com/match/v1/en/US/android/{SessionId}/{Guid.NewGuid()}");
        request.Headers.UserAgent.ParseAdd("NoktoDev/2.0 (+https://github.com/jmorgadodev/Nokto)");
        request.Content = JsonContent.Create(new
        {
            signatures = new[] { new { uri = "data:audio/vnd.shazam.sig;base64," + Convert.ToBase64String(signature.Data), samplems = signature.Milliseconds } },
            timezone = "GMT"
        });
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("El reconocimiento está temporalmente ocupado. Inténtalo más tarde.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new InvalidOperationException("El reconocimiento no está disponible en este momento. Prueba más tarde.");
        response.EnsureSuccessStatusCode();
        using var doc = await RemoteJson.ReadAsync(response, timeout.Token);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("El servicio devolvió una respuesta no reconocida.");
        if (!root.TryGetProperty("results", out var result) || result.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("El servicio de reconocimiento devolvió una respuesta no reconocida. Prueba más tarde.");
        if (!result.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array || matches.GetArrayLength() == 0) return null;
        string id = Read(matches[0], "id");
        if (string.IsNullOrEmpty(id)) return null;
        if (!root.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Object ||
            !resources.TryGetProperty("shazam-songs", out var songs) || songs.ValueKind != JsonValueKind.Object ||
            !songs.TryGetProperty(id, out var song) || song.ValueKind != JsonValueKind.Object ||
            !song.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Se encontró una coincidencia, pero no se recibieron sus datos. Vuelve a intentarlo.");
        string title = Read(attributes, "title"), artist = Read(attributes, "artist");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("El servicio no devolvió el nombre de la canción.");
        string url = Read(attributes, "webUrl");
        return new RecognizedSong(title, artist, Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" ? url : null);
    }
    private static string Read(JsonElement obj, string name)
    {
        string text = obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        return text.Length > 2048 ? text[..2048] : text;
    }
    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
