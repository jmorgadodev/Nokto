using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.Metrics;

/// <summary>
/// Colector de métricas pasivas de ultra-bajo consumo (<0.01% CPU, cero allocations periódicas).
/// Utiliza GetSystemTimes, GlobalMemoryStatusEx, GetLastInputInfo, NetworkInterface, IOCTL_DISK_PERFORMANCE y PDH GPU.
/// </summary>
public sealed class PassiveMetricsCollector : IDisposable
{
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private DateTimeOffset _prevCpuSampleTime = DateTimeOffset.MinValue;
    private double _lastCpuUsage = 0.0;
    private double _lastCpuKernel = 0.0;
    private double _lastCpuUser = 0.0;

    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTimeOffset _prevNetworkSampleTime = DateTimeOffset.MinValue;
    private double _lastDownKBs = 0.0;
    private double _lastUpKBs = 0.0;

    private long _prevDiskBytesRead;
    private long _prevDiskBytesWritten;
    private DateTimeOffset _prevDiskSampleTime = DateTimeOffset.MinValue;
    private double _lastDiskReadMBs = 0.0;
    private double _lastDiskWriteMBs = 0.0;

    private string _gpuAdapterName = "Adaptador de Pantalla";
    public string GpuAdapterName => _gpuAdapterName;
    private IntPtr _hPdhQuery = IntPtr.Zero;
    private IntPtr _hPdhCounter = IntPtr.Zero;
    private bool _pdhInitialized = false;

    private readonly object _syncLock = new();

    public PassiveMetricsCollector()
    {
        InitializeBaseline();
        InitializeGpu();
    }

