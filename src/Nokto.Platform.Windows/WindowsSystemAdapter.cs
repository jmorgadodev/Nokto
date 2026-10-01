using System.ComponentModel;
using System.Runtime.InteropServices;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Audio;
using Nokto.Platform.Windows.Interop;
using Nokto.Platform.Windows.KeepAlive;
using Nokto.Platform.Windows.Metrics;

namespace Nokto.Platform.Windows;

/// <summary>
/// Adaptador de sistema para Windows 10/11 utilizando llamadas nativas Win32 (P/Invoke) y WASAPI.
/// Totalmente libre de dependencias pesadas y optimizado para consumo mínimo de memoria (<25 MB RAM).
/// </summary>
public sealed class WindowsSystemAdapter : ISystemAdapter
{
    private readonly WasapiAudioController _audioController;
    private readonly KeepAliveEngine _keepAliveEngine;
    private readonly PassiveMetricsCollector _metricsCollector;
    private bool _disposed;

    public WindowsSystemAdapter()
    {
        _audioController = new WasapiAudioController();
        _keepAliveEngine = new KeepAliveEngine();
        _metricsCollector = new PassiveMetricsCollector();
    }

    /// <inheritdoc />
    public bool IsDryRunMode { get; set; }

    /// <inheritdoc />
    public Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken cancellationToken = default)
    {
        if (IsDryRunMode)
        {
            switch (action)
            {
                case PowerAction.Shutdown:
                case PowerAction.Restart:
                case PowerAction.Sleep:
                case PowerAction.Hibernate:
                    Console.WriteLine($"[DRY-RUN] Acción de energía simulada con éxito: {action} (Forzado: {force})");
                    return Task.CompletedTask;
            }
        }

        switch (action)
        {
            case PowerAction.Shutdown:
                ExecuteShutdown(force, reboot: false);
                break;

            case PowerAction.Restart:
                ExecuteShutdown(force, reboot: true);
                break;

            case PowerAction.Sleep:
                ExecuteSuspend(hibernate: false, force: force);
                break;

            case PowerAction.Hibernate:
                ExecuteSuspend(hibernate: true, force: force);
                break;

            case PowerAction.LockStation:
                ExecuteLockStation();
                break;

            case PowerAction.Logoff:
                ExecuteLogoff(force);
                break;

            case PowerAction.TurnOffMonitors:
                return SetDisplayPowerAsync(turnOn: false, cancellationToken);

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Acción de energía no soportada.");
        }

        return Task.CompletedTask;
    }

    private static void ExecuteShutdown(bool force, bool reboot)
    {
        EnableShutdownPrivilege();

        var flags = (reboot ? NativeConstants.ExitWindowsFlags.EWX_REBOOT : NativeConstants.ExitWindowsFlags.EWX_SHUTDOWN | NativeConstants.ExitWindowsFlags.EWX_POWEROFF);
        if (force)
        {
            flags |= NativeConstants.ExitWindowsFlags.EWX_FORCEIFHUNG;
        }

        bool result = NativeMethods.ExitWindowsEx(flags, NativeConstants.SHTDN_REASON_FLAG_PLANNED);
        if (!result)
        {
            // Fallback con InitiateSystemShutdownEx
            NativeMethods.InitiateSystemShutdownEx(
                null,
                null,
                0,
                force,
                reboot,
                NativeConstants.SHTDN_REASON_FLAG_PLANNED);
        }
    }

    private static void ExecuteSuspend(bool hibernate, bool force)
    {
        EnableShutdownPrivilege();
        bool success = NativeMethods.SetSuspendState(hibernate, force, false);
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Fallo al cambiar estado de suspensión de Windows.");
        }
    }

    private static void ExecuteLockStation()
    {
        bool success = NativeMethods.LockWorkStation();
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Fallo al bloquear la estación de trabajo.");
        }
    }

    private static void ExecuteLogoff(bool force)
    {
        var flags = NativeConstants.ExitWindowsFlags.EWX_LOGOFF;
        if (force)
        {
            flags |= NativeConstants.ExitWindowsFlags.EWX_FORCE;
        }
        NativeMethods.ExitWindowsEx(flags, NativeConstants.SHTDN_REASON_FLAG_PLANNED);
    }

    private static bool EnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeConstants.TOKEN_ADJUST_PRIVILEGES | NativeConstants.TOKEN_QUERY,
                out IntPtr hToken))
        {
            return false;
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, NativeConstants.SE_SHUTDOWN_NAME, out LUID luid))
            {
                return false;
            }

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = NativeConstants.SE_PRIVILEGE_ENABLED
                }
            };

            return NativeMethods.AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.CloseHandle(hToken);
        }
    }

    /// <inheritdoc />
    public Task SetDisplayPowerAsync(bool turnOn, CancellationToken cancellationToken = default)
    {
        if (!turnOn)
        {
            // Envío de señal SC_MONITORPOWER para apagar monitores
            NativeMethods.SendMessage(
                NativeConstants.HWND_BROADCAST,
                NativeConstants.WM_SYSCOMMAND,
                (IntPtr)NativeConstants.SC_MONITORPOWER,
                (IntPtr)NativeConstants.MONITOR_OFF);
        }
        else
        {
            // Reactivación de pantallas
            NativeMethods.SendMessage(
                NativeConstants.HWND_BROADCAST,
                NativeConstants.WM_SYSCOMMAND,
                (IntPtr)NativeConstants.SC_MONITORPOWER,
                (IntPtr)NativeConstants.MONITOR_ON);

            // Genera micro-movimiento de ratón para despertar paneles resistentes
            KeepAliveEngine.SendMouseJitter(1);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default)
    {
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
        _audioController.SetMasterVolume(volume);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
    {
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
        int x = NativeMethods.GetSystemMetrics(NativeConstants.SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(NativeConstants.SM_YVIRTUALSCREEN);
        int cx = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXVIRTUALSCREEN);
        int cy = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYVIRTUALSCREEN);

        if (cx <= 0 || cy <= 0)
        {
            cx = 1920;
            cy = 1080;
        }

        IntPtr hDesktopDC = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr hMemDC = NativeMethods.CreateCompatibleDC(hDesktopDC);
        IntPtr hBitmap = NativeMethods.CreateCompatibleBitmap(hDesktopDC, cx, cy);
        IntPtr hOldBitmap = NativeMethods.SelectObject(hMemDC, hBitmap);

        try
        {
            NativeMethods.BitBlt(hMemDC, 0, 0, cx, cy, hDesktopDC, x, y, NativeMethods.SRCCOPY);

            // Generar cabecera BMP en memoria de manera nativa sin dependencias
            byte[] bmpBytes = CreateBmpBytes(hDesktopDC, hBitmap, cx, cy);
            return Task.FromResult(bmpBytes);
        }
        finally
        {
            NativeMethods.SelectObject(hMemDC, hOldBitmap);
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(hMemDC);
            NativeMethods.ReleaseDC(IntPtr.Zero, hDesktopDC);
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _audioController.Dispose();
            _disposed = true;
        }
    }
}
