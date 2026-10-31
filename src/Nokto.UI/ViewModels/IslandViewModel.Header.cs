using CommunityToolkit.Mvvm.ComponentModel;

namespace Nokto.UI.ViewModels;

public partial class IslandViewModel
{
    [ObservableProperty] private bool _showCodexUsage;
    [ObservableProperty] private int _headerLeftIndex;
    [ObservableProperty] private int _headerRightIndex;
    private static readonly string[] LeftModes = ["Auto", "Codex", "Timer", "Audio", "None"];
    private static readonly string[] RightModes = ["Auto", "Clock", "Week", "Reset", "None"];
    public string HeaderLeftMode => LeftModes[Math.Clamp(HeaderLeftIndex, 0, LeftModes.Length - 1)];
    public string HeaderRightMode => RightModes[Math.Clamp(HeaderRightIndex, 0, RightModes.Length - 1)];
    private void InitializeHeaderPreferences()
    {
        ShowCodexUsage = _config.Island.ShowCodexUsage;
        HeaderLeftIndex = Math.Max(0, Array.IndexOf(LeftModes, _config.Island.HeaderLeftMode));
        HeaderRightIndex = Math.Max(0, Array.IndexOf(RightModes, _config.Island.HeaderRightMode));
    }
    private void SaveHeaderPreferences()
    {
        _config.Island.ShowCodexUsage = ShowCodexUsage;
        _config.Island.HeaderLeftMode = HeaderLeftMode;
        _config.Island.HeaderRightMode = HeaderRightMode;
    }
    partial void OnShowCodexUsageChanged(bool value) => Save();
    partial void OnHeaderLeftIndexChanged(int value) => Save();
    partial void OnHeaderRightIndexChanged(int value) => Save();
}
