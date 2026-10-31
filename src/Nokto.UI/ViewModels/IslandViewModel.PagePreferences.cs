using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Nokto.UI.ViewModels;

public sealed class IslandPageFeature : ObservableObject
{
    private readonly IslandViewModel _owner;
    public IslandViewModel Owner => _owner;
    public string Id { get; }
    public string Label { get; }
    public IslandPageFeature(IslandViewModel owner, string id, string label)
    {
        _owner = owner; Id = id; Label = label;
        Up1 = new RelayCommand(() => owner.MoveContent(id, 0, -1)); Down1 = new RelayCommand(() => owner.MoveContent(id, 0, 1));
        Up2 = new RelayCommand(() => owner.MoveContent(id, 1, -1)); Down2 = new RelayCommand(() => owner.MoveContent(id, 1, 1));
        Up3 = new RelayCommand(() => owner.MoveContent(id, 2, -1)); Down3 = new RelayCommand(() => owner.MoveContent(id, 2, 1));
    }
    public IRelayCommand Up1 { get; } public IRelayCommand Down1 { get; }
    public IRelayCommand Up2 { get; } public IRelayCommand Down2 { get; }
    public IRelayCommand Up3 { get; } public IRelayCommand Down3 { get; }
    public bool Page1 { get => _owner.FeatureInSlot(Id, 0); set => _owner.SetFeatureInSlot(Id, 0, value); }
    public bool Page2 { get => _owner.FeatureInSlot(Id, 1); set => _owner.SetFeatureInSlot(Id, 1, value); }
    public bool Page3 { get => _owner.FeatureInSlot(Id, 2); set => _owner.SetFeatureInSlot(Id, 2, value); }
    public void Refresh() { OnPropertyChanged(nameof(Page1)); OnPropertyChanged(nameof(Page2)); OnPropertyChanged(nameof(Page3)); }
}

