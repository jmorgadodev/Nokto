using System.Management;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Nokto.Platform.Windows.Notifications;

public sealed partial class IslandSystemObserver
{
    private readonly List<BluetoothWatch> _bluetooth = [];
    private ManagementEventWatcher? _storage;
    private void StartDeviceNotices()
    {
        if (Wants("bluetooth"))
        {
            try { _bluetooth.Add(new BluetoothWatch(BluetoothDevice.GetDeviceSelectorFromPairingState(true), Emit)); } catch (Exception) { }
            try { _bluetooth.Add(new BluetoothWatch(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), Emit)); } catch (Exception) { }
        }
        if (Wants("storage")) try
        {
            _storage = new ManagementEventWatcher(@"root\CIMV2", "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 OR EventType = 3");
            _storage.EventArrived += StorageChanged; _storage.Start();
        }
        catch (Exception) { _storage?.Dispose(); _storage = null; }
    }
    private void StorageChanged(object sender, EventArrivedEventArgs args)
    {
        try
        {
            string name = args.NewEvent["DriveName"]?.ToString() ?? "";
            if (name.Length != 2 || !char.IsAsciiLetter(name[0]) || name[1] != ':') return;
            bool connected = Convert.ToInt32(args.NewEvent["EventType"]) == 2;
            Emit(new("storage", connected ? "Unidad conectada" : "Unidad retirada", name.ToUpperInvariant()));
        }
        catch (Exception) { }
    }
    private void StopDeviceNotices()
    {
        foreach (var watcher in _bluetooth) watcher.Dispose(); _bluetooth.Clear();
        if (_storage is not null)
        {
            try { _storage.Stop(); } catch (Exception) { }
            _storage.EventArrived -= StorageChanged; _storage.Dispose(); _storage = null;
        }
    }

    private sealed class BluetoothWatch : IDisposable
    {
        private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
        private readonly DeviceWatcher _watcher;
        private readonly IslandConnectionTransitions _state = new();
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private readonly Action<IslandSystemNotice> _emit;
        private readonly object _gate = new();
        private bool _ready, _closed;
        public BluetoothWatch(string selector, Action<IslandSystemNotice> emit)
        {
            _emit = emit;
            _watcher = DeviceInformation.CreateWatcher(selector, [ConnectedProperty], DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += Added; _watcher.Updated += Updated; _watcher.Removed += Removed; _watcher.EnumerationCompleted += Completed;
            try { _watcher.Start(); }
            catch { Dispose(); throw; }
        }
        private void Completed(DeviceWatcher sender, object args) { lock (_gate) { if (!_closed) _ready = true; } }
        private void Added(DeviceWatcher sender, DeviceInformation device)
        {
            lock (_gate)
            {
                if (_closed) return;
                _names[device.Id] = device.Name;
                bool connected = device.Properties.TryGetValue(ConnectedProperty, out var value) && value is true;
                Publish(_state.Observe(device.Id, connected, _ready), device.Name);
            }
        }
        private void Updated(DeviceWatcher sender, DeviceInformationUpdate change)
        {
            lock (_gate)
            {
                if (_closed || !change.Properties.TryGetValue(ConnectedProperty, out var value) || value is not bool connected) return;
                Publish(_state.Observe(change.Id, connected, _ready), _names.GetValueOrDefault(change.Id, "Dispositivo Bluetooth"));
            }
        }
        private void Removed(DeviceWatcher sender, DeviceInformationUpdate change)
        {
            lock (_gate)
            {
                if (_closed) return;
                Publish(_state.Remove(change.Id, _ready), _names.GetValueOrDefault(change.Id, "Dispositivo Bluetooth"));
                _names.Remove(change.Id);
            }
        }
        private void Publish(bool? connected, string name)
        {
            if (!connected.HasValue) return;
            _emit(new("bluetooth", connected.Value ? "Bluetooth conectado" : "Bluetooth desconectado",
                string.IsNullOrWhiteSpace(name) ? "Dispositivo Bluetooth" : name.Length > 120 ? name[..120] : name));
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed) return; _closed = true;
                _watcher.Added -= Added; _watcher.Updated -= Updated; _watcher.Removed -= Removed; _watcher.EnumerationCompleted -= Completed;
            }
            // Stop puede esperar callbacks; nunca retener el cerrojo de dichos callbacks.
            try { if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop(); } catch (Exception) { }
        }
    }
}

/// <summary>Estado efímero: la enumeración inicial solo establece una base, sin avisos históricos.</summary>
public sealed class IslandConnectionTransitions
{
    private readonly Dictionary<string, bool> _connections = new(StringComparer.Ordinal);
    public bool? Observe(string id, bool connected, bool announce)
    {
        bool known = _connections.TryGetValue(id, out var before); _connections[id] = connected;
        return announce && (known ? before != connected : connected) ? connected : null;
    }
    public bool? Remove(string id, bool announce) => _connections.Remove(id, out var connected) && connected && announce ? false : null;
}
