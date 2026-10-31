using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Networking.Connectivity;
using Windows.System.Power;
using IslandNoticeCatalog = Nokto.Core.Models.IslandNoticeCatalog;

namespace Nokto.Platform.Windows.Notifications;

public sealed record IslandSystemNotice(string Kind, string Title, string Detail, double? Level = null);

/// <summary>Eventos de sistema. Teclado: solo estados Mayús/Num e idioma; nunca caracteres ni historial.</summary>
public sealed partial class IslandSystemObserver : IDisposable, IMMNotificationClient
{
    private readonly HashSet<string> _enabled;
    private readonly object _audioLock = new();
    private readonly object _lifetimeLock = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _output;
    private MMDevice? _input;
    private bool _inputMuted;
    private ManagementEventWatcher? _brightness;
    private Timer? _keys;
    private volatile bool _disposed;
    private bool _started, _caps, _num, _internet, _ac;
    private string? _lastEndpoint;
    private DateTime _lastEndpointAt;
    private long _layout;
    public event Action<IslandSystemNotice>? Notice;
    public IslandSystemObserver(IEnumerable<string>? enabled = null)
        => _enabled = (enabled ?? IslandNoticeCatalog.All.Where(x => x.Group is "system" or "keys" or "connection").Select(x => x.Id)).ToHashSet(StringComparer.Ordinal);
    private bool Wants(string id) => _enabled.Contains(id);

    public void Start()
    {
        lock (_lifetimeLock)
        {
        if (_disposed || _started) return;
        _started = true;
        if (Wants("network")) try { _internet = HasInternet(); NetworkInformation.NetworkStatusChanged += NetworkChanged; } catch (Exception) { }
        StartPowerNotices();
        if (Wants("volume") || Wants("audioDevice") || Wants("microphone"))
        try
        {
            _enumerator = new MMDeviceEnumerator(); _enumerator.RegisterEndpointNotificationCallback(this); BindVolume();
        }
        catch (Exception) { }
        if (Wants("brightness")) try
        {
            _brightness = new ManagementEventWatcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessEvent");
            _brightness.EventArrived += BrightnessChanged; _brightness.Start();
        }
        catch (Exception) { _brightness?.Dispose(); _brightness = null; }
        if (Wants("capsLock") || Wants("numLock") || Wants("keyboardLayout"))
        {
            _caps = (GetKeyState(0x14) & 1) != 0; _num = (GetKeyState(0x90) & 1) != 0; _layout = ReadLayout();
            _keys = new Timer(_ => ReadKeys(), null, 300, 300);
        }
        StartDeviceNotices();
        }
    }