/// <summary>Opciones independientes por página, con persistencia compatible.</summary>
public partial class IslandViewModel
{
    [ObservableProperty] private bool _showOverviewPage = true;
    [ObservableProperty] private bool _showSoundPage = true;
    [ObservableProperty] private bool _showShortcutsPage = true;
    [ObservableProperty] private bool _showMainButton = true;
    [ObservableProperty] private bool _showHideButton = true;
    [ObservableProperty] private bool _soundShowVolume = true;
    [ObservableProperty] private bool _soundShowMicrophone = true;
    [ObservableProperty] private bool _soundShowOutputMute = true;
    [ObservableProperty] private bool _shortcutsShowRoutines = true;
    [ObservableProperty] private bool _shortcutsShowExplorer = true;
    [ObservableProperty] private bool _shortcutsShowFolders = true;
    [ObservableProperty] private bool _showWeather;
    [ObservableProperty] private string _weatherCity = "";
    [ObservableProperty] private bool _weatherUseLocation;
    private int[] _pageOrder = [0, 1, 2];
    private Dictionary<string, int> _featureMasks = DefaultFeatureMasks();
    private bool _changingFeature;
    public ObservableCollection<IslandPageFeature> PageFeatures { get; } = [];
    private readonly Dictionary<int, List<string>> _contentOrder = new();
    public IReadOnlyList<IslandPageFeature> Slot1Features => OrderedFeatures(0);
    public IReadOnlyList<IslandPageFeature> Slot2Features => OrderedFeatures(1);
    public IReadOnlyList<IslandPageFeature> Slot3Features => OrderedFeatures(2);
    private IReadOnlyList<IslandPageFeature> OrderedFeatures(int slot)
        => ContentOrderForPage(_pageOrder[slot]).Select(id => PageFeatures.Single(f => f.Id == id)).ToArray();
    public IReadOnlyList<string> ContentOrderForPage(int page)
    {
        var valid = PageFeatures.Select(f => f.Id).ToArray();
        var saved = _contentOrder.GetValueOrDefault(page) ?? [];
        return saved.Where(valid.Contains).Concat(valid).Distinct().ToArray();
    }
    public void MoveContent(string id, int slot, int delta)
    {
        if (slot is < 0 or > 2) return;
        int page = _pageOrder[slot];
        var order = ContentOrderForPage(page).ToList();
        int index = order.IndexOf(id), target = index + delta;
        if (index < 0 || target < 0 || target >= order.Count) return;
        (order[index], order[target]) = (order[target], order[index]);
        _contentOrder[page] = order;
        NotifyContentOrder();
        PageLayoutVersion++;
        OnPropertyChanged(nameof(PageLayoutVersion));
        Save();
    }
    public bool ReorderContent(string id, int slot, string targetId, bool after)
    {
        if (slot is < 0 or > 2 || id == targetId) return false;
        int page = _pageOrder[slot];
        var order = ContentOrderForPage(page).ToList();
        if (!order.Contains(id) || !order.Contains(targetId)) return false;
        var original = order.ToArray();
        order.Remove(id); order.Insert(order.IndexOf(targetId) + (after ? 1 : 0), id);
        if (order.SequenceEqual(original)) return false;
        _contentOrder[page] = order; NotifyContentOrder(); PageLayoutVersion++;
        OnPropertyChanged(nameof(PageLayoutVersion)); Save(); return true;
    }
    private void NotifyContentOrder()
    {
        OnPropertyChanged(nameof(Slot1Features)); OnPropertyChanged(nameof(Slot2Features)); OnPropertyChanged(nameof(Slot3Features));
    }
    public int PageLayoutVersion { get; private set; }
    private static Dictionary<string, int> DefaultFeatureMasks() => new()
    {
        ["media"] = 1, ["battery"] = 1, ["network"] = 1, ["weather"] = 1,
        ["volume"] = 2, ["microphone"] = 2, ["output"] = 2,
        ["routines"] = 4, ["explorer"] = 4, ["folders"] = 4, ["files"] = 4, ["clipboard"] = 4, ["codex"] = 0
    };
    public bool PageHasFeature(string id, int page) => page is >= 0 and <= 2 &&
        _featureMasks.TryGetValue(id, out int mask) && (mask & (1 << page)) != 0;
    public bool FeatureInSlot(string id, int slot) => slot is >= 0 and <= 2 && FeatureEnabled(id) && PageHasFeature(id, _pageOrder[slot]);
    private bool FeatureEnabled(string id) => id switch
    {
        "media" => ShowMediaControls, "battery" => ShowBatteryStatus, "network" => ShowNetworkStatus,
        "weather" => ShowWeather, "volume" => SoundShowVolume, "microphone" => SoundShowMicrophone,
        "output" => SoundShowOutputMute, "routines" => ShortcutsShowRoutines,
        "explorer" => ShowQuickActions && ShortcutsShowExplorer, "folders" => ShowQuickActions && ShortcutsShowFolders,
        "files" => ShowFileShelf, "clipboard" => ShowClipboardActions, "codex" => ShowCodexUsage, _ => false
    };
    public int PageSlotNumber(int page) => Array.IndexOf(_pageOrder, page) + 1;
    public void SetFeatureInSlot(string id, int slot, bool value)
    {
        if (_changingFeature || slot is < 0 or > 2 || !_featureMasks.TryGetValue(id, out int mask)) return;
        int bit = 1 << _pageOrder[slot];
        int updated = value ? mask | bit : mask & ~bit;
        if (mask == updated && (!value || FeatureEnabled(id))) return;
        _featureMasks[id] = updated;
        if (value)
        {
            _changingFeature = true;
            try { EnableFeature(id); }
            finally { _changingFeature = false; }
        }
        PageLayoutVersion++;
        OnPropertyChanged(nameof(PageLayoutVersion));
        Save();
    }
    private void EnableFeature(string id)
    {
        switch (id)
        {
            case "media": ShowMediaControls = true; break;
            case "battery": ShowBatteryStatus = true; break;
            case "network": ShowNetworkStatus = true; break;
            case "weather": ShowWeather = true; break;
            case "volume": SoundShowVolume = true; break;
            case "microphone": SoundShowMicrophone = true; break;
            case "output": SoundShowOutputMute = true; break;
            case "routines": ShortcutsShowRoutines = true; break;
            case "explorer": ShowQuickActions = true; ShortcutsShowExplorer = true; break;
            case "folders": ShowQuickActions = true; ShortcutsShowFolders = true; break;
            case "files": ShowFileShelf = true; break;
            case "clipboard": ShowClipboardActions = true; break;
            case "codex": ShowCodexUsage = true; break;
        }
    }
    public int PageOne { get => _pageOrder[0]; set => SetPageOrder(0, value); }
    public int PageTwo { get => _pageOrder[1]; set => SetPageOrder(1, value); }
    public int PageThree { get => _pageOrder[2]; set => SetPageOrder(2, value); }
    public bool ShowSlot1 { get => SlotEnabled(0); set => SetSlotEnabled(0, value); }
    public bool ShowSlot2 { get => SlotEnabled(1); set => SetSlotEnabled(1, value); }
    public bool ShowSlot3 { get => SlotEnabled(2); set => SetSlotEnabled(2, value); }
    private bool SlotEnabled(int slot) => _pageOrder[slot] switch
    { 0 => ShowOverviewPage, 1 => ShowSoundPage, _ => ShowShortcutsPage };
    private void SetSlotEnabled(int slot, bool value)
    {
        switch (_pageOrder[slot])
        {
            case 0: ShowOverviewPage = value; break;
            case 1: ShowSoundPage = value; break;
            default: ShowShortcutsPage = value; break;
        }
    }
    private void NotifySlots()
    {
        OnPropertyChanged(nameof(ShowSlot1)); OnPropertyChanged(nameof(ShowSlot2)); OnPropertyChanged(nameof(ShowSlot3));
    }
    public void AddAudioToSlot(int slot)
    {
        if (slot is < 0 or > 2) return;
        SetSlotEnabled(slot, true);
        foreach (string feature in new[] { "media", "volume", "microphone", "output" })
        {
            SetFeatureInSlot(feature, slot, true);
            for (int other = 0; other < 3; other++)
                if (other != slot) SetFeatureInSlot(feature, other, false);
        }
    }
    [RelayCommand]
    private void AddAudioToPage(string? slot)
    {
        if (int.TryParse(slot, out int number)) AddAudioToSlot(number);
    }

