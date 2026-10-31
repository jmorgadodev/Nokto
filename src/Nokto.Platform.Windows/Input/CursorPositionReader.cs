using System.Runtime.InteropServices;

namespace Nokto.Platform.Windows.Input;

/// <summary>Lectura de posición; no instala hooks ni captura clics/teclas.</summary>
public static class CursorPositionReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    public static bool TryGet(out int x, out int y)
    {
        x = y = 0;
        if (!GetCursorPos(out var p)) return false;
        x = p.X; y = p.Y;
        return true;
    }
}