    private void Emit(IslandSystemNotice notice) { if (!_disposed && Wants(notice.Kind)) Notice?.Invoke(notice); }
    private static bool HasInternet() => NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
    private static bool HasAc() => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
    private void NetworkChanged(object sender)
    {
        try
        {
            bool next = HasInternet(); if (next == _internet) return; _internet = next;
            Emit(new("network", next ? "Conexión recuperada" : "Sin conexión a Internet", next ? "La red vuelve a estar disponible" : "Comprueba Wi-Fi o el cable de red"));
        }
        catch (Exception) { }
    }
    private void PowerChanged(object? sender, object args)
    {
        try
        {
            bool next = HasAc(); if (next == _ac) return; _ac = next;
            if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) return;
            Emit(new("power", next ? "Cargador conectado" : "Cargador desconectado", $"Batería · {PowerManager.RemainingChargePercent}%", PowerManager.RemainingChargePercent));
        }
        catch (Exception) { }
    }
    private void BrightnessChanged(object sender, EventArrivedEventArgs args)
    {
        try { int level = Convert.ToInt32(args.NewEvent["Brightness"], CultureInfo.InvariantCulture); Emit(new("brightness", "Brillo de pantalla", $"{level}%", level)); }
        catch (Exception) { }
    }
    private void ReadKeys()
    {
        if (_disposed) return;
        bool caps = (GetKeyState(0x14) & 1) != 0;
        if (Wants("capsLock") && caps != _caps) { _caps = caps; Emit(new("capsLock", caps ? "Bloq Mayús activado" : "Bloq Mayús desactivado", caps ? "Escritura en mayúsculas" : "Escritura normal")); }
        bool num = (GetKeyState(0x90) & 1) != 0;
        if (Wants("numLock") && num != _num) { _num = num; Emit(new("numLock", num ? "Bloq Num activado" : "Bloq Num desactivado", num ? "Teclado numérico disponible" : "Teclado numérico desactivado")); }
        if (!Wants("keyboardLayout")) return;
        long layout = ReadLayout();
        if (layout == 0 || layout == _layout) return;
        _layout = layout;
        try { Emit(new("keyboardLayout", "Idioma del teclado", CultureInfo.GetCultureInfo((int)(layout & 0xffff)).NativeName)); }
        catch (CultureNotFoundException) { Emit(new("keyboardLayout", "Idioma del teclado", "Distribución de teclado cambiada")); }
    }
    private static long ReadLayout() { uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _); return GetKeyboardLayout(thread).ToInt64(); }
    private void BindVolume()
    {
        lock (_audioLock)
        {
            if (_disposed || _enumerator is null) return;
            if (!Wants("volume") && !Wants("audioDevice")) { BindInput(); return; }
            if (_output is not null) { _output.AudioEndpointVolume.OnVolumeNotification -= VolumeChanged; _output.Dispose(); _output = null; }
            try { _output = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); if (Wants("volume")) _output.AudioEndpointVolume.OnVolumeNotification += VolumeChanged; }
            catch (Exception) { }
            BindInput();
        }
    }
    private void VolumeChanged(AudioVolumeNotificationData value)
    {
        int level = (int)Math.Round(value.MasterVolume * 100);
        Emit(new("volume", value.Muted ? "Sonido silenciado" : "Volumen", value.Muted ? "Salida de audio en silencio" : $"{level}%", value.Muted ? 0 : level));
    }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
    {
        if (role != Role.Multimedia) return;
        if (flow == DataFlow.Capture) { if (Wants("microphone")) _ = Task.Run(BindInput); return; }
        if (flow != DataFlow.Render || !Wants("volume") && !Wants("audioDevice")) return;
        _ = Task.Run(() =>
        {
            try
            {
                BindVolume();
                lock (_audioLock) if (!_disposed && _output is not null) EndpointNotice(id, _output.FriendlyName);
            }
            catch (Exception) { }
        });
    }
    private void EndpointNotice(string id, string name)
    {
        if (_lastEndpoint == id && DateTime.UtcNow - _lastEndpointAt < TimeSpan.FromSeconds(3)) return;
        _lastEndpoint = id; _lastEndpointAt = DateTime.UtcNow;
        Emit(new("audioDevice", "Salida de audio disponible", name));
    }
    public void OnDeviceStateChanged(string id, DeviceState state)
    {
        if (state != DeviceState.Active || _disposed || !Wants("audioDevice")) return;
        _ = Task.Run(() =>
        {
            try
            {
                lock (_audioLock)
                {
                    if (_disposed || _enumerator is null) return;
                    using var device = _enumerator.GetDevice(id);
                    if (device.DataFlow == DataFlow.Render) EndpointNotice(id, device.FriendlyName);
                }
            }
            catch (Exception) { }
        });
    }
    public void OnDeviceAdded(string id) => OnDeviceStateChanged(id, DeviceState.Active);
    public void OnDeviceRemoved(string id) { }
    public void OnPropertyValueChanged(string id, PropertyKey key) { }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
        if (_disposed) return;
        _disposed = true; _keys?.Dispose(); _keys = null;
        StopDeviceNotices(); StopPowerNotices();
        try { NetworkInformation.NetworkStatusChanged -= NetworkChanged; } catch (Exception) { }
        try { PowerManager.PowerSupplyStatusChanged -= PowerChanged; } catch (Exception) { }
        if (_brightness is not null) { try { _brightness.Stop(); } catch (Exception) { } _brightness.Dispose(); _brightness = null; }
        lock (_audioLock)
        {
            if (_input is not null) { _input.AudioEndpointVolume.OnVolumeNotification -= InputVolumeChanged; _input.Dispose(); _input = null; }
            if (_output is not null) { _output.AudioEndpointVolume.OnVolumeNotification -= VolumeChanged; _output.Dispose(); _output = null; }
            if (_enumerator is not null) { try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (Exception) { } _enumerator.Dispose(); _enumerator = null; }
        }
        }
    }
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);

    private void BindInput()
    {
        lock (_audioLock)
        {
            if (_disposed || !Wants("microphone") || _enumerator is null) return;
            if (_input is not null) { _input.AudioEndpointVolume.OnVolumeNotification -= InputVolumeChanged; _input.Dispose(); _input = null; }
            try
            {
                _input = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                _inputMuted = _input.AudioEndpointVolume.Mute; _input.AudioEndpointVolume.OnVolumeNotification += InputVolumeChanged;
            }
            catch (Exception) { }
        }
    }
    private void InputVolumeChanged(AudioVolumeNotificationData value)
    {
        lock (_audioLock)
        {
            if (_disposed || value.Muted == _inputMuted) return;
            _inputMuted = value.Muted;
        }
        Emit(new("microphone", value.Muted ? "Micrófono silenciado" : "Micrófono activado", "Estado de la entrada de audio"));
    }
}
