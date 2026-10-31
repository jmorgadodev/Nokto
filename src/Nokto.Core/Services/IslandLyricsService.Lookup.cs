using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nokto.Core.Services;

public sealed partial class IslandLyricsService
{
    private DateTimeOffset _lyricsRetryAfter;
    private HttpStatusCode _lyricsCooldownStatus;

    private async Task<JsonDocument?> FetchLyricsDocumentAsync(string path, CancellationToken cancellation)
    {
        var wait = _lyricsRetryAfter - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            // No reenviar durante un límite. Para sobrecarga breve, respetar Retry-After antes de buscar.
            if (_lyricsCooldownStatus == HttpStatusCode.TooManyRequests || wait > TimeSpan.FromSeconds(2))
                throw new HttpRequestException("LRCLIB solicita esperar antes de otra consulta.", null, _lyricsCooldownStatus);
            await Task.Delay(wait, cancellation);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://lrclib.net" + path);
        request.Headers.UserAgent.ParseAdd("NoktoDev/2.0 (+https://github.com/jmorgadodev/Nokto)");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ??
                TimeSpan.FromSeconds(response.StatusCode == HttpStatusCode.TooManyRequests ? 30 : 1);
            delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            _lyricsRetryAfter = DateTimeOffset.UtcNow + delay + TimeSpan.FromMilliseconds(Math.Min(300, delay.TotalMilliseconds * .25) * Random.Shared.NextDouble());
            _lyricsCooldownStatus = response.StatusCode;
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await RemoteJson.ReadAsync(response, cancellation);
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static SongLyrics ReadLyrics(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Formato de letra no válido.");
        return new(ParseLrc(Text(element, "syncedLyrics") ?? ""), Text(element, "plainLyrics"),
            element.TryGetProperty("instrumental", out var flag) && flag.ValueKind == JsonValueKind.True);
    }

    private static SongLyrics SelectLyrics(JsonElement results, string title, string artist, TimeSpan duration)
    {
        if (results.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Formato de búsqueda de letras no válido.");
        string expectedTitle = NormalizeMetadata(title), expectedArtist = NormalizeMetadata(artist);
        var candidates = new List<(SongLyrics Lyrics, double Difference)>();
        foreach (var candidate in results.EnumerateArray().Take(20))
        {
            if (candidate.ValueKind != JsonValueKind.Object || NormalizeMetadata(Text(candidate, "trackName") ?? "") != expectedTitle ||
                NormalizeMetadata(Text(candidate, "artistName") ?? "") != expectedArtist) continue;
            double difference = 0;
            if (duration > TimeSpan.Zero)
            {
                if (!candidate.TryGetProperty("duration", out var length) || length.ValueKind != JsonValueKind.Number ||
                    !length.TryGetDouble(out double seconds) || !double.IsFinite(seconds)) continue;
                difference = Math.Abs(seconds - duration.TotalSeconds);
                // LRCLIB documenta ±2 s: no usar otra grabación para simular sincronización.
                if (difference > 2) continue;
            }
            var lyrics = ReadLyrics(candidate);
            if (HasLyrics(lyrics)) candidates.Add((lyrics, difference));
        }
        return candidates.OrderByDescending(c => c.Lyrics.Lines.Count > 0).ThenBy(c => c.Difference)
            .Select(c => c.Lyrics).FirstOrDefault() ?? new SongLyrics([], null, false);
    }

    private static string NormalizeMetadata(string value)
    {
        var normalized = new StringBuilder();
        foreach (char character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
                normalized.Append(char.ToLowerInvariant(character));
        return normalized.ToString();
    }
}