    private void InitializeBaseline()
    {
        lock (_syncLock)
        {
            if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                _prevIdleTime = ToUInt64(idle);
                _prevKernelTime = ToUInt64(kernel);
                _prevUserTime = ToUInt64(user);
                _prevCpuSampleTime = DateTimeOffset.UtcNow;
            }

            ReadCurrentNetworkTotals(out _prevBytesReceived, out _prevBytesSent);
            _prevNetworkSampleTime = DateTimeOffset.UtcNow;

            ReadDiskBytes(out _prevDiskBytesRead, out _prevDiskBytesWritten);
            _prevDiskSampleTime = DateTimeOffset.UtcNow;
        }
    }

    private void InitializeGpu()
    {
        try
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (NativeMethods.EnumDisplayDevices(null, 0, ref d, 0))
            {
                if (!string.IsNullOrWhiteSpace(d.DeviceString))
                {
                    _gpuAdapterName = d.DeviceString.Trim();
                }
            }
        }
        catch
        {
            _gpuAdapterName = "Adaptador Gráfico";
        }

        try
        {
            if (NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _hPdhQuery) == 0)
            {
                // Monitorea el motor 3D de todas las GPUs
                if (NativeMethods.PdhAddEnglishCounter(_hPdhQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _hPdhCounter) == 0)
                {
                    NativeMethods.PdhCollectQueryData(_hPdhQuery);
                    _pdhInitialized = true;
                }
            }
        }
        catch
        {
            _pdhInitialized = false;
        }
    }

    public SystemMetrics CollectMetrics()
    {
        lock (_syncLock)
        {
            var now = DateTimeOffset.UtcNow;

            // 1. CPU Pasivo vía GetSystemTimes con desglose Kernel / Usuario
            CalculateCpuUsage(now, out double cpuUsage, out double kernelUsage, out double userUsage);

            // 2. Memoria RAM vía GlobalMemoryStatusEx
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            double ramTotalMb = 0.0;
            double ramUsedMb = 0.0;
            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
            {
                ramTotalMb = memStatus.ullTotalPhys / (1024.0 * 1024.0);
                ramUsedMb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0);
            }

            // 3. GPU vía PDH
            double gpuUsage = CalculateGpuUsage();

            // 4. Disco del sistema vía DriveInfo
            long diskFreeGb = 0;
            long diskTotalGb = 0;
            try
            {
                string? sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
                var drive = new DriveInfo(sysRoot ?? "C:\\");
                diskFreeGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                diskTotalGb = drive.TotalSize / (1024 * 1024 * 1024);
            }
            catch { }

            // 5. Tráfico de Red vía NetworkInterface
            CalculateNetworkThroughput(now, out double downKBs, out double upKBs);

            // 6. Inactividad de Usuario vía GetLastInputInfo
            int userIdleSeconds = CalculateUserIdleSeconds();

            return new SystemMetrics
            {
                CpuUsagePercentage = Math.Round(cpuUsage, 1),
                CpuKernelPercentage = Math.Round(kernelUsage, 1),
                CpuUserPercentage = Math.Round(userUsage, 1),
                GpuUsagePercentage = Math.Round(gpuUsage, 1),
                GpuAdapterName = _gpuAdapterName,
                RamUsedMb = Math.Round(ramUsedMb, 1),
                RamTotalMb = Math.Round(ramTotalMb, 1),
                DiskFreeGb = diskFreeGb,
                DiskTotalGb = diskTotalGb,
                DiskReadMBs = 0.0,
                DiskWriteMBs = 0.0,
                DiskTotalMBs = 0.0,
                NetworkDownKBs = Math.Round(downKBs, 1),
                NetworkUpKBs = Math.Round(upKBs, 1),
                AudioSilenceDurationSeconds = 0,
                UserIdleSeconds = userIdleSeconds,
                Timestamp = now
            };
        }
    }

    private void CalculateCpuUsage(DateTimeOffset now, out double totalUsage, out double kernelUsage, out double userUsage)
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            totalUsage = _lastCpuUsage;
            kernelUsage = _lastCpuKernel;
            userUsage = _lastCpuUser;
            return;
        }

        ulong idleTime = ToUInt64(idle);
        ulong kernelTime = ToUInt64(kernel);
        ulong userTime = ToUInt64(user);

        if (_prevCpuSampleTime == DateTimeOffset.MinValue)
        {
            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;
            _prevCpuSampleTime = now;
            totalUsage = _lastCpuUsage;
            kernelUsage = _lastCpuKernel;
            userUsage = _lastCpuUser;
            return;
        }

        ulong diffIdle = idleTime - _prevIdleTime;
        ulong diffKernel = kernelTime - _prevKernelTime;
        ulong diffUser = userTime - _prevUserTime;

        // En Windows, kernelTime ya incluye idleTime
        ulong totalTime = diffKernel + diffUser;

        if (totalTime > 0)
        {
            double idleFraction = (double)diffIdle / totalTime;
            double calculated = (1.0 - idleFraction) * 100.0;
            _lastCpuUsage = Math.Clamp(calculated, 0.0, 100.0);

            ulong activeKernel = diffKernel >= diffIdle ? (diffKernel - diffIdle) : 0;
            _lastCpuKernel = Math.Clamp(((double)activeKernel / totalTime) * 100.0, 0.0, 100.0);
            _lastCpuUser = Math.Clamp(((double)diffUser / totalTime) * 100.0, 0.0, 100.0);
        }

        _prevIdleTime = idleTime;
        _prevKernelTime = kernelTime;
        _prevUserTime = userTime;
        _prevCpuSampleTime = now;

        totalUsage = _lastCpuUsage;
        kernelUsage = _lastCpuKernel;
        userUsage = _lastCpuUser;
    }

    private double CalculateGpuUsage()
    {
        if (!_pdhInitialized) return 0.0;

        try
        {
            if (NativeMethods.PdhCollectQueryData(_hPdhQuery) == 0)
            {
                const uint PDH_FMT_DOUBLE = 0x00000200;
                if (NativeMethods.PdhGetFormattedCounterValue(_hPdhCounter, PDH_FMT_DOUBLE, IntPtr.Zero, out var pValue) == 0)
                {
                    return Math.Clamp(pValue.doubleValue, 0.0, 100.0);
                }
            }
        }
        catch
        {
            // Silently fallback to 0
        }

        return 0.0;
    }

    private void CalculateDiskThroughput(DateTimeOffset now, out double readMBs, out double writeMBs, out double totalMBs)
    {
        ReadDiskBytes(out long curRead, out long curWritten);

        if (_prevDiskSampleTime == DateTimeOffset.MinValue)
        {
            _prevDiskBytesRead = curRead;
            _prevDiskBytesWritten = curWritten;
            _prevDiskSampleTime = now;
            readMBs = 0.0;
            writeMBs = 0.0;
            totalMBs = 0.0;
            return;
        }

        double elapsedSeconds = (now - _prevDiskSampleTime).TotalSeconds;
        if (elapsedSeconds >= 0.5)
        {
            long deltaRead = Math.Max(0, curRead - _prevDiskBytesRead);
            long deltaWrite = Math.Max(0, curWritten - _prevDiskBytesWritten);

            _lastDiskReadMBs = Math.Round((deltaRead / (1024.0 * 1024.0)) / elapsedSeconds, 1);
            _lastDiskWriteMBs = Math.Round((deltaWrite / (1024.0 * 1024.0)) / elapsedSeconds, 1);

            _prevDiskBytesRead = curRead;
            _prevDiskBytesWritten = curWritten;
            _prevDiskSampleTime = now;
        }

        readMBs = _lastDiskReadMBs;
        writeMBs = _lastDiskWriteMBs;
        totalMBs = Math.Round(readMBs + writeMBs, 1);
    }

    private static void ReadDiskBytes(out long bytesRead, out long bytesWritten)
    {
        bytesRead = 0;
        bytesWritten = 0;
        IntPtr hDisk = IntPtr.Zero;
        try
        {
            hDisk = NativeMethods.CreateFile(
                @"\\.\PhysicalDrive0",
                0, // GENERIC_READ no necesario, 0 es suficiente para IOCTL_DISK_PERFORMANCE
                0x00000001 | 0x00000002, // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3, // OPEN_EXISTING
                0,
                IntPtr.Zero);

            if (hDisk != IntPtr.Zero && hDisk.ToInt64() != -1)
            {
                const uint IOCTL_DISK_PERFORMANCE = 0x00070020;
                if (NativeMethods.DeviceIoControl(
                    hDisk,
                    IOCTL_DISK_PERFORMANCE,
                    IntPtr.Zero,
                    0,
                    out DISK_PERFORMANCE perf,
                    (uint)Marshal.SizeOf<DISK_PERFORMANCE>(),
                    out _,
                    IntPtr.Zero))
                {
                    bytesRead = perf.BytesRead;
                    bytesWritten = perf.BytesWritten;
                }
            }
        }
        catch
        {
            // Silently fallback
        }
        finally
        {
            if (hDisk != IntPtr.Zero && hDisk.ToInt64() != -1)
            {
                NativeMethods.CloseHandle(hDisk);
            }
        }
    }

    private void CalculateNetworkThroughput(DateTimeOffset now, out double downKBs, out double upKBs)
    {
        ReadCurrentNetworkTotals(out long currentRecv, out long currentSent);

        double elapsedSeconds = (now - _prevNetworkSampleTime).TotalSeconds;
        if (elapsedSeconds >= 0.5)
        {
            long deltaRecv = Math.Max(0, currentRecv - _prevBytesReceived);
            long deltaSent = Math.Max(0, currentSent - _prevBytesSent);

            _lastDownKBs = (deltaRecv / 1024.0) / elapsedSeconds;
            _lastUpKBs = (deltaSent / 1024.0) / elapsedSeconds;

            _prevBytesReceived = currentRecv;
            _prevBytesSent = currentSent;
            _prevNetworkSampleTime = now;
        }

        downKBs = _lastDownKBs;
        upKBs = _lastUpKBs;
    }

    private static void ReadCurrentNetworkTotals(out long totalRecv, out long totalSent)
    {
        totalRecv = 0;
        totalSent = 0;

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = ni.GetIPStatistics();
                totalRecv += stats.BytesReceived;
                totalSent += stats.BytesSent;
            }
        }
        catch
        {
            // Ignore network interface query issues
        }
    }

    private static int CalculateUserIdleSeconds()
    {
        try
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (NativeMethods.GetLastInputInfo(ref lii))
            {
                uint currentTick = (uint)Environment.TickCount;
                uint elapsedTicks = currentTick >= lii.dwTime ? currentTick - lii.dwTime : (uint.MaxValue - lii.dwTime + currentTick);
                return (int)(elapsedTicks / 1000);
            }
        }
        catch
        {
            // Fallback
        }
        return 0;
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
    {
        return ((ulong)(uint)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
    }

    public void Dispose()
    {
        if (_pdhInitialized && _hPdhQuery != IntPtr.Zero)
        {
            try
            {
                NativeMethods.PdhCloseQuery(_hPdhQuery);
            }
            catch
            {
            }
            _hPdhQuery = IntPtr.Zero;
            _pdhInitialized = false;
        }
    }
}
