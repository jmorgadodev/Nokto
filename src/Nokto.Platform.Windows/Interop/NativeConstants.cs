namespace Nokto.Platform.Windows.Interop;

internal static class NativeConstants
{
    // Windows Messages
    public const int WM_SYSCOMMAND = 0x0112;
    public const int SC_MONITORPOWER = 0xF170;

    // Monitor Power States
    public const int MONITOR_ON = -1;
    public const int MONITOR_OFF = 2;
    public const int MONITOR_STANDBY = 1;

    // HWND Broadcast
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    // Execution States (SetThreadExecutionState)
    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_USER_PRESENT = 0x00000004,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000
    }

    // ExitWindowsEx Flags
    [Flags]
    public enum ExitWindowsFlags : uint
    {
        EWX_LOGOFF = 0x00000000,
        EWX_SHUTDOWN = 0x00000001,
        EWX_REBOOT = 0x00000002,
        EWX_FORCE = 0x00000004,
        EWX_POWEROFF = 0x00000008,
        EWX_FORCEIFHUNG = 0x00000010,
        EWX_QUICKRESOLVE = 0x00000020,
        EWX_RESTARTAPPS = 0x00000040,
        EWX_HYBRID_SHUTDOWN = 0x00400000
    }

    // Shutdown Reasons
    public const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
    public const uint SHTDN_REASON_MINOR_OTHER = 0x00000000;
    public const uint SHTDN_REASON_FLAG_PLANNED = 0x40000000;

    // Privilege Constants
    public const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x00000020;
    public const uint TOKEN_QUERY = 0x00000008;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    // SendInput Constants
    public const int INPUT_MOUSE = 0;
    public const int INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_F15 = 0x7E;
    public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
    public const ushort VK_MEDIA_STOP = 0xB2;

    // System Metrics
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
}
