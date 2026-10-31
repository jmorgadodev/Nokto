using System.Runtime.InteropServices;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private void ExcludeFromAltTab()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero) return;
        const int extendedStyle = -20;
        const long toolWindow = 0x00000080, appWindow = 0x00040000;
        long current = GetWindowLongPtr(handle, extendedStyle).ToInt64();
        SetWindowLongPtr(handle, extendedStyle, new IntPtr((current | toolWindow) & ~appWindow));
        // Refresh only this island's frame; never touch the dashboard or focus.
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
