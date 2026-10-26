using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

/// <summary>Referencias a archivos elegidos explícitamente. Sin copiar datos ni vigilar el disco.</summary>
public partial class IslandWindow
{
    private bool _shelfOpen;

    private void RefreshShelf()
    {
        if (_settings is null) return;
        FileShelfPanel.IsVisible = _settings.ShowFileShelf && !_settings.PrivacyMode;
        ShelfDetails.IsVisible = _settings.ShowFileShelf && !_settings.PrivacyMode && _shelfOpen;
        if (_animationTimer is not null && _islandReady && _expanded)
        {
            double height = ExpandedHeightForContent;
            if (Math.Abs(height - _toHeight) > .5)
                AnimateTo(ExpandedWidth, height);
        }
    }

    private void OnToggleShelf(object? sender, RoutedEventArgs e)
    {
        if (_settings?.ShowFileShelf != true) return;
        _shelfOpen = !_shelfOpen;
        _pinned = true;
        RefreshShelf();
    }

    private async void OnShelfPick(object? sender, RoutedEventArgs e)
    {
        if (_settings?.ShowFileShelf != true) return;
        try
        {
            var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Elegir archivo para Nokto Island",
                AllowMultiple = false
            });
            var path = chosen.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (_settings.AddShelfFile(path))
            {
                ShelfList.SelectedIndex = _settings.ShelfFiles.Count - 1;
                ShelfStatusText.Text = "Archivo añadido (solo referencia local).";
            }
            else ShelfStatusText.Text = "No se pudo añadir: duplicado, ausente o máximo de seis archivos.";
        }
        catch (Exception)
        {
            ShelfStatusText.Text = "No se pudo abrir el selector de archivos.";
        }
    }

    private void OnShelfOpen(object? sender, RoutedEventArgs e)
    {
        if (ShelfList.SelectedItem is not IslandViewModel.ShelfFileOption file) return;
        if (!File.Exists(file.Path))
        {
            ShelfStatusText.Text = "El archivo ya no existe en su ubicación.";
            return;
        }
        try
        {
            // Abre el archivo con la aplicación predeterminada solo tras acción explícita.
            Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
            ShelfStatusText.Text = "Archivo abierto con la aplicación predeterminada.";
        }
        catch (Exception)
        {
            ShelfStatusText.Text = "Windows no pudo abrir este archivo.";
        }
    }

    private void OnShelfRemove(object? sender, RoutedEventArgs e)
    {
        if (ShelfList.SelectedItem is not IslandViewModel.ShelfFileOption file) return;
        if (_settings?.RemoveShelfFile(file.Path) == true)
            ShelfStatusText.Text = "Referencia quitada; el archivo original no se modificó.";
    }
}
