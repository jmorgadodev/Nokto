using Avalonia.Controls;
using Avalonia.Interactivity;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class IslandSettingsView : UserControl
{
    public IslandSettingsView() => InitializeComponent();

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
            e.Handled = true;
        }
    }
}
