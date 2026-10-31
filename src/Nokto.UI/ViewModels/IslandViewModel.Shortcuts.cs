using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Nokto.UI.ViewModels;

public partial class IslandViewModel
{
    public sealed record QuickFolderOption(string Path, string DisplayName);
    public ObservableCollection<QuickFolderOption> QuickFolders { get; } = [];

    [ObservableProperty] private bool _showQuickActions = true;
    [ObservableProperty] private bool _enableWheelPages = true;
    [ObservableProperty] private bool _showClipboardActions;

    public string QuickFoldersLabel => QuickFolders.Count == 0 ? "Añade las carpetas que usas a diario" :
        QuickFolders.Count == 1 ? "1 acceso guardado" : $"{QuickFolders.Count} accesos guardados";
    private static string FolderName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void InitializeShortcuts()
    {
        ShowQuickActions = _config.Island.ShowQuickActions;
        EnableWheelPages = _config.Island.EnableWheelPages;
        ShowClipboardActions = _config.Island.ShowClipboardActions;
        foreach (var folder in (_config.Island.QuickFolders ?? []).Take(8))
        {
            if (!string.IsNullOrWhiteSpace(folder))
                QuickFolders.Add(new QuickFolderOption(folder, FolderName(folder)));
        }
    }

    public bool AddQuickFolder(string folder)
    {
        if (QuickFolders.Count >= 8 || !Directory.Exists(folder)) return false;
        string path = Path.GetFullPath(folder);
        if (QuickFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) return false;
        QuickFolders.Add(new QuickFolderOption(path, FolderName(path)));
        OnPropertyChanged(nameof(QuickFoldersLabel));
        Save();
        return true;
    }

    public bool RemoveQuickFolder(string? path)
    {
        var folder = QuickFolders.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (folder is null) return false;
        QuickFolders.Remove(folder);
        OnPropertyChanged(nameof(QuickFoldersLabel));
        Save();
        return true;
    }

    private void SaveShortcuts()
    {
        _config.Island.ShowQuickActions = ShowQuickActions;
        _config.Island.EnableWheelPages = EnableWheelPages;
        _config.Island.ShowClipboardActions = ShowClipboardActions;
        _config.Island.QuickFolders = QuickFolders.Select(f => f.Path).ToList();
    }

    partial void OnShowQuickActionsChanged(bool value) => Save();
    partial void OnEnableWheelPagesChanged(bool value) => Save();
    partial void OnShowClipboardActionsChanged(bool value) => Save();
}
