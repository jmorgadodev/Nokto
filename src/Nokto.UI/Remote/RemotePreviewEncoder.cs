using Nokto.Core.Abstractions;
using SkiaSharp;

namespace Nokto.UI.Remote;

/// <summary>Encodes the existing native GDI capture in memory; never writes screenshots to disk.</summary>
public static class RemotePreviewEncoder
{
    public static async Task<byte[]> CaptureAsync(ISystemAdapter adapter, CancellationToken ct)
    {
        byte[] desktop = await adapter.CaptureScreenAsync(cancellationToken: ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (desktop.Length == 0) return [];
        using var bitmap = SKBitmap.Decode(desktop);
        if (bitmap is null) return [];
        using var surface = SKSurface.Create(new SKImageInfo(1280, 720));
        surface.Canvas.Clear(SKColors.Black);
        float ratio = Math.Min(1280f / bitmap.Width, 720f / bitmap.Height);
        float width = bitmap.Width * ratio, height = bitmap.Height * ratio;
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        surface.Canvas.DrawBitmap(bitmap, new SKRect((1280 - width) / 2, (720 - height) / 2,
            (1280 + width) / 2, (720 + height) / 2), paint);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 70);
        return encoded.ToArray();
    }
}

