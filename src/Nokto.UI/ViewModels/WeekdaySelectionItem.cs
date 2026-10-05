using CommunityToolkit.Mvvm.ComponentModel;

namespace Nokto.UI.ViewModels;

public partial class WeekdaySelectionItem(string label, string name, int day) : ObservableObject
{
    public string Label { get; } = label;
    public string Name { get; } = name;
    public int Day { get; } = day;
    [ObservableProperty] private bool _isSelected = true;
}
