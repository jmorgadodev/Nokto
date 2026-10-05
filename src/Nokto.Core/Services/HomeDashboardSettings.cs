using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nokto.Core.Models;

namespace Nokto.Core.Services;

/// <summary>Reactive home visibility settings backed by the existing local configuration.</summary>
public sealed class HomeDashboardSettings : INotifyPropertyChanged
{
    private AppSettings _settings;
    private readonly Action _save;
    public event PropertyChangedEventHandler? PropertyChanged;

    public HomeDashboardSettings(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    public bool ShowNetworkCardInHome
    {
        get => _settings.ShowNetworkCardInHome;
        set => SetVisibility(_settings.ShowNetworkCardInHome, value, v => _settings.ShowNetworkCardInHome = v);
    }
    public bool ShowEnergyStatusCardInHome
    {
        get => _settings.ShowEnergyStatusCardInHome;
        set => SetVisibility(_settings.ShowEnergyStatusCardInHome, value, v => _settings.ShowEnergyStatusCardInHome = v);
    }
    public bool ShowHardwareCardInHome
    {
        get => _settings.ShowHardwareCardInHome;
        set => SetVisibility(_settings.ShowHardwareCardInHome, value, v => _settings.ShowHardwareCardInHome = v);
    }
    public bool ShowAiRadarCardInHome
    {
        get => _settings.ShowAiRadarCardInHome;
        set => SetVisibility(_settings.ShowAiRadarCardInHome, value, v => _settings.ShowAiRadarCardInHome = v);
    }
    public bool ShowQuickActionsInHome
    {
        get => _settings.ShowQuickActionsInHome;
        set => SetVisibility(_settings.ShowQuickActionsInHome, value, v => _settings.ShowQuickActionsInHome = v);
    }
    public bool ShowAudioControlCardInHome
    {
        get => _settings.ShowAudioControlCardInHome;
        set => SetVisibility(_settings.ShowAudioControlCardInHome, value, v => _settings.ShowAudioControlCardInHome = v);
    }

    public void Reload(AppSettings settings)
    {
        if (ReferenceEquals(_settings, settings)) return;
        _settings = settings;
        foreach (string name in new[] { nameof(ShowNetworkCardInHome), nameof(ShowEnergyStatusCardInHome),
            nameof(ShowHardwareCardInHome), nameof(ShowAiRadarCardInHome), nameof(ShowQuickActionsInHome), nameof(ShowAudioControlCardInHome) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void SetVisibility(bool current, bool value, Action<bool> update, [CallerMemberName] string? name = null)
    {
        if (current == value) return;
        update(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        _save();
    }
}
