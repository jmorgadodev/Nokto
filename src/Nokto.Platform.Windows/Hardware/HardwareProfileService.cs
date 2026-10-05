using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.Hardware;

/// <summary>Passive registry/Win32 inspection; discovers hybrid graphics through DXGI.</summary>
public static class HardwareProfileService
{
    public static HardwareProfile Capture(string graphicsAdapterName)
    {
        var displayMode = new DEVMODE { Size = (ushort)Marshal.SizeOf<DEVMODE>() };
        int refreshRate = NativeMethods.EnumDisplaySettings(null, -1, ref displayMode)
            ? (int)displayMode.DisplayFrequency : 0;
        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        double memoryGb = NativeMethods.GlobalMemoryStatusEx(ref memory)
            ? memory.ullTotalPhys / (1024.0 * 1024 * 1024) : 0;
        // Installed capacity excludes the reduction caused by hardware-reserved memory.
        if (NativeMethods.GetPhysicallyInstalledSystemMemory(out ulong installedKb) && installedKb > 0)
            memoryGb = installedKb / (1024.0 * 1024);

        return new HardwareProfile
        {
            OperatingSystemName = ReadOperatingSystemName(),
            ProcessorName = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "No disponible",
            InstalledMemoryGigabytes = Math.Round(memoryGb, 1),
            GraphicsAdapterName = GraphicsAdapterDiscovery.FormatNames(GraphicsAdapterDiscovery.Enumerate(), graphicsAdapterName),
            MonitorCount = NativeMethods.GetSystemMetrics(NativeConstants.SM_CMONITORS),
            PrimaryScreenWidth = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN),
            PrimaryScreenHeight = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN),
            PrimaryScreenRefreshRateHz = refreshRate
        };
    }

    private static string ReadOperatingSystemName()
    {
        const string versionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string? product = ReadRegistryString(versionKey, "ProductName");
        int build = int.TryParse(ReadRegistryString(versionKey, "CurrentBuildNumber"), out int registeredBuild)
            ? registeredBuild : Environment.OSVersion.Version.Build;
        // Some Windows 11 installations retain "Windows 10" in ProductName.
        if (build >= 22000 && product?.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) == true)
            product = "Windows 11" + product[10..];
        product ??= build >= 22000 ? "Windows 11" : "Windows 10";
        string? revision = ReadRegistryString(versionKey, "UBR");
        string buildText = revision == null ? build.ToString() : $"{build}.{revision}";
        return $"{product} {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} (Build {buildText})";
    }

    private static string? ReadRegistryString(string path, string name)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(path, writable: false);
            string? value = key?.GetValue(name)?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
