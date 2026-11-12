using System.Runtime.InteropServices;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    internal void EnsurePositionOnPrimaryScreen()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        double scale = screen.Scaling;
        int width = (int)Math.Ceiling(Width * scale);
        int height = (int)Math.Ceiling(Height * scale);
        bool fullyVisible = Position.X >= area.X && Position.X + width <= area.Right &&
            Position.Y >= area.Y && Position.Y + height <= area.Bottom;
        if (!fullyVisible) Reposition();
    }

    internal void EnsureNativePresentation()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero) return;
        if (!IsWindowVisible(handle)) ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
        long exStyle = GetWindowLongPtr(handle, -20).ToInt64();
        if ((exStyle & 0x00000008) == 0) // WS_EX_TOPMOST
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
    }

    private void ExcludeFromAltTab()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero) return;
        const int extendedStyle = -20;
        const long toolWindow = 0x00000080, appWindow = 0x00040000, noActivate = 0x08000000;
        long current = GetWindowLongPtr(handle, extendedStyle).ToInt64();
        SetWindowLongPtr(handle, extendedStyle, new IntPtr((current | toolWindow | noActivate) & ~appWindow));
        // Keep clicks on the island from stealing foreground focus from fullscreen apps.
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
