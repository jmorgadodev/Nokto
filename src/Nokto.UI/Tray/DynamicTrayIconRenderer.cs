using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Nokto.UI.Tray;

public enum TrayIconVisualState
{
    Idle,
    InProgress,
    Completed
}

/// <summary>
/// Renderizador dinámico de iconos en memoria (32x32 px) para el System Tray usando SkiaSharp.
/// Dibuja anillos de progreso en tiempo real y glifos geométricos sin archivos .ico en disco.
/// </summary>
public static class DynamicTrayIconRenderer
{
    private const int IconSize = 32;

    public static WindowIcon RenderTrayIcon(
        TrayIconVisualState state,
        double progressPercentage = 0,
        int secondsRemaining = 0,
        float pulsePhase = 1.0f)
    {
        using var bitmap = new SKBitmap(IconSize, IconSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        switch (state)
        {
            case TrayIconVisualState.Idle:
                DrawIdleGlyph(canvas);
                break;

            case TrayIconVisualState.InProgress:
                DrawProgressRing(canvas, progressPercentage, secondsRemaining, pulsePhase);
                break;

            case TrayIconVisualState.Completed:
                DrawCompletedDiamond(canvas);
                break;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var avaloniaBitmap = new Bitmap(stream);
        return new WindowIcon(avaloniaBitmap);
    }

    private static void DrawIdleGlyph(SKCanvas canvas)
    {
        // Glifo geométrico Nokto: Luna / Anillo estilizado blanco limpio (#FFFFFF)
        using var paint = new SKPaint
        {
            Color = new SKColor(0xF0, 0xF2, 0xF5),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };

        // Círculo base con corte
        var rect = new SKRect(6, 6, 26, 26);
        canvas.DrawArc(rect, 45, 270, false, paint);

        // Núcleo interno
        using var dotPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xD2, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(16, 16, 2.5f, dotPaint);
    }

    private static void DrawProgressRing(SKCanvas canvas, double progressPercentage, int secondsRemaining, float pulsePhase)
    {
        float center = IconSize / 2f;
        float radius = 12f;
        var rect = new SKRect(center - radius, center - radius, center + radius, center + radius);

        // 1. Pista de fondo (#262930)
        using var trackPaint = new SKPaint
        {
            Color = new SKColor(0x26, 0x29, 0x30),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };
        canvas.DrawOval(rect, trackPaint);

        // 2. Arco de progreso: Cian (#00D2FF) o Ámbar (#FFB300) si restan <= 120s
        var arcColor = (secondsRemaining > 0 && secondsRemaining <= 120)
            ? new SKColor(0xFF, 0xB3, 0x00)
            : new SKColor(0x00, 0xD2, 0xFF);

        using var arcPaint = new SKPaint
        {
            Color = arcColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round
        };

        float sweepAngle = Math.Clamp((float)(progressPercentage / 100.0 * 360.0), 1f, 360f);
        canvas.DrawArc(rect, -90, sweepAngle, false, arcPaint);

        // 3. Punto central pulsante con interpolación alfa
        byte alpha = (byte)(140 + (int)(115 * Math.Clamp(pulsePhase, 0f, 1f)));
        using var centerDotPaint = new SKPaint
        {
            Color = arcColor.WithAlpha(alpha),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(center, center, 2.8f, centerDotPaint);
    }

    private static void DrawCompletedDiamond(SKCanvas canvas)
    {
        // Rombo sólido de confirmación en Verde Neón (#00E676)
        using var paint = new SKPaint
        {
            Color = new SKColor(0x00, 0xE6, 0x76),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        using var path = new SKPath();
        path.MoveTo(16, 7);
        path.LineTo(25, 16);
        path.LineTo(16, 25);
        path.LineTo(7, 16);
        path.Close();

        canvas.DrawPath(path, paint);
    }
}
