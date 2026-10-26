using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private int _pageIndex;
    private long _lastWheelTimestamp;
    private string? _clipboardText;

    private void OnPagePrevious(object? sender, RoutedEventArgs e) => SwitchPage(-1);
    private void OnPageNext(object? sender, RoutedEventArgs e) => SwitchPage(1);

    private bool WithinControl(object? origin, Control target)
    {
        if (origin is not Visual source) return false;
        return source == target || source.GetVisualAncestors().Contains(target);
    }

    private void OnWheelGuard(object? sender, PointerWheelEventArgs e)
    {
        // Avalonia Slider maneja la rueda antes de burbujear al panel. Interceptar
        // en tunneling evita cambiar accidentalmente el volumen.
        if (_pageIndex == 1 && WithinControl(e.Source, AudioControls) &&
            !WithinControl(e.Source, IslandPageNav)) e.Handled = true;
    }

    private void OnIslandWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_settings?.EnableWheelPages != true || e.Delta.Y == 0) return;
        if (_pageIndex == 1 && !WithinControl(e.Source, IslandPageNav))
        {
            // La página Sonido no cambia con la rueda sobre faders o botones.
            e.Handled = true;
            return;
        }
        if (e.Source is Visual visual && (visual is Slider or ListBox ||
            visual.GetVisualAncestors().Any(v => v is Slider or ListBox)))
        {
            e.Handled = true;
            return;
        }
        long now = Stopwatch.GetTimestamp();
        if (_lastWheelTimestamp != 0 && Stopwatch.GetElapsedTime(_lastWheelTimestamp, now).TotalMilliseconds < 350)
        {
            e.Handled = true;
            return;
        }
        _lastWheelTimestamp = now;
        _pinned = true;
        if (!_expanded) SetExpanded(true);
        else SwitchPage(e.Delta.Y < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void SwitchPage(int delta)
    {
        if (_settings is null) return;
        var pages = _settings.AvailablePages;
        int pos = pages.ToList().IndexOf(_pageIndex);
        int next = Math.Clamp((pos < 0 ? 0 : pos) + delta, 0, pages.Count - 1);
        _pinned = true;
        _pageIndex = pages[next];
        if (!_expanded) SetExpanded(true);
        RefreshPages();
    }

    private string PageLabel(string key, string fallback)
    {
        try { return this.FindResource(key)?.ToString() ?? fallback; }
        catch { return fallback; }
    }

    private void RefreshPages()
    {
        if (_settings is null) return;
        var pages = _settings.AvailablePages;
        int position = pages.ToList().IndexOf(_pageIndex);
        if (position < 0)
        {
            _pageIndex = pages[0];
            position = 0;
        }
        OverviewPage.IsVisible = _pageIndex == 0;
        AudioPage.IsVisible = _pageIndex == 1;
        QuickPage.IsVisible = _pageIndex == 2;
        PageTitle.Text = (_pageIndex switch
        {
            0 => PageLabel("IslandPageOverview", "Resumen"),
            1 => PageLabel("IslandPageAudio", "Sonido"),
            _ => PageLabel("IslandPageShortcuts", "Accesos")
        }) + $"   {position + 1}/{pages.Count}";
        PreviousPageButton.IsEnabled = position > 0;
        NextPageButton.IsEnabled = position < pages.Count - 1;
        OpenDashboardButton.IsVisible = _settings.ShowMainButton;
        HideIslandButton.IsVisible = _settings.ShowHideButton;
        // La isla es un centro de uso, no una demo: las pruebas de ojos van en Ajustes.
        TestEyesButton.IsVisible = false;
        IslandFooter.IsVisible = _settings.ShowMainButton || _settings.ShowHideButton;

        AudioControls.IsVisible = true;
        SoundMuteButtons.IsVisible = _settings.SoundShowMicrophone || _settings.SoundShowOutputMute;
        MicMuteButton.IsVisible = _settings.SoundShowMicrophone;
        OutputMuteButton.IsVisible = _settings.SoundShowOutputMute;
        VolumeRow.IsVisible = _settings.SoundShowVolume;
        VolumeValueText.IsVisible = _settings.SoundShowVolume;
        ProductivityPanel.IsVisible = _settings.ShortcutsShowRoutines;
        QuickActionsPanel.IsVisible = _settings.ShowQuickActions &&
            (_settings.ShortcutsShowExplorer || _settings.ShortcutsShowFolders);
        ExplorerButton.IsVisible = _settings.ShortcutsShowExplorer;
        AddFolderButton.IsVisible = _settings.ShortcutsShowFolders;
        FolderCountLabel.IsVisible = _settings.ShortcutsShowFolders && !_settings.PrivacyMode;
        QuickFolderList.IsVisible = _settings.ShortcutsShowFolders && !_settings.PrivacyMode;
        FolderActionRow.IsVisible = _settings.ShortcutsShowFolders && !_settings.PrivacyMode;
        FileShelfPanel.IsVisible = _settings.ShowFileShelf && !_settings.PrivacyMode;
        ClipboardPanel.IsVisible = _settings.ShowClipboardActions && !_settings.PrivacyMode;
        SystemStatus.IsVisible = !_settings.PrivacyMode &&
            (_settings.ShowBatteryStatus || _settings.ShowNetworkStatus);
        WeatherPanel.IsVisible = _settings.ShowWeather && !_settings.PrivacyMode;
        if (!WeatherPanel.IsVisible) _weatherRequest?.Cancel();
        if (_pageIndex != 2 || !ClipboardPanel.IsVisible) ClearClipboardPreview();

        if (_islandReady && _expanded)
            AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        if (_pageIndex == 0) RefreshWeatherIfNeeded();
        SyncMediaProgressTimer();
    }

    private void OnOpenRoutinesIsland(object? sender, RoutedEventArgs e)
    {
        if (_controlVm is not null) _controlVm.SelectedTabIndex = 2;
        App.CurrentInstance?.ShowMainWindow();
    }

    private void OnOpenExplorer(object? sender, RoutedEventArgs e)
    {
        if (_settings?.ShowQuickActions != true) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); }
        catch (Exception) { QuickFolderStatus.Text = "No se pudo iniciar el Explorador."; }
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (_settings?.ShowQuickActions != true) return;
        try
        {
            var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleccionar carpeta para Nokto Island", AllowMultiple = false
            });
            string? path = selected.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (_settings.AddQuickFolder(path))
            {
                QuickFolderList.SelectedIndex = _settings.QuickFolders.Count - 1;
                QuickFolderStatus.Text = "Acceso añadido. La carpeta no se ha modificado.";
            }
            else QuickFolderStatus.Text = "Carpeta duplicada, inaccesible o límite de ocho accesos.";
        }
        catch (Exception) { QuickFolderStatus.Text = "No se pudo abrir el selector de carpetas."; }
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (QuickFolderList.SelectedItem is not IslandViewModel.QuickFolderOption folder) return;
        if (!Directory.Exists(folder.Path))
        { QuickFolderStatus.Text = "La carpeta ya no existe."; return; }
        try
        {
            var process = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            process.ArgumentList.Add(folder.Path);
            Process.Start(process);
            QuickFolderStatus.Text = "Carpeta abierta en el Explorador.";
        }
        catch (Exception) { QuickFolderStatus.Text = "Windows no pudo abrir la carpeta."; }
    }

    private void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (QuickFolderList.SelectedItem is not IslandViewModel.QuickFolderOption folder) return;
        if (_settings?.RemoveQuickFolder(folder.Path) == true)
            QuickFolderStatus.Text = "Acceso quitado; la carpeta original no se ha tocado.";
    }

    private async void OnReadClipboard(object? sender, RoutedEventArgs e)
    {
        if (_settings?.ShowClipboardActions != true || _settings.PrivacyMode) return;
        try
        {
            string? content = await Clipboard!.GetTextAsync();
            if (string.IsNullOrEmpty(content))
            { ClipboardPreview.Text = "No hay texto disponible."; return; }
            _clipboardText = content.Length > 110 ? content[..110] + "…" : content;
            ClipboardPreview.Text = _clipboardText.Replace('\r', ' ').Replace('\n', ' ');
        }
        catch (Exception) { ClipboardPreview.Text = "No se pudo leer texto del portapapeles."; }
    }
    private void ClearClipboardPreview()
    { _clipboardText = null; ClipboardPreview.Text = ""; }
}
