using CommunityToolkit.Mvvm.ComponentModel;
using Nokto.Core.Models;

namespace Nokto.UI.ViewModels;

public partial class IslandViewModel
{
    public IReadOnlyList<IslandNoticeOption> NoticeOptions { get; private set; } = [];
    public IEnumerable<IslandNoticeOption> AvailableNoticeOptions => NoticeOptions.Where(x => x.Available);
    public int NoticePreferencesVersion { get; private set; }
    private bool _syncingNoticeGroups;

    private void InitializeNotices(IslandSettings settings)
    {
        bool Legacy(string group) => group switch { "system" => ShowSystemOverlays, "keys" => ShowKeyNotices, "connection" => ShowConnectionNotices, _ => false };
        NoticeOptions = IslandNoticeCatalog.All.Select(d => new IslandNoticeOption(d,
            d.Available && (settings.NoticePreferences?.TryGetValue(d.Id, out var enabled) == true ? enabled : Legacy(d.Group)), NoticePreferenceChanged)).ToArray();
    }
    private void NoticePreferenceChanged()
    {
        if (_loading || _syncingNoticeGroups) return;
        Save(); NoticePreferencesVersion++; OnPropertyChanged(nameof(NoticePreferencesVersion));
    }
    private void SetLegacyNoticeGroup(string group, bool value)
    {
        _syncingNoticeGroups = true;
        foreach (var option in NoticeOptions.Where(x => x.Group == group)) option.IsEnabled = value;
        _syncingNoticeGroups = false; NoticePreferenceChanged();
    }
    public bool IsNoticeEnabled(string id) => id switch
    {
        "keys" => IsNoticeEnabled("capsLock") || IsNoticeEnabled("keyboardLayout"),
        "connection" => IsNoticeEnabled("network") || IsNoticeEnabled("power") || IsNoticeEnabled("audioDevice"),
        _ => NoticeOptions.Any(x => x.Id == id && x.Available && x.IsEnabled)
    };
    public HashSet<string> EnabledNoticeIds() => NoticeOptions.Where(x => x.Available && x.IsEnabled).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
}

public sealed class IslandNoticeOption(IslandNoticeDefinition definition, bool enabled, Action changed) : ObservableObject
{
    public string Id => definition.Id;
    public string Title => definition.Title;
    public string Description => definition.Description;
    public string Group => definition.Group;
    public bool Available => definition.Available;
    private bool _isEnabled = enabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (Available && SetProperty(ref _isEnabled, value)) changed(); }
    }
}
