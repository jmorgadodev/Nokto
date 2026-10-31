using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Nokto.UI.ViewModels;
using Nokto.Core.Models;
using Windows.Devices.Geolocation;

namespace Nokto.UI.Views;

public partial class IslandSettingsView : UserControl
{
    public IslandSettingsView()
    {
        InitializeComponent();
        AddHandler(Avalonia.Input.DragDrop.DragOverEvent, OnOrderDragOver);
        AddHandler(Avalonia.Input.DragDrop.DragLeaveEvent, OnOrderDragLeave);
        AddHandler(Avalonia.Input.DragDrop.DropEvent, OnOrderDrop);
    }

    private async void OnWeatherLocationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || DataContext is not MainViewModel main) return;
        if (box.IsChecked != true)
        {
            main.IslandVm.WeatherUseLocation = false;
            return;
        }
        box.IsEnabled = false;
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            main.IslandVm.WeatherUseLocation = access == GeolocationAccessStatus.Allowed;
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, main.IslandVm.WeatherUseLocation);
            WeatherLocationStatus.Text = main.IslandVm.WeatherUseLocation
                ? "Ubicación activada. Open-Meteo consulta el clima y OpenStreetMap identifica tu ciudad con coordenadas aproximadas."
                : "Permiso de ubicación no disponible. Revísalo en Configuración de Windows.";
        }
        catch (Exception)
        {
            main.IslandVm.WeatherUseLocation = false;
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            WeatherLocationStatus.Text = "No se pudo solicitar la ubicación. Revisa Configuración de Windows.";
        }
        finally { box.IsEnabled = true; }
    }

    private void OnWeatherCityCommitted(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox input && DataContext is MainViewModel main)
            main.IslandVm.WeatherCity = input.Text?.Trim() ?? "";
    }

    private void OnOpenRoutines(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel main)
            main.SelectedTabIndex = 2;
    }


    private void OnSelectEyeStyle(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string styleId } &&
            DataContext is MainViewModel main)
        {
            main.IslandVm.SelectStyle(styleId);
            if (!main.IslandVm.ReducedMotion && styleId != "None")
                EyePreview.PlayActivation();
            e.Handled = true;
        }
    }

    private void OnPreviewEyeAnimation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel main && !main.IslandVm.ReducedMotion)
            EyePreview.PlayActivation();
    }
}
