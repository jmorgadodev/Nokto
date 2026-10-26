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
    private string? _clipboardText;

    private void OnPagePrevious(object? sender, RoutedEventArgs e) => SwitchPage(-1);
    private void OnPageNext(object? sender, RoutedEventArgs e) => SwitchPage(1);

    private void OnIslandWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_settings?.EnableWheelPages != true || e.Delta.Y == 0) return;
        // Lists and the volume slider retain their usual wheel behavior.
        if (e.Source is Visual visual &&
            (visual is Slider or ListBox ||
             visual.GetVisualAncestors().Any(v => v is Slider or ListBox)))
            return;
        if (!_expanded)
        {
            _pinned = true;
            SetExpanded(true);
        }
        else SwitchPage(e.Delta.Y < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void SwitchPage(int delta)
    {
        // Navigating is intentional interaction: keep the panel open so pointer
        // leave notifications during layout changes cannot collapse it.
        _pinned = true;
        _pageIndex = (_pageIndex + delta + 3) % 3;
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
        OverviewPage.IsVisible = _pageIndex == 0;
        AudioPage.IsVisible = _pageIndex == 1;
        QuickPage.IsVisible = _pageIndex == 2;
        PageTitle.Text = (_pageIndex switch
        {
            0 => PageLabel("IslandPageOverview", "Resumen"),
            1 => PageLabel("IslandPageAudio", "Sonido"),
            _ => PageLabel("IslandPageShortcuts", "Accesos")
        }) + $"  ·  {_pageIndex + 1}/3";
        QuickActionsPanel.IsVisible = _settings?.ShowQuickActions == true;
        ClipboardPanel.IsVisible = _settings?.ShowClipboardActions == true;
        if (_pageIndex != 2 || !ClipboardPanel.IsVisible) ClearClipboardPreview();
        if (_islandReady && _expanded)
            AnimateTo(ExpandedWidth, ExpandedHeightForContent);
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
                Title = "Seleccionar carpeta para Nokto Island",
                AllowMultiple = false
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
        {
            QuickFolderStatus.Text = "La carpeta ya no existe.";
            return;
        }
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
        if (_settings?.ShowClipboardActions != true) return;
        try
        {
            string? content = await Clipboard!.GetTextAsync();
            if (string.IsNullOrEmpty(content))
            {
                ClipboardPreview.Text = "No hay texto disponible.";
                return;
            }
            // No persistir; la lectura se realiza solo tras pulsar y se elimina al cambiar.
            _clipboardText = content.Length > 110 ? content[..110] + "…" : content;
            ClipboardPreview.Text = _clipboardText.Replace('\r', ' ').Replace('\n', ' ');
        }
        catch (Exception) { ClipboardPreview.Text = "No se pudo leer texto del portapapeles."; }
    }
    private void ClearClipboardPreview()
    {
        _clipboardText = null;
        ClipboardPreview.Text = "";
    }
}
