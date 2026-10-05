using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nokto.Core.Models;

namespace Nokto.UI.ViewModels;

public record InstalledAppInfo(string Name, string ExecutablePath, Bitmap? Icon)
{
    public string Arguments { get; init; } = "";
    public string? WorkingDirectory { get; init; }

    public static InstalledAppInfo FromApplication(InstalledApplication application)
    {
        WriteableBitmap? icon = null;
        if (application.IconPixels is { Length: 4096 } pixels)
        {
            icon = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = icon.Lock();
            for (int row = 0; row < 32; row++) Marshal.Copy(pixels, row * 128, buffer.Address + row * buffer.RowBytes, 128);
        }
        return new(application.Name, application.ExecutablePath, icon) { Arguments = application.Arguments, WorkingDirectory = application.WorkingDirectory };
    }
}
