using System.Runtime.InteropServices;

namespace Nokto.Platform.Windows.Audio;

internal static class AudioCom
{
    /// <summary>
    /// Activar la interfaz directamente evita convertir el RCW compartido de Windows
    /// a una coclase propia: NAudio puede haber creado ya ese mismo objeto COM.
    /// </summary>
    internal static IMMDeviceEnumerator CreateEnumerator()
    {
        var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        var iid = typeof(IMMDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, IntPtr.Zero, 1, in iid, out var pointer));
        try { return (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr result);
}