    private void SetPageOrder(int slot, int page)
    {
        if (page < 0 || page > 2 || _pageOrder[slot] == page) return;
        int other = Array.IndexOf(_pageOrder, page);
        (_pageOrder[slot], _pageOrder[other]) = (_pageOrder[other], _pageOrder[slot]);
        OnPropertyChanged(nameof(PageOne));
        OnPropertyChanged(nameof(PageTwo));
        OnPropertyChanged(nameof(PageThree));
        NotifySlots();
        NotifyContentOrder();
        foreach (var feature in PageFeatures) feature.Refresh();
        PageLayoutVersion++;
        OnPropertyChanged(nameof(PageLayoutVersion));
        Save();
    }
    [ObservableProperty] private bool _showWindowsNotifications;
    [ObservableProperty] private bool _privacyMode;

    public IReadOnlyList<int> AvailablePages
    {
        get
        {
            var pages = _pageOrder.Where(page => page switch
            { 0 => ShowOverviewPage, 1 => ShowSoundPage, _ => ShowShortcutsPage }).ToList();
            return pages.Count == 0 ? [1] : pages;
        }
    }

    private void InitializePagePreferences()
    {
        var p = _config.Island;
        if (p.ContentOrder is not null)
            foreach (var pair in p.ContentOrder.Where(x => x.Key is >= 0 and <= 2)) _contentOrder[pair.Key] = pair.Value ?? [];
        _pageOrder = p.PageOrder is { Count: 3 } && p.PageOrder.Order().SequenceEqual(new[] { 0, 1, 2 })
            ? p.PageOrder.ToArray() : [0, 1, 2];
        _featureMasks = DefaultFeatureMasks();
        if (p.PageFeatures is not null)
            foreach (var pair in p.PageFeatures)
                if (_featureMasks.ContainsKey(pair.Key)) _featureMasks[pair.Key] = pair.Value & 7;
        foreach (var (id, label) in new[] { ("media", "Reproductor musical"), ("volume", "Volumen del sistema"),
            ("microphone", "Micrófono"), ("output", "Silenciar salida"), ("battery", "Batería"),
            ("network", "Red"), ("weather", "Clima"), ("routines", "Rutinas"), ("explorer", "Explorador"),
            ("folders", "Carpetas"), ("files", "Archivos"), ("clipboard", "Portapapeles"), ("codex", "Cuota de Codex") })
            PageFeatures.Add(new IslandPageFeature(this, id, label));
        ShowOverviewPage = p.ShowOverviewPage;
        ShowSoundPage = p.ShowSoundPage;
        ShowShortcutsPage = p.ShowShortcutsPage;
        ShowMainButton = p.ShowMainButton;
        ShowHideButton = p.ShowHideButton;
        SoundShowVolume = p.SoundShowVolume;
        SoundShowMicrophone = p.SoundShowMicrophone;
        SoundShowOutputMute = p.SoundShowOutputMute;
        ShortcutsShowRoutines = p.ShortcutsShowRoutines;
        ShortcutsShowExplorer = p.ShortcutsShowExplorer;
        ShortcutsShowFolders = p.ShortcutsShowFolders;
        ShowWeather = p.ShowWeather;
        WeatherUseLocation = p.WeatherUseLocation;
        WeatherCity = p.WeatherCity ?? "";
        ShowWindowsNotifications = p.ShowWindowsNotifications;
        PrivacyMode = p.PrivacyMode;
    }

