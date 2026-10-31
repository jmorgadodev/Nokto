using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Nokto.Core.Services;

/// <summary>
/// Pronóstico opt-in por ciudad o coordenadas suministradas por el usuario.
/// Caché en memoria de 30 minutos por ciudad.
/// </summary>
public sealed class IslandWeatherService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private string _cachedCity = "";
    private WeatherResult? _cachedResult;
    private DateTimeOffset _cachedAt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _placeKey = "";
    private (string City, string Country)? _place;
    private DateTimeOffset _placeAt;
    private DateTimeOffset _lastReverseRequest;
    private readonly Uri _reverseEndpoint;

    public IslandWeatherService(HttpClient? http = null, Uri? reverseEndpoint = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(9) };
        _reverseEndpoint = reverseEndpoint ?? new Uri("https://nominatim.openstreetmap.org/reverse");
    }

    public async Task<WeatherResult> GetAsync(string city, DateTimeOffset utcNow,
        bool force = false, CancellationToken cancellation = default)
    {
        city = (city ?? "").Trim();
        if (city.Length is < 2 or > 80)
            throw new ArgumentException("Escribe una ciudad de entre 2 y 80 caracteres.", nameof(city));
        await _gate.WaitAsync(cancellation);
        try
        {
            if (!force && _cachedResult is not null &&
                string.Equals(_cachedCity, city, StringComparison.OrdinalIgnoreCase) &&
                utcNow - _cachedAt < TimeSpan.FromMinutes(30))
                return _cachedResult;

            string[] parts = city.Split(',', 2, StringSplitOptions.TrimEntries);
            string geo = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                Uri.EscapeDataString(parts[0]) + "&count=10&language=es";
            using var geoResponse = await _http.GetAsync(geo, cancellation);
            geoResponse.EnsureSuccessStatusCode();
            using var geoData = JsonDocument.Parse(await geoResponse.Content.ReadAsStringAsync(cancellation));
            if (!geoData.RootElement.TryGetProperty("results", out var places) ||
                places.GetArrayLength() == 0)
                throw new InvalidOperationException("Ciudad no encontrada. Añade el país.");

            JsonElement best = places[0];
            if (parts.Length == 2 && parts[1].Length > 0)
            {
                bool matched = false;
                foreach (var place in places.EnumerateArray())
                {
                    string country = place.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                    string countryCode = place.TryGetProperty("country_code", out var code) ? code.GetString() ?? "" : "";
                    if (country.Contains(parts[1], StringComparison.OrdinalIgnoreCase) ||
                        countryCode.Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                    { best = place; matched = true; break; }
                }
                if (!matched)
                    throw new InvalidOperationException("No se encontró esa ciudad en el país indicado.");
            }
            double latitude = best.GetProperty("latitude").GetDouble();
            double longitude = best.GetProperty("longitude").GetDouble();
            string resolved = best.GetProperty("name").GetString() ?? city;
            string countryName = best.TryGetProperty("country", out var countryField) ? countryField.GetString() ?? "" : "";
            var result = await FetchCoordinatesAsync(latitude, longitude, resolved, countryName, utcNow, cancellation);
            _cachedCity = city;
            _cachedAt = utcNow;
            _cachedResult = result;
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<WeatherResult> GetAtLocationAsync(double latitude, double longitude,
        DateTimeOffset utcNow, CancellationToken cancellation = default)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) ||
            Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        await _gate.WaitAsync(cancellation);
        try
        {
            // Consulta aproximada: solo la localidad, nunca una dirección de calle.
            double lat = Math.Round(latitude, 2), lon = Math.Round(longitude, 2);
            string key = FormattableString.Invariant($"{lat:0.00},{lon:0.00}");
            (string City, string Country)? place = null;
            if (_placeKey == key && utcNow - _placeAt < TimeSpan.FromMinutes(30)) place = _place;
            else
            {
                try
                {
                    var delay = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - _lastReverseRequest);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellation);
                    _lastReverseRequest = DateTimeOffset.UtcNow;
                    string url = _reverseEndpoint.AbsoluteUri + "?format=jsonv2&zoom=10&addressdetails=1&accept-language=es&lat=" +
                        lat.ToString(CultureInfo.InvariantCulture) + "&lon=" + lon.ToString(CultureInfo.InvariantCulture);
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("NoktoDev/2.0 (+https://github.com/jmorgadodev/Nokto)");
                    using var response = await _http.SendAsync(request, cancellation);
                    response.EnsureSuccessStatusCode();
                    using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                    if (data.RootElement.TryGetProperty("address", out var address))
                    {
                        string Read(string name) => address.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
                        string name = new[] { "city", "town", "village", "municipality", "county", "state" }
                            .Select(Read).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                        if (name.Length > 0) place = (name, Read("country"));
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { }
                _placeKey = key; _placeAt = utcNow; _place = place;
            }
            string fallback = FormattableString.Invariant($"{Math.Abs(lat):0.00}° {(lat < 0 ? "S" : "N")} · {Math.Abs(lon):0.00}° {(lon < 0 ? "O" : "E")}");
            var weather = await FetchCoordinatesAsync(latitude, longitude, place?.City ?? fallback, place?.Country ?? "", utcNow, cancellation);
            return weather with { PlaceAttribution = place.HasValue ? "© OpenStreetMap" : "Coordenadas aproximadas" };
        }
        finally { _gate.Release(); }
    }

    private async Task<WeatherResult> FetchCoordinatesAsync(double latitude, double longitude,
        string resolved, string countryName, DateTimeOffset utcNow, CancellationToken cancellation)
    {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" +
                latitude.ToString(CultureInfo.InvariantCulture) + "&longitude=" +
                longitude.ToString(CultureInfo.InvariantCulture) +
                "&current=temperature_2m,relative_humidity_2m,weather_code&timezone=auto";
            using var response = await _http.GetAsync(url, cancellation);
            response.EnsureSuccessStatusCode();
            using var weatherData = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var current = weatherData.RootElement.GetProperty("current");
            int codeValue = current.GetProperty("weather_code").GetInt32();
            double temperature = current.GetProperty("temperature_2m").GetDouble();
            int humidity = current.GetProperty("relative_humidity_2m").GetInt32();
            return new WeatherResult(resolved, countryName, temperature, humidity,
                codeValue, Describe(codeValue), utcNow);
    }

    public static string Describe(int code) => code switch
    {
        0 => "Despejado",
        1 or 2 => "Poco nublado",
        3 => "Nublado",
        45 or 48 => "Niebla",
        >= 51 and <= 67 => "Lluvia",
        >= 71 and <= 77 => "Nieve",
        >= 80 and <= 82 => "Chubascos",
        >= 95 => "Tormenta",
        _ => "Variable"
    };

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsClient) _http.Dispose();
    }
}

public sealed record WeatherResult(string City, string Country, double TemperatureC,
    int HumidityPercent, int WeatherCode, string Condition, DateTimeOffset UpdatedAtUtc)
{
    public string PlaceAttribution { get; init; } = "";
    public string DisplayText => $"{City} · {TemperatureC:0}°C · {Condition}";
    public string Detail => (string.IsNullOrEmpty(Country) ? $"Humedad {HumidityPercent}%" : $"{Country} · Humedad {HumidityPercent}%") +
        (string.IsNullOrEmpty(PlaceAttribution) ? "" : $" · {PlaceAttribution}");
}
