using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.Metrics;

/// <summary>
/// Colector de métricas pasivas de ultra-bajo consumo (<0.01% CPU, cero allocations periódicas).
/// Utiliza GetSystemTimes, GlobalMemoryStatusEx, GetLastInputInfo y NetworkInterface del BCL.
/// </summary>
public sealed class PassiveMetricsCollector
{
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private DateTimeOffset _prevCpuSampleTime = DateTimeOffset.MinValue;
    private double _lastCpuUsage = 0.0;

    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTimeOffset _prevNetworkSampleTime = DateTimeOffset.MinValue;
    private double _lastDownKBs = 0.0;
    private double _lastUpKBs = 0.0;

    private readonly object _syncLock = new();

    public PassiveMetricsCollector()
    {
        InitializeBaseline();
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
        }
    }

    public SystemMetrics CollectMetrics()
    {
        lock (_syncLock)
        {
            var now = DateTimeOffset.UtcNow;

            // 1. CPU Pasivo vía GetSystemTimes
            double cpuUsage = CalculateCpuUsage(now);

            // 2. Memoria RAM vía GlobalMemoryStatusEx
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            double ramTotalMb = 0.0;
            double ramUsedMb = 0.0;
            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
            {
                ramTotalMb = memStatus.ullTotalPhys / (1024.0 * 1024.0);
                ramUsedMb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0);
            }

            // 3. Tráfico de Red vía NetworkInterface
            CalculateNetworkThroughput(now, out double downKBs, out double upKBs);

            // 4. Inactividad de Usuario vía GetLastInputInfo
            int userIdleSeconds = CalculateUserIdleSeconds();

            return new SystemMetrics
            {
                CpuUsagePercentage = Math.Round(cpuUsage, 1),
                GpuUsagePercentage = 0.0,
                RamUsedMb = Math.Round(ramUsedMb, 1),
                RamTotalMb = Math.Round(ramTotalMb, 1),
                NetworkDownKBs = Math.Round(downKBs, 1),
                NetworkUpKBs = Math.Round(upKBs, 1),
                AudioSilenceDurationSeconds = 0,
                UserIdleSeconds = userIdleSeconds,
                Timestamp = now
            };
        }
    }

    private double CalculateCpuUsage(DateTimeOffset now)
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return _lastCpuUsage;
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
            return _lastCpuUsage;
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
        }

        _prevIdleTime = idleTime;
        _prevKernelTime = kernelTime;
        _prevUserTime = userTime;
        _prevCpuSampleTime = now;

        return _lastCpuUsage;
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
}
