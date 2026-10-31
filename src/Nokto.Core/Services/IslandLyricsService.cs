using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nokto.Core.Services;

public sealed record LyricLine(TimeSpan Time, string Text);
public sealed record SongLyrics(IReadOnlyList<LyricLine> Lines, string? PlainText, bool Instrumental);

public sealed partial class IslandLyricsService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Dictionary<string, SongLyrics> _cache = new();
    private readonly SemaphoreSlim _lookups = new(1, 1);
    public IslandLyricsService(HttpClient? http = null)
    { _ownsClient = http is null; _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; }

    public async Task<SongLyrics> GetAsync(string title, string artist, TimeSpan duration, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist) || title.Length > 500 || artist.Length > 500)
            return new SongLyrics([], null, false);
        string key = $"{title}|{artist}|{Math.Round(duration.TotalSeconds)}";
        await _lookups.WaitAsync(cancellation);
        try
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            string query = "track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist);
            string direct = "/api/get?" + query;
            double seconds = Math.Round(duration.TotalSeconds);
            if (seconds is >= 1 and <= 3600) direct += "&duration=" + seconds.ToString(CultureInfo.InvariantCulture);
            try
            {
                // Reservar tiempo para la búsqueda si el endpoint directo se atasca.
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(5));
                using var doc = await FetchLyricsDocumentAsync(direct, attempt.Token);
                if (doc is not null)
                {
                    var result = ReadLyrics(doc.RootElement);
                    if (HasLyrics(result)) return RememberLyrics(key, result);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500) { }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }

            // Una sola alternativa documentada, secuencial y cancelable; nunca saltar un 429.
            await Task.Delay(250, timeout.Token);
            using var search = await FetchLyricsDocumentAsync("/api/search?" + query, timeout.Token);
            var found = search is null ? new SongLyrics([], null, false) : SelectLyrics(search.RootElement, title, artist, duration);
            return RememberLyrics(key, found);
        }
        finally { _lookups.Release(); }
    }

    private static bool HasLyrics(SongLyrics result) => result.Instrumental || result.Lines.Count > 0 || !string.IsNullOrWhiteSpace(result.PlainText);
    private SongLyrics RememberLyrics(string key, SongLyrics result)
    {
        // No convertir fallos o ausencia transitoria en una pista vacía permanente.
        if (!HasLyrics(result)) return result;
        if (_cache.Count >= 32) _cache.Clear();
        _cache[key] = result;
        return result;
    }

    public static IReadOnlyList<LyricLine> ParseLrc(string text)
    {
        var lines = new List<LyricLine>();
        var offsetTag = OffsetPattern().Match(text);
        int offset = offsetTag.Success && int.TryParse(offsetTag.Groups[1].Value, out var ms) ? Math.Clamp(ms, -60000, 60000) : 0;
        foreach (string row in text.Split('\n').Take(2000))
        {
            var tags = TimePattern().Matches(row);
            if (tags.Count == 0) continue;
            string content = WordTimePattern().Replace(TimePattern().Replace(row, ""), "").Trim();
            foreach (Match tag in tags)
            {
                double minutes = double.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                double seconds = double.Parse(tag.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds >= 60 || minutes > 240) continue;
                lines.Add(new(TimeSpan.FromMilliseconds(Math.Max(0, (minutes * 60 + seconds) * 1000 + offset)), content));
            }
        }
        return lines.OrderBy(l => l.Time).GroupBy(l => l.Time)
            .Select(g => new LyricLine(g.Key, string.Join(" · ", g.Select(l => l.Text).Distinct()))).ToArray();
    }

    public static int ActiveLine(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (lines[mid].Time <= position) { found = mid; low = mid + 1; } else high = mid - 1;
        }
        return found;
    }

    [GeneratedRegex(@"\[(\d{1,3}):([0-5]?\d(?:\.\d{1,3})?)\]")]
    private static partial Regex TimePattern();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:\.\d{1,3})?>")]
    private static partial Regex WordTimePattern();
    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
