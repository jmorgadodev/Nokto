using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Nokto.Core.Abstractions;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.Applications;

/// <summary>Offline Start Menu catalog. COM and native icon handles stay on a background STA.</summary>
public sealed class InstalledAppsService : IInstalledAppsService
{
    private readonly string[] _roots;
    private readonly object _sync = new();
    private Task<IReadOnlyList<InstalledApplication>>? _scan;

    public InstalledAppsService(IEnumerable<string>? roots = null) => _roots = roots?.ToArray() ??
    [Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs)];

    public Task<IReadOnlyList<InstalledApplication>> ScanAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            // Cancellation stops the caller's wait; the shared scan remains cached for other callers.
            if (_scan is null)
            {
                var completion = new TaskCompletionSource<IReadOnlyList<InstalledApplication>>(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() =>
                {
                    try { completion.SetResult(Scan()); }
                    catch (Exception ex) { completion.SetException(ex); }
                }) { IsBackground = true, Name = "Nokto Start Menu catalog" };
                thread.SetApartmentState(ApartmentState.STA);
                _scan = completion.Task;
                thread.Start();
            }
            return _scan.WaitAsync(cancellationToken);
        }
    }

    private IReadOnlyList<InstalledApplication> Scan()
    {
        var apps = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        var icons = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _roots.Where(Directory.Exists))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (IsExcluded(name)) continue;
                object? instance = null;
                try
                {
                    instance = new ShellLink();
                    ((IPersistFile)instance).Load(file, 0);
                    var link = (IShellLinkW)instance;
                    var path = new StringBuilder(32768);
                    var arguments = new StringBuilder(32768);
                    var directory = new StringBuilder(32768);
                    link.GetPath(path, path.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH: never resolve online or start the target.
                    link.GetArguments(arguments, arguments.Capacity);
                    link.GetWorkingDirectory(directory, directory.Capacity);
                    string exe = Environment.ExpandEnvironmentVariables(path.ToString());
                    if (exe.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(exe) ||
                        !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe) || IsExcluded(Path.GetFileName(exe))) continue;
                    string key = exe + "\n" + arguments;
                    if (apps.ContainsKey(key)) continue;
                    if (!icons.TryGetValue(exe, out var pixels)) icons[exe] = pixels = ReadIcon(exe);
                    apps[key] = new(name, exe, arguments.ToString(), directory.Length == 0 ? null : directory.ToString(), pixels);
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Debug.WriteLine($"Shortcut ignored: {name} ({ex.GetType().Name})");
                }
                finally { if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static bool IsExcluded(string name) => new[] { "uninstall", "desinstalar", "readme", "help" }
        .Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    public static bool IsAppRunning(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath));
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static byte[]? ReadIcon(string path)
    {
        IntPtr icon = IntPtr.Zero, dc = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100) == IntPtr.Zero) return null;
            icon = info.Icon;
            if (icon == IntPtr.Zero) return null;
            dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            var dib = new BitmapInfo { Size = 40, Width = 32, Height = -32, Planes = 1, BitCount = 32, SizeImage = 4096 };
            bitmap = CreateDIBSection(dc, ref dib, 0, out IntPtr pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) return null;
            old = NativeMethods.SelectObject(dc, bitmap);
            if (old == IntPtr.Zero || old == new IntPtr(-1)) { old = IntPtr.Zero; return null; }
            var bytes = new byte[4096];
            Marshal.Copy(bytes, 0, pixels, bytes.Length);
            if (!DrawIconEx(dc, 0, 0, icon, 32, 32, 0, IntPtr.Zero, 3)) return null;
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            if (Enumerable.Range(0, 1024).All(i => bytes[i * 4 + 3] == 0))
                for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            return bytes;
        }
        finally
        {
            if (old != IntPtr.Zero) NativeMethods.SelectObject(dc, old);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (dc != IntPtr.Zero) NativeMethods.DeleteDC(dc);
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    // Keep vtable order, including methods not needed by the passive reader.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPels, YPels; public uint ColorsUsed, ColorsImportant;
        public uint Color;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
