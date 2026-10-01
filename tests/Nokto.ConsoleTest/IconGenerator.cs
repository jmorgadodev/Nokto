using SkiaSharp;

namespace Nokto.ConsoleTest;

public static class IconGenerator
{
    public static void GenerateOfficialIco(string outputPath)
    {
        int[] sizes = [16, 32, 48, 256];
        var pngDataList = new List<byte[]>();

        foreach (int size in sizes)
        {
            using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            // 1. Dark circular plate with subtle border (#16181D)
            float padding = size >= 32 ? size * 0.04f : 0.5f;
            float diameter = size - (padding * 2);
            var plateRect = new SKRect(padding, padding, padding + diameter, padding + diameter);

            using var platePaint = new SKPaint
            {
                Color = new SKColor(0x16, 0x18, 0x1D, 0xFA),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawOval(plateRect, platePaint);

            using var borderPaint = new SKPaint
            {
                Color = new SKColor(0x26, 0x29, 0x30),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, size * 0.03f)
            };
            canvas.DrawOval(plateRect, borderPaint);

            // 2. Technical geometric crescent / arc in Cyan (#00D2FF)
            float arcStroke = Math.Max(1.5f, size * 0.09f);
            float arcInset = Math.Max(2f, size * 0.18f);
            var arcRect = new SKRect(arcInset, arcInset, size - arcInset, size - arcInset);

            using var arcPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0xD2, 0xFF),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = arcStroke,
                StrokeCap = SKStrokeCap.Round
            };
            canvas.DrawArc(arcRect, 45, 270, false, arcPaint);

            // 3. Central white glowing dot (#F0F2F5)
            float center = size / 2f;
            float dotRadius = Math.Max(1.2f, size * 0.10f);
            using var dotPaint = new SKPaint
            {
                Color = new SKColor(0xF0, 0xF2, 0xF5),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(center, center, dotRadius, dotPaint);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            pngDataList.Add(data.ToArray());
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var fs = File.Create(outputPath);
        using var bw = new BinaryWriter(fs);

        // Header: ICONDIR (6 bytes)
        bw.Write((ushort)0); // idReserved
        bw.Write((ushort)1); // idType: 1 = ICO
        bw.Write((ushort)sizes.Length); // idCount: 4

        int offset = 6 + (16 * sizes.Length);

        // Entries: ICONDIRENTRY (16 bytes each)
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            byte[] png = pngDataList[i];

            bw.Write((byte)(sz >= 256 ? 0 : sz)); // bWidth
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // bHeight
            bw.Write((byte)0);                    // bColorCount
            bw.Write((byte)0);                    // bReserved
            bw.Write((ushort)1);                  // wPlanes
            bw.Write((ushort)32);                 // wBitCount
            bw.Write((uint)png.Length);           // dwBytesInRes
            bw.Write((uint)offset);               // dwImageOffset

            offset += png.Length;
        }

        // Image data
        foreach (byte[] png in pngDataList)
        {
            bw.Write(png);
        }

        bw.Flush();
    }
}
