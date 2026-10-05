using System.Runtime.InteropServices;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.Hotkeys;

/// <summary>
/// Servicio de atajo global de teclado (Win32 RegisterHotKey) ejecutado en un hilo de mensajes dedicado.
/// Permite capturar la tecla de pánico global (default: Pause/Break o Ctrl+Shift+F12) en cualquier lugar del sistema.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HOTKEY_ID = 9001;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG msg, IntPtr window, uint min, uint max, uint remove);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    private Thread? _messageLoopThread;
    private uint _threadId;
    private uint _currentModifiers;
    private uint _currentVk = 0x13; // VK_PAUSE por defecto
    private readonly Action _onHotkeyPressed;
    private readonly ManualResetEventSlim _readyEvent = new(false);
    private bool _disposed;
    public bool IsRegistered { get; private set; }
    public int RegistrationError { get; private set; }

    public GlobalHotkeyService(Action onHotkeyPressed)
    {
        _onHotkeyPressed = onHotkeyPressed ?? throw new ArgumentNullException(nameof(onHotkeyPressed));
    }

    public bool Start(uint modifiers = 0, uint vk = 0x13)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        _currentModifiers = modifiers;
        _currentVk = vk;
        _readyEvent.Reset();
        IsRegistered = false;
        RegistrationError = 0;

        _messageLoopThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "NoktoGlobalHotkeyThread"
        };
        _messageLoopThread.Start();
        if (!_readyEvent.Wait(2000))
        {
            Stop();
            return false;
        }
        return IsRegistered;
    }

    public void Stop()
    {
        if (_messageLoopThread != null && _messageLoopThread.IsAlive)
        {
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            if (!_messageLoopThread.Join(2000))
                throw new TimeoutException("No se pudo detener el servicio de atajo global.");
            _messageLoopThread = null;
        }
        _threadId = 0;
        IsRegistered = false;
    }

    private void RunMessageLoop()
    {
        bool registered = false;
        try
        {
            _threadId = GetCurrentThreadId();

            // Create the queue without blocking before RegisterHotKey.
            MSG msg;
            _ = PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);

            registered = NativeMethods.RegisterHotKey(IntPtr.Zero, HOTKEY_ID, _currentModifiers | 0x4000, _currentVk); // MOD_NOREPEAT
            IsRegistered = registered;
            RegistrationError = registered ? 0 : Marshal.GetLastWin32Error();
            _readyEvent.Set();
            if (!registered) return;

            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY && (int)msg.wParam == HOTKEY_ID)
                {
                    try
                    {
                        _onHotkeyPressed();
                    }
                    catch
                    {
                        // Evitar que excepciones en el handler rompan el loop de mensajes
                    }
                }
            }
        }
        catch
        {
            _readyEvent.Set();
        }
        finally
        {
            if (registered)
            {
                try
                {
                    NativeMethods.UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
                }
                catch { }
            }
            IsRegistered = false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _readyEvent.Dispose();
            _disposed = true;
        }
    }
}
