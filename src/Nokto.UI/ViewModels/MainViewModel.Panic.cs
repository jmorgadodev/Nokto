using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Platform.Windows.Hotkeys;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _panicHotkeyStatus = "Pendiente";
    private HotkeyChord? _registeredPanicChord;
    private bool _panicInitialized;

    private void InitializePanicHotkey()
    {
        _panicInitialized = true;
        LoadPanicHotkeySettings();
    }

    private void LoadPanicHotkeySettings()
    {
        PanicHotkeyText = _config.Settings.PanicHotkey;
        if (_panicInitialized) ActivatePanicHotkey(PanicHotkeyText);
    }

    private bool ActivatePanicHotkey(string text)
    {
        if (!HotkeyChord.TryParse(text, out var chord))
        {
            PanicHotkeyStatus = "Atajo inválido. Usa Pause, F9 o Ctrl+Shift+F9.";
            return false;
        }
        if (_registeredPanicChord == chord && _panicHotkeyService?.IsRegistered == true)
        {
            PanicHotkeyStatus = $"Activo: {text.Trim()} · finaliza todas las rutinas";
            return true;
        }
        // Register the replacement first; a conflict must not disable the existing panic key.
        var candidate = new GlobalHotkeyService(() => Dispatcher.UIThread.Post(() =>
        {
            if (!_disposalCts.IsCancellationRequested) FinishTask();
        }));
        try
        {
            if (!candidate.Start(chord.Modifiers, chord.VirtualKey))
            {
                candidate.Dispose();
                PanicHotkeyStatus = _panicHotkeyService?.IsRegistered == true
                    ? "Windows no pudo registrar ese atajo. El anterior sigue activo; elige otra combinación."
                    : "Windows no pudo activar el atajo de pánico. Elige una combinación que no esté en uso.";
                return false;
            }
            _panicHotkeyService?.Dispose();
            _panicHotkeyService = candidate;
            _registeredPanicChord = chord;
            PanicHotkeyStatus = $"Activo: {text.Trim()} · finaliza todas las rutinas";
            return true;
        }
        catch (Exception ex)
        {
            candidate.Dispose();
            PanicHotkeyStatus = $"No se pudo actualizar el atajo ({ex.GetType().Name}).";
            return false;
        }
    }

    [RelayCommand]
    private void ApplyPanicHotkey()
    {
        if (!HotkeyChord.TryParse(PanicHotkeyText, out var panic))
        {
            PanicHotkeyStatus = "Atajo inválido. Usa Pause, F9 o Ctrl+Shift+F9.";
            return;
        }
        if (HotkeyChord.TryParse(_config.Settings.MicMuteHotkey, out var microphone) && panic == microphone ||
            HotkeyChord.TryParse(_config.Settings.AudioMuteHotkey, out var output) && panic == output)
        {
            PanicHotkeyStatus = "El atajo de pánico debe ser distinto de los atajos de audio.";
            return;
        }
        if (!ActivatePanicHotkey(PanicHotkeyText)) return;
        _config.Settings.PanicHotkey = PanicHotkeyText.Trim();
        _persistence.SaveConfig(_config);
    }
}
