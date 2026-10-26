using System.Runtime.InteropServices;

namespace Nokto.Platform.Windows.Display;

// Consulta el modo ACTIVO de Windows en el monitor que contiene la isla.
public static class DisplayRefreshRateDetector
{
    private const int CurrentSettings = -1;
    private const uint NearestMonitor = 2;
    public const int FallbackHz = 60;

    public static int GetPrimaryRefreshRate() => Read(null);

    public static int GetRefreshRateAt(int x, int y)
    {
        try
        {
            var handle = MonitorFromPoint(new Point(x, y), NearestMonitor);
            var info = new MonitorInfoEx { DeviceName = string.Empty };
            info.Size = (uint)Marshal.SizeOf<MonitorInfoEx>();
            if (handle != IntPtr.Zero && GetMonitorInfo(handle, ref info) && !string.IsNullOrWhiteSpace(info.DeviceName))
                return Read(info.DeviceName);
        }
        catch (Exception) { }
        return GetPrimaryRefreshRate();
    }

    public static int Normalize(int raw)
    {
        if (raw <= 1 || raw > 500) return FallbackHz;
        foreach (int value in new[] { 60, 75, 90, 100, 120, 144, 165, 180, 240, 360 })
            if (Math.Abs(raw - value) <= 1) return value;
        return raw;
    }

    private static int Read(string? name)
    {
        try
        {
            var mode = new DevMode { DeviceName = string.Empty, FormName = string.Empty };
            mode.Size = (ushort)Marshal.SizeOf<DevMode>();
            if (EnumDisplaySettings(name, CurrentSettings, ref mode))
                return Normalize((int)mode.DisplayFrequency);
        }
        catch (Exception) { }
        return FallbackHz;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
        public Point(int x, int y) { X = x; Y = y; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2;
        public uint PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? name, int mode, ref DevMode devMode);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
