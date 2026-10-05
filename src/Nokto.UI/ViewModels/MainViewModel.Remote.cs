using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Remote;
using Nokto.UI.Remote;
using QRCoder;

namespace Nokto.UI.ViewModels;

public partial class MainViewModel
{
    private LocalRemoteServerService? _remoteServer;
    private readonly SemaphoreSlim _remoteLifecycleGate = new(1, 1);
    private bool _remoteControlsInitialized;
    [ObservableProperty] private bool _remoteControlEnabled;
    [ObservableProperty] private decimal _remoteControlPort = 5050;
    [ObservableProperty] private bool _remoteServerRunning;
    [ObservableProperty] private string _remoteServerStatusText = "Desactivado. Habilita el acceso para conectar tu teléfono.";
    [ObservableProperty] private Bitmap? _remoteQrCode;

    public string RemoteBaseUrl => $"http://{LocalIpAddress}:{RemoteControlPort:0}/";
    public string RemoteConnectionUrl => _remoteServer?.IsRunning == true ? _remoteServer.GetPairingUrl(LocalIpAddress) : "";

    private void InitializeRemoteControls()
    {
        RemoteControlEnabled = _config.LanServer.Enabled;
        RemoteControlPort = _config.LanServer.Port is >= 1024 and <= 65535 ? _config.LanServer.Port : 5050;
        _remoteServer = new(_systemAdapter, _workflowEngine, ct => RemotePreviewEncoder.CaptureAsync(_systemAdapter, ct))
        { AllowScreenPreview = _config.LanServer.AllowScreenPreview };
        _remoteControlsInitialized = true;
        if (RemoteControlEnabled) QueueRemoteConfiguration();
    }

    partial void OnRemoteControlEnabledChanged(bool value)
    {
        if (!_remoteControlsInitialized) return;
        _config.LanServer.Enabled = value;
        _persistence.SaveConfig(_config);
        QueueRemoteConfiguration();
    }

    partial void OnRemoteControlPortChanged(decimal value)
    {
        OnPropertyChanged(nameof(RemoteBaseUrl));
        if (!_remoteControlsInitialized) return;
        if (value is < 1024 or > 65535 || decimal.Truncate(value) != value)
        {
            RemoteServerStatusText = "Elige un puerto entero entre 1024 y 65535.";
            return;
        }
        _config.LanServer.Port = (int)value;
        _persistence.SaveConfig(_config);
        QueueRemoteConfiguration();
    }

    partial void OnLocalIpAddressChanged(string value)
    {
        OnPropertyChanged(nameof(RemoteBaseUrl));
        RefreshRemoteQr();
    }

    private void QueueRemoteConfiguration()
    {
        if (_disposalCts.IsCancellationRequested) return;
        _ = Task.Run(ApplyRemoteConfigurationAsync);
    }

    private async Task ApplyRemoteConfigurationAsync()
    {
        await _remoteLifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposalCts.IsCancellationRequested || _remoteServer is null) return;
            bool enabled = RemoteControlEnabled;
            int port = (int)RemoteControlPort;
            if (_remoteServer.IsRunning && (!enabled || _remoteServer.Port != port))
                await _remoteServer.StopAsync().ConfigureAwait(false);
            if (enabled && !_remoteServer.IsRunning)
                await _remoteServer.StartAsync(port, _disposalCts.Token).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposalCts.IsCancellationRequested) return;
                RemoteServerRunning = _remoteServer.IsRunning;
                RemoteServerStatusText = RemoteServerRunning
                    ? "Activo en la red local. Escanea el QR con el teléfono conectado a la misma red."
                    : "Desactivado. Habilita el acceso para conectar tu teléfono.";
                RefreshRemoteQr();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LAN startup: {ex.GetType().Name}");
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposalCts.IsCancellationRequested) return;
                RemoteServerRunning = false;
                RemoteServerStatusText = "No se pudo abrir el puerto local. Comprueba que esté libre e inténtalo de nuevo.";
                RefreshRemoteQr();
            });
        }
        finally { _remoteLifecycleGate.Release(); }
    }

    private void RefreshRemoteQr()
    {
        OnPropertyChanged(nameof(RemoteConnectionUrl));
        var previous = RemoteQrCode;
        if (_remoteServer?.IsRunning != true) RemoteQrCode = null;
        else
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(RemoteConnectionUrl, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            using var stream = new System.IO.MemoryStream(png.GetGraphic(4));
            RemoteQrCode = new Bitmap(stream);
        }
        previous?.Dispose();
    }

    [RelayCommand]
    private void RetryRemoteServer() => QueueRemoteConfiguration();

    [RelayCommand]
    private void OpenRemoteControl()
    {
        if (_remoteServer?.IsRunning != true) return;
        Process.Start(new ProcessStartInfo(RemoteConnectionUrl) { UseShellExecute = true });
    }

    private void DisposeRemoteControls()
    {
        _remoteControlsInitialized = false;
        _remoteLifecycleGate.Wait();
        try { _remoteServer?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        finally { _remoteLifecycleGate.Release(); }
        RemoteQrCode?.Dispose();
    }
}

