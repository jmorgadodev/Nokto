using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Nokto.UI.Services;

/// <summary>Native Windows notification-area message. No installer or extra package.</summary>
public sealed class WindowsBalloonNotifier : IDisposable
{
    private IntPtr _window;
    private DispatcherTimer? _timer;
    private bool _active;

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct TrayData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)]
    private static extern IntPtr CreateWindow(uint ex, string cls, string title,
        uint style, int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr arg);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint="LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode)]
    private static extern bool Notify(uint message, ref TrayData data);

    private TrayData Data(string title="",string info="") => new()
    {
        Size=(uint)Marshal.SizeOf<TrayData>(),Window=_window,Id=2033,Flags=6,
        Icon=LoadIcon(IntPtr.Zero,new IntPtr(32516)),Tip="Nokto · Avisos",
        Info=info.Length>250 ? info[..250] : info,
        Title=title.Length>60 ? title[..60] : title,
        InfoFlags=1|16|128
    };

    public bool Show(string title,string message)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(message)) return false;
        try
        {
            if (_window==IntPtr.Zero)
                _window=CreateWindow(0,"STATIC","Nokto Alerts",0,0,0,0,0,
                    IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            if (_window==IntPtr.Zero) return false;
            var info=Data(title,message);
            if (!_active)
            {
                if (!Notify(0,ref info)) return false;
                _active=true;
            }
            info.Flags=16;
            bool sent=Notify(1,ref info);
            _timer ??= new DispatcherTimer { Interval=TimeSpan.FromSeconds(15) };
            _timer.Tick-=CloseEvent; _timer.Tick+=CloseEvent;
            _timer.Stop(); _timer.Start();
            return sent;
        }
        catch (Exception) { return false; }
    }
    private void CloseEvent(object? sender,EventArgs e)
    {
        _timer?.Stop();
        if (!_active) return;
        var d=Data();
        Notify(2,ref d);
        _active=false;
    }
    public void Dispose()
    {
        CloseEvent(null,EventArgs.Empty);
        _timer=null;
        if(_window!=IntPtr.Zero) { DestroyWindow(_window);_window=IntPtr.Zero; }
    }
}
