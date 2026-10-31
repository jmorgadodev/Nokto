using Avalonia.Interactivity;
using Nokto.Core.Services;
using Windows.Devices.Geolocation;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private IslandWeatherService? _weatherService;
    private CancellationTokenSource? _weatherRequest;
    private string _weatherCityLast = "";
    private DateTimeOffset _weatherCheckedUtc;

    private void RefreshWeatherIfNeeded(bool force = false)
    {
        if (!_islandReady || _settings?.ShowWeather != true || _settings.PrivacyMode ||
            !_settings.PageHasFeature("weather", _pageIndex)) return;
        bool location = _settings.WeatherUseLocation;
        string city = location ? "@location" : _settings.WeatherCity?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(city))
        {
            WeatherText.Text = "Configura una ciudad desde Ajustes";
            WeatherDetail.Text = "";
            return;
        }
        if (!force && string.Equals(city, _weatherCityLast, StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.UtcNow - _weatherCheckedUtc < TimeSpan.FromMinutes(30)) return;
        _weatherRequest?.Cancel();
        _weatherRequest?.Dispose();
        _weatherRequest = new CancellationTokenSource();
        _weatherService ??= new IslandWeatherService();
        _weatherCityLast = city;
        _weatherCheckedUtc = DateTimeOffset.UtcNow;
        WeatherText.Text = "Consultando clima…";
        WeatherDetail.Text = "";
        _ = LoadWeatherAsync(city, _weatherRequest.Token, force, location);
    }
    private async Task LoadWeatherAsync(string city, CancellationToken cancellation, bool force, bool location)
    {
        try
        {
            WeatherResult weather;
            if (location)
            {
                var position = await new Geolocator { DesiredAccuracyInMeters = 5000 }
                    .GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15));
                cancellation.ThrowIfCancellationRequested();
                var point = position.Coordinate.Point.Position;
                weather = await _weatherService!.GetAtLocationAsync(point.Latitude, point.Longitude, DateTimeOffset.UtcNow, cancellation);
            }
            else weather = await _weatherService!.GetAsync(city, DateTimeOffset.UtcNow, force, cancellation);
            if (cancellation.IsCancellationRequested || !_islandReady || _settings?.ShowWeather != true ||
                _settings.PrivacyMode || !_settings.PageHasFeature("weather", _pageIndex)) return;
            WeatherText.Text = weather.DisplayText;
            WeatherDetail.Text = weather.Detail;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested && _islandReady)
            {
                WeatherText.Text = "Clima no disponible";
                WeatherDetail.Text = location ? "Comprueba el permiso de ubicación y la conexión." : "Comprueba ciudad o conexión.";
            }
        }
        finally
        {
            if (!cancellation.IsCancellationRequested && _islandReady && _expanded)
            { ArrangeContentSections(); AnimateTo(ExpandedWidth, ExpandedHeightForContent); }
        }
    }
    private void OnWeatherRefresh(object? sender, RoutedEventArgs e) => RefreshWeatherIfNeeded(force: true);

    private void DisposeWeather()
    {
        _weatherRequest?.Cancel();
        _weatherRequest?.Dispose();
        _weatherRequest = null;
        _weatherService?.Dispose();
        _weatherService = null;
    }
}
