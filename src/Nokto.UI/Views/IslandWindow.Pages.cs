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

    private void OnWheelGuard(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || e.Source is not Visual source ||
            !_sections.TryGetValue("volume", out var volumeCard) ||
            source != volumeCard && !source.GetVisualAncestors().Contains(volumeCard)) return;
        // Consume the wheel for the entire volume card before page navigation or
        // Slider can process it a second time. SetCurrentValue preserves binding.
        RecordLocalAudioInteraction();
        VolumeSlider.SetCurrentValue(Slider.ValueProperty,
            Math.Clamp(VolumeSlider.Value + e.Delta.Y * 5, VolumeSlider.Minimum, VolumeSlider.Maximum));
        e.Handled = true;
    }

    private void OnIslandWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_recognitionView is not null) { e.Handled = true; return; }
        if (_settings?.EnableWheelPages != true || e.Delta.Y == 0) return;
        if (e.Source is Visual visual && (visual is Slider or ListBox ||
            visual.GetVisualAncestors().Any(v => v is Slider or ListBox)))
        {
            e.Handled = true;
            return;
        }
        if (e.Source is Visual source && (source == PageScrollViewer || source.GetVisualAncestors().Contains(PageScrollViewer)) &&
            PageScrollViewer.Extent.Height > PageScrollViewer.Viewport.Height + 1) return;
        long now = Stopwatch.GetTimestamp();
        if (_lastWheelTimestamp != 0 && Stopwatch.GetElapsedTime(_lastWheelTimestamp, now).TotalMilliseconds < 350)
        {
            e.Handled = true;
            return;
        }
        _lastWheelTimestamp = now;
        if (!_expanded) SetExpanded(true);
        else SwitchPage(e.Delta.Y < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void SwitchPage(int delta)
    {
        if (_settings is null) return;
        CloseRecognition();
        var pages = _settings.AvailablePages;
        int pos = pages.ToList().IndexOf(_pageIndex);
        int next = Math.Clamp((pos < 0 ? 0 : pos) + delta, 0, pages.Count - 1);
        _pageIndex = pages[next];
        PageScrollViewer.Offset = default;
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
        bool Has(string feature) => _settings.PageHasFeature(feature, _pageIndex);
        PageTitle.Text = pages.Count > 1 ? $"{PageLabel("IslandModule", "Módulo")}  {position + 1:00}/{pages.Count:00}" : "";
        PreviousPageButton.IsEnabled = position > 0;
        NextPageButton.IsEnabled = position < pages.Count - 1;
        OpenDashboardButton.IsVisible = _settings.ShowMainButton;
        HideIslandButton.IsVisible = _settings.ShowHideButton;
        // La isla es un centro de uso, no una demo: las pruebas de ojos van en Ajustes.
        TestEyesButton.IsVisible = false;
        IslandFooter.IsVisible = _settings.ShowMainButton || _settings.ShowHideButton;

        bool volume = _settings.SoundShowVolume && Has("volume");
        bool mic = _settings.SoundShowMicrophone && Has("microphone");
        bool output = _settings.SoundShowOutputMute && Has("output");
        AudioControls.IsVisible = volume || mic || output;
        AudioPage.IsVisible = AudioControls.IsVisible;
        SoundMuteButtons.IsVisible = mic || output;
        MicControl.IsVisible = mic;
        OutputControl.IsVisible = output;
        VolumeRow.IsVisible = volume;
        VolumeValueText.IsVisible = volume;
        ProductivityPanel.IsVisible = _settings.ShortcutsShowRoutines && Has("routines");
        bool explorer = _settings.ShortcutsShowExplorer && Has("explorer");
        bool folders = _settings.ShortcutsShowFolders && Has("folders");
        QuickActionsPanel.IsVisible = _settings.ShowQuickActions &&
            (explorer || folders);
        ExplorerButton.IsVisible = explorer;
        AddFolderButton.IsVisible = folders;
        FolderCountLabel.IsVisible = folders && !_settings.PrivacyMode;
        QuickFolderList.IsVisible = folders && !_settings.PrivacyMode;
        FileShelfPanel.IsVisible = _settings.ShowFileShelf && Has("files") && !_settings.PrivacyMode;
        ShelfDetails.IsVisible = FileShelfPanel.IsVisible && _shelfOpen;
        ClipboardPanel.IsVisible = _settings.ShowClipboardActions && Has("clipboard") && !_settings.PrivacyMode;
        BatteryStatusText.IsVisible = _settings.ShowBatteryStatus && Has("battery");
        NetworkStatusText.IsVisible = _settings.ShowNetworkStatus && Has("network");
        SystemStatus.IsVisible = !_settings.PrivacyMode &&
            (BatteryStatusText.IsVisible || NetworkStatusText.IsVisible);
        WeatherPanel.IsVisible = _settings.ShowWeather && Has("weather") && !_settings.PrivacyMode;
        OverviewPage.IsVisible = (_settings.ShowMediaControls && Has("media")) || SystemStatus.IsVisible || WeatherPanel.IsVisible;
        QuickPage.IsVisible = ProductivityPanel.IsVisible || FileShelfPanel.IsVisible || ClipboardPanel.IsVisible;
        if (!WeatherPanel.IsVisible)
        {
            _weatherRequest?.Cancel();
            _weatherCityLast = "";
        }
        if (!ClipboardPanel.IsVisible) ClearClipboardPreview();

        RefreshMediaPresentation(false);
        ArrangeContentSections();

        if (_islandReady && _expanded)
            AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        if (WeatherPanel.IsVisible) RefreshWeatherIfNeeded();
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
                QuickFolderStatus.Text = "Acceso añadido. La carpeta no se ha modificado.";
            }
            else QuickFolderStatus.Text = "Carpeta duplicada, inaccesible o límite de ocho accesos.";
        }
        catch (Exception) { QuickFolderStatus.Text = "No se pudo abrir el selector de carpetas."; }
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: IslandViewModel.QuickFolderOption folder }) return;
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
        if (sender is not Button { CommandParameter: IslandViewModel.QuickFolderOption folder }) return;
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
