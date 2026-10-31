using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Audio;
using Nokto.Platform.Windows.Hardware;
using Nokto.Platform.Windows.Interop;
using Nokto.Platform.Windows.KeepAlive;
using Nokto.Platform.Windows.Metrics;

namespace Nokto.Platform.Windows;

/// <summary>
/// Adaptador de sistema para Windows 10/11 utilizando llamadas nativas Win32 (P/Invoke) y WASAPI.
/// Las acciones mutables respetan el modo de simulación; la energía tiene un backend verificable por separado.
/// </summary>
public sealed class WindowsSystemAdapter : ISystemAdapter
{
    private readonly WasapiAudioController _audioController;
    private readonly KeepAliveEngine _keepAliveEngine;
    private readonly PassiveMetricsCollector _metricsCollector;
    private readonly HardwareProfile _hardwareProfile;
    private bool _disposed;

    public WindowsSystemAdapter()
    {
        _audioController = new WasapiAudioController();
        _keepAliveEngine = new KeepAliveEngine();
        _metricsCollector = new PassiveMetricsCollector();
        _hardwareProfile = HardwareProfileService.Capture(_metricsCollector.GpuAdapterName);
    }

    public HardwareProfile GetHardwareProfile() => _hardwareProfile;

    public AudioDeviceProfile GetAudioDevices() => _audioController.GetDefaultAudioProfile();
    public IReadOnlyList<AudioEndpointInfo> GetOutputAudioDevices() => _audioController.GetOutputAudioDevices();
    public IReadOnlyList<AudioEndpointInfo> GetInputAudioDevices() => _audioController.GetInputAudioDevices();
    public bool? GetOutputMute() => _audioController.GetOutputMute();
    public bool? GetInputMute() => _audioController.GetInputMute();
    public bool ToggleOutputMute() => IsDryRunMode ? !_audioController.GetOutputMute().GetValueOrDefault() : _audioController.ToggleOutputMute();
    public bool ToggleInputMute() => IsDryRunMode ? !_audioController.GetInputMute().GetValueOrDefault() : _audioController.ToggleInputMute();
    public bool SetInputMute(bool mute) => IsDryRunMode || AudioDeviceProfileService.SetMute(true, mute);
    public bool IsAppRunning(string executablePath) => Applications.InstalledAppsService.IsAppRunning(executablePath);

    public Task LaunchAppAsync(LaunchAppOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDryRunMode) return Task.CompletedTask;
        if (!File.Exists(options.ExecutablePath)) throw new FileNotFoundException("No se encuentra la aplicación seleccionada.", options.ExecutablePath);
        var start = new ProcessStartInfo(options.ExecutablePath, options.Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = options.WorkingDirectory ?? Path.GetDirectoryName(options.ExecutablePath) ?? "",
            WindowStyle = options.LaunchMode switch
            {
                AppLaunchMode.Maximized => ProcessWindowStyle.Maximized,
                AppLaunchMode.Minimized => ProcessWindowStyle.Minimized,
                _ => ProcessWindowStyle.Normal
            }
        };
        if (Path.GetExtension(options.ExecutablePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            start.Arguments = "-NoProfile -File \"" + options.ExecutablePath + "\" " + options.Arguments;
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows no pudo abrir la aplicación.");
        return Task.CompletedTask;
    }
    public void CloseForegroundApplication()
    {
        if (IsDryRunMode) return;
        IntPtr window = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (window != IntPtr.Zero && processId != (uint)Environment.ProcessId)
            NativeMethods.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
    }
    public IReadOnlyList<ApplicationWindow> GetApplicationWindows() => Applications.ApplicationWindowService.GetWindows();
    public Task CloseApplicationsAsync(IReadOnlyList<ApplicationCloseTarget> targets, bool foregroundAtExecution,
        CancellationToken cancellationToken = default) => IsDryRunMode ? Task.CompletedTask :
        Applications.ApplicationWindowService.CloseAsync(targets, foregroundAtExecution, cancellationToken);
    public bool SetDefaultAudioDevice(string deviceId, bool input) => IsDryRunMode || _audioController.SetDefaultAudioDevice(deviceId, input);

    /// <inheritdoc />
    public bool IsDryRunMode { get; set; }

    /// <inheritdoc />
    private readonly Power.WindowsPowerController _power = new(new Power.WindowsPowerBackend());

    public Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken cancellationToken = default)
        => _power.ExecuteAsync(action, force, IsDryRunMode, cancellationToken);

