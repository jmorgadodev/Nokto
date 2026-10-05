using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Hotkeys;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel
{
    private GlobalHotkeyService? _micMuteHotkeyService;
    private GlobalHotkeyService? _audioMuteHotkeyService;
    private bool _syncingAudio;
    private bool _audioControlsInitialized;

    public ObservableCollection<AudioEndpointInfo> InputAudioDevices { get; } = [];
    public ObservableCollection<AudioEndpointInfo> OutputAudioDevices { get; } = [];
    [ObservableProperty] private AudioEndpointInfo? _selectedInputAudioDevice;
    [ObservableProperty] private AudioEndpointInfo? _selectedOutputAudioDevice;
    [ObservableProperty] private double _masterVolumePercent;
    [ObservableProperty] private string _micMuteHotkeyText = "Ctrl+Shift+M";
    [ObservableProperty] private string _audioMuteHotkeyText = "Ctrl+Shift+S";
    [ObservableProperty] private string _micHotkeyStatus = "Pendiente";
    [ObservableProperty] private string _audioHotkeyStatus = "Pendiente";
    [ObservableProperty] private string _audioControlStatusText = "";

    public bool HasInputAudio => AudioDevices.InputMuted.HasValue;
    public bool HasOutputAudio => AudioDevices.OutputMuted.HasValue && AudioDevices.OutputVolumePercent.HasValue;
    public string InputMuteButtonText => AudioDevices.InputMuted switch
    {
        true => "🔇 Micrófono: Silenciado (🔴)", false => "🎙 Micrófono: Activo (🟢)", null => "Micrófono no disponible"
    };
    public string OutputMuteButtonText => AudioDevices.OutputMuted switch
    {
        true => "🔇 Salida Silenciada", false => "🔊 Silenciar Salida", null => "Salida no disponible"
    };
    public string OutputMuteIcon => AudioDevices.OutputMuted switch { true => "🔇", false => "🔊", null => "—" };
    public bool IsOutputMuted => AudioDevices.OutputMuted == true;
    public string MasterVolumeText => $"{MasterVolumePercent:0}%";
    public string MicHotkeyDisplay => _config.Settings.MicMuteHotkey.Replace("+", " + ");
    public string AudioHotkeyDisplay => _config.Settings.AudioMuteHotkey.Replace("+", " + ");
    public bool HasAudioControlStatus => !string.IsNullOrEmpty(AudioControlStatusText);
    public string DiskFooterText => DiskText.Replace(" GB / ", " / ").Replace(" GB libres", " GB");

    partial void OnDiskTextChanged(string value) => OnPropertyChanged(nameof(DiskFooterText));
    partial void OnAudioControlStatusTextChanged(string value) => OnPropertyChanged(nameof(HasAudioControlStatus));
    partial void OnAudioDevicesChanged(AudioDeviceProfile value)
    {
        OnPropertyChanged(nameof(HasInputAudio));
        OnPropertyChanged(nameof(HasOutputAudio));
        OnPropertyChanged(nameof(InputMuteButtonText));
        OnPropertyChanged(nameof(OutputMuteButtonText));
        OnPropertyChanged(nameof(OutputMuteIcon));
        OnPropertyChanged(nameof(IsOutputMuted));
    }

    private void InitializeAudioControls()
    {
        _audioControlsInitialized = true;
        RefreshAudioPanel(true);
        RegisterAudioHotkeys(_config.Settings.MicMuteHotkey, _config.Settings.AudioMuteHotkey);
    }

    private void LoadAudioSettings()
    {
        MicMuteHotkeyText = _config.Settings.MicMuteHotkey;
        AudioMuteHotkeyText = _config.Settings.AudioMuteHotkey;
        OnPropertyChanged(nameof(MicHotkeyDisplay));
        OnPropertyChanged(nameof(AudioHotkeyDisplay));
        if (_audioControlsInitialized) RegisterAudioHotkeys(MicMuteHotkeyText, AudioMuteHotkeyText);
    }

    private void RefreshAudioPanel(bool enumerate = false)
    {
        _syncingAudio = true;
        try
        {
            AudioDevices = _systemAdapter.GetAudioDevices();
            // Windows' multimedia endpoint ID is authoritative; list order is never a default selector.
            if (enumerate || !OutputAudioDevices.Any(d => d.Id == AudioDevices.OutputId && d.IsDefault) ||
                !InputAudioDevices.Any(d => d.Id == AudioDevices.InputId && d.IsDefault))
            {
                UpdateEndpointList(InputAudioDevices, _systemAdapter.GetInputAudioDevices());
                UpdateEndpointList(OutputAudioDevices, _systemAdapter.GetOutputAudioDevices());
            }
            SelectedInputAudioDevice = InputAudioDevices.FirstOrDefault(d => d.Id == AudioDevices.InputId);
            SelectedOutputAudioDevice = OutputAudioDevices.FirstOrDefault(d => d.Id == AudioDevices.OutputId);
            MasterVolumePercent = AudioDevices.OutputVolumePercent ?? 0;
        }
        finally { _syncingAudio = false; }
    }

    private static void UpdateEndpointList(ObservableCollection<AudioEndpointInfo> target, IReadOnlyList<AudioEndpointInfo> devices)
    {
        if (target.SequenceEqual(devices)) return;
        target.Clear();
        foreach (var device in devices) target.Add(device);
    }

    partial void OnSelectedInputAudioDeviceChanged(AudioEndpointInfo? value) => SelectAudioDevice(value, true);
    partial void OnSelectedOutputAudioDeviceChanged(AudioEndpointInfo? value) => SelectAudioDevice(value, false);
    private void SelectAudioDevice(AudioEndpointInfo? device, bool input)
    {
        if (_syncingAudio || device == null) return;
        AudioControlStatusText = _systemAdapter.SetDefaultAudioDevice(device.Id, input)
            ? "Dispositivo predeterminado actualizado."
            : "Windows no pudo cambiar el dispositivo. Puedes seleccionarlo en Configuración de Sonido.";
        RefreshAudioPanel(true);
    }

    partial void OnMasterVolumePercentChanged(double value)
    {
        OnPropertyChanged(nameof(MasterVolumeText));
        if (!_syncingAudio && double.IsFinite(value)) _ = SetAudioVolumeAsync(value);
    }
    private async Task SetAudioVolumeAsync(double value)
    {
        try
        {
            await _systemAdapter.SetMasterVolumeAsync((float)Math.Clamp(value / 100, 0, 1));
            RefreshAudioPanel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            AudioControlStatusText = "No se pudo ajustar el volumen de la salida.";
        }
    }

    [RelayCommand]
    private void ToggleInputMute()
    {
        AudioControlStatusText = _systemAdapter.ToggleInputMute() ? "" : "No se pudo cambiar el silencio del micrófono.";
        RefreshAudioPanel();
    }
    [RelayCommand]
    private void ToggleOutputMute()
    {
        AudioControlStatusText = _systemAdapter.ToggleOutputMute() ? "" : "No se pudo cambiar el silencio de la salida.";
        RefreshAudioPanel();
    }
    [RelayCommand]
    private void OpenWindowsSoundSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            AudioControlStatusText = "No se pudo abrir la configuración de sonido de Windows.";
        }
    }

    private bool RegisterAudioHotkeys(string microphone, string output)
    {
        _micMuteHotkeyService ??= new GlobalHotkeyService(() => Dispatcher.UIThread.Post(() =>
        {
            if (!_disposalCts.IsCancellationRequested) ToggleInputMute();
        }));
        _audioMuteHotkeyService ??= new GlobalHotkeyService(() => Dispatcher.UIThread.Post(() =>
        {
            if (!_disposalCts.IsCancellationRequested) ToggleOutputMute();
        }));
        _micMuteHotkeyService.Stop();
        _audioMuteHotkeyService.Stop();
        bool micOk = StartAudioHotkey(_micMuteHotkeyService, microphone);
        bool outputOk = StartAudioHotkey(_audioMuteHotkeyService, output);
        MicHotkeyStatus = micOk ? "Registrado" : "No disponible: combinación inválida o ya en uso";
        AudioHotkeyStatus = outputOk ? "Registrado" : "No disponible: combinación inválida o ya en uso";
        return micOk && outputOk;
    }
    private static bool StartAudioHotkey(GlobalHotkeyService service, string text) =>
        HotkeyChord.TryParse(text, out var chord) && service.Start(chord.Modifiers, chord.VirtualKey);

    [RelayCommand]
    private void ApplyAudioHotkeys()
    {
        if (!HotkeyChord.TryParse(MicMuteHotkeyText, out var mic) || !HotkeyChord.TryParse(AudioMuteHotkeyText, out var output) ||
            mic == output || mic == _registeredPanicChord || output == _registeredPanicChord ||
            HotkeyChord.TryParse(_config.Settings.PanicHotkey, out var configuredPanic) && (mic == configuredPanic || output == configuredPanic))
        {
            AudioControlStatusText = "Usa atajos distintos para micrófono, salida y pánico. Por ejemplo Ctrl+Shift+M o F8.";
            return;
        }
        if (!RegisterAudioHotkeys(MicMuteHotkeyText, AudioMuteHotkeyText))
        {
            LoadAudioSettings();
            AudioControlStatusText = "Windows no pudo registrar los nuevos atajos. Se conservaron los anteriores; elige otra combinación.";
            return;
        }
        _config.Settings.MicMuteHotkey = MicMuteHotkeyText.Trim();
        _config.Settings.AudioMuteHotkey = AudioMuteHotkeyText.Trim();
        _persistence.SaveConfig(_config);
        OnPropertyChanged(nameof(MicHotkeyDisplay));
        OnPropertyChanged(nameof(AudioHotkeyDisplay));
        AudioControlStatusText = "Atajos globales guardados y activos.";
    }

    private void DisposeAudioControls()
    {
        _micMuteHotkeyService?.Dispose();
        _audioMuteHotkeyService?.Dispose();
    }
}