    private void SavePagePreferences()
    {
        var p = _config.Island;
        p.PageOrder = _pageOrder.ToList();
        p.PageFeatures = new Dictionary<string, int>(_featureMasks);
        p.ContentOrder = _contentOrder.ToDictionary(x => x.Key, x => x.Value.ToList());
        p.ShowOverviewPage = ShowOverviewPage;
        p.ShowSoundPage = ShowSoundPage;
        p.ShowShortcutsPage = ShowShortcutsPage;
        p.ShowMainButton = ShowMainButton;
        p.ShowHideButton = ShowHideButton;
        p.SoundShowVolume = SoundShowVolume;
        p.SoundShowMicrophone = SoundShowMicrophone;
        p.SoundShowOutputMute = SoundShowOutputMute;
        p.ShortcutsShowRoutines = ShortcutsShowRoutines;
        p.ShortcutsShowExplorer = ShortcutsShowExplorer;
        p.ShortcutsShowFolders = ShortcutsShowFolders;
        p.ShowWeather = ShowWeather;
        p.WeatherUseLocation = WeatherUseLocation;
        p.WeatherCity = WeatherCity?.Trim().Length > 80 ? WeatherCity.Trim()[..80] : WeatherCity?.Trim() ?? "";
        p.ShowWindowsNotifications = ShowWindowsNotifications;
        p.PrivacyMode = PrivacyMode;
    }
    partial void OnShowOverviewPageChanged(bool value) { NotifySlots(); Save(); }
    partial void OnShowSoundPageChanged(bool value) { NotifySlots(); Save(); }
    partial void OnShowShortcutsPageChanged(bool value) { NotifySlots(); Save(); }
    partial void OnShowMainButtonChanged(bool value) => Save();
    partial void OnShowHideButtonChanged(bool value) => Save();
    partial void OnSoundShowVolumeChanged(bool value) => Save();
    partial void OnSoundShowMicrophoneChanged(bool value) => Save();
    partial void OnSoundShowOutputMuteChanged(bool value) => Save();
    partial void OnShortcutsShowRoutinesChanged(bool value) => Save();
    partial void OnShortcutsShowExplorerChanged(bool value) => Save();
    partial void OnShortcutsShowFoldersChanged(bool value) => Save();
    partial void OnShowWeatherChanged(bool value) => Save();
    partial void OnWeatherCityChanged(string value) => Save();
    partial void OnWeatherUseLocationChanged(bool value) => Save();
    partial void OnShowWindowsNotificationsChanged(bool value) => Save();
    partial void OnPrivacyModeChanged(bool value) => Save();
}