    public Task SetDisplayPowerAsync(bool turnOn, CancellationToken cancellationToken = default)
        => _power.SetDisplayAsync(turnOn, IsDryRunMode, cancellationToken);

    /// <inheritdoc />
    public Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDryRunMode) return Task.CompletedTask;
        return _audioController.FadeVolumeAsync(targetVolume, duration, cancellationToken);
    }

    /// <inheritdoc />
    public Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_audioController.GetMasterVolume());
    }

    /// <inheritdoc />
    public Task SetMasterVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDryRunMode) return Task.CompletedTask;
        _audioController.SetMasterVolume(volume);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDryRunMode) return Task.CompletedTask;
        _audioController.SetMute(mute);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> GetMuteAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_audioController.GetMute());
    }

    /// <inheritdoc />
    public void SimulateKeepAlivePulse(KeepAliveMode mode)
    {
        if (IsDryRunMode) return;
        KeepAliveEngine.EmitPulse(mode);
    }

    /// <inheritdoc />
    public Task RunKeepAliveLoopAsync(
        KeepAliveMode mode,
        int jitterMinSeconds,
        int jitterMaxSeconds,
        Action<string>? onActivityLogged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDryRunMode) return Task.Delay(Timeout.Infinite, cancellationToken);
        return _keepAliveEngine.RunLoopAsync(mode, jitterMinSeconds, jitterMaxSeconds, onActivityLogged, cancellationToken);
    }

    /// <inheritdoc />
    public float GetMasterPeakValue()
    {
        return _audioController.GetPeakValue();
    }

    /// <inheritdoc />
    public BatteryStatus GetBatteryStatus()
    {
        if (NativeMethods.GetSystemPowerStatus(out var status))
        {
            // ACLineStatus: 0 = Offline, 1 = Online, 255 = Unknown
            bool isOnAc = status.ACLineStatus == 1 || status.ACLineStatus == 255;
            // BatteryFlag: 128 = No system battery, 255 = Unknown
            bool hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255;
            bool isCharging = (status.BatteryFlag & 8) != 0;
            int percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : -1;
            int secondsRemaining = status.BatteryLifeTime >= 0 ? status.BatteryLifeTime : -1;

            return new BatteryStatus
            {
                HasBattery = hasBattery,
                IsCharging = isCharging,
                IsOnAcPower = isOnAc,
                BatteryLifePercent = percent,
                BatteryLifeSecondsRemaining = secondsRemaining
            };
        }

        return new BatteryStatus
        {
            HasBattery = false,
            IsCharging = false,
            IsOnAcPower = true,
            BatteryLifePercent = -1,
            BatteryLifeSecondsRemaining = -1
        };
    }

    /// <inheritdoc />
    public SystemMetrics GetCurrentMetrics()
    {
        var metrics = _metricsCollector.CollectMetrics();
        return metrics with
        {
            AudioPeakLevel = GetMasterPeakValue(),
            Battery = GetBatteryStatus()
        };
    }

    /// <inheritdoc />
    public Task<byte[]> CaptureScreenAsync(bool stampMetadata = false, string? label = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDryRunMode) return Task.FromResult(Array.Empty<byte>());
        int x = NativeMethods.GetSystemMetrics(NativeConstants.SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(NativeConstants.SM_YVIRTUALSCREEN);
        int cx = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXVIRTUALSCREEN);
        int cy = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYVIRTUALSCREEN);

        if (cx <= 0 || cy <= 0)
        {
            cx = 1920;
            cy = 1080;
        }

        IntPtr hDesktopDC = IntPtr.Zero;
        IntPtr hMemDC = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOldBitmap = IntPtr.Zero;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            hDesktopDC = NativeMethods.GetDC(IntPtr.Zero);
            if (hDesktopDC == IntPtr.Zero) return Task.FromResult(Array.Empty<byte>());
            hMemDC = NativeMethods.CreateCompatibleDC(hDesktopDC);
            if (hMemDC == IntPtr.Zero) return Task.FromResult(Array.Empty<byte>());
            hBitmap = NativeMethods.CreateCompatibleBitmap(hDesktopDC, cx, cy);
            if (hBitmap == IntPtr.Zero) return Task.FromResult(Array.Empty<byte>());
            hOldBitmap = NativeMethods.SelectObject(hMemDC, hBitmap);
            if (hOldBitmap == IntPtr.Zero || hOldBitmap == new IntPtr(-1))
            {
                hOldBitmap = IntPtr.Zero;
                return Task.FromResult(Array.Empty<byte>());
            }
            NativeMethods.BitBlt(hMemDC, 0, 0, cx, cy, hDesktopDC, x, y, NativeMethods.SRCCOPY);

            // Generar cabecera BMP en memoria de manera nativa sin dependencias
            byte[] bmpBytes = CreateBmpBytes(hDesktopDC, hBitmap, cx, cy);
            return Task.FromResult(bmpBytes);
        }
        finally
        {
            if (hMemDC != IntPtr.Zero && hOldBitmap != IntPtr.Zero)
                NativeMethods.SelectObject(hMemDC, hOldBitmap);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (hMemDC != IntPtr.Zero) NativeMethods.DeleteDC(hMemDC);
            if (hDesktopDC != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, hDesktopDC);
        }
    }

    private static byte[] CreateBmpBytes(IntPtr hdc, IntPtr hBitmap, int width, int height)
    {
        int rowSize = ((width * 32 + 31) / 32) * 4;
        int imageSize = rowSize * height;
        int fileSize = 54 + imageSize;

        byte[] buffer = new byte[fileSize];
        using var ms = new MemoryStream(buffer);
        using var bw = new BinaryWriter(ms);

        // BITMAPFILEHEADER (14 bytes)
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((short)0); // reserved1
        bw.Write((short)0); // reserved2
        bw.Write(54); // offBits

        // BITMAPINFOHEADER (40 bytes)
        bw.Write(40); // biSize
        bw.Write(width);
        bw.Write(height); // bottom-up
        bw.Write((short)1); // biPlanes
        bw.Write((short)32); // biBitCount
        bw.Write(0); // biCompression = BI_RGB
        bw.Write(imageSize);
        bw.Write(0); // biXPelsPerMeter
        bw.Write(0); // biYPelsPerMeter
        bw.Write(0); // biClrUsed
        bw.Write(0); // biClrImportant

        // Usamos GetDIBits para extraer los píxeles reales
        GetDiBitsNative(hdc, hBitmap, width, height, buffer, 54);

        return buffer;
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        [Out] byte[] lpvBits,
        ref BITMAPINFO lpbi,
        uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private static void GetDiBitsNative(IntPtr hdc, IntPtr hBitmap, int width, int height, byte[] targetBuffer, int offset)
    {
        var bi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        byte[] rawPixels = new byte[width * height * 4];
        GetDIBits(hdc, hBitmap, 0, (uint)height, rawPixels, ref bi, 0 /* DIB_RGB_COLORS */);
        Buffer.BlockCopy(rawPixels, 0, targetBuffer, offset, rawPixels.Length);
    }

    /// <inheritdoc />
    public void SendMediaControl(bool pauseOnly = true)
    {
        // WM_APPCOMMAND: discrete pause (47) / play (46), forwarded by DefWindowProc to the shell.
        // https://learn.microsoft.com/windows/win32/inputdev/wm-appcommand
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;
        if (!NativeMethods.PostMessage(foreground, 0x0319, foreground, new IntPtr((pauseOnly ? 47 : 46) << 16)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo enviar el comando multimedia.");
    }
    public void Dispose()
    {
        if (!_disposed)
        {
            _audioController.Dispose();
            _metricsCollector.Dispose();
            _disposed = true;
        }
    }
}
