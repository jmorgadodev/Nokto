using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nokto.Core.Models;

namespace Nokto.UI.Controls;

/// <summary>Un solo renderer vectorial de los 42 estilos; sin GIF, assets externos ni animación en reposo.</summary>
public sealed class NoktoEyesControl : Control
{
    public static readonly StyledProperty<string> StyleIdProperty =
        AvaloniaProperty.Register<NoktoEyesControl, string>(nameof(StyleId), "Eyes");
    static NoktoEyesControl() => AffectsRender<NoktoEyesControl>(StyleIdProperty);
    private double _gazeX, _gazeY, _blink;
    private bool _sleeping;

    public string StyleId
    {
        get => GetValue(StyleIdProperty);
        set => SetValue(StyleIdProperty, value);
    }
    public void SetPose(double x, double y, double blink, bool sleeping)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        blink = Math.Clamp(blink, 0, 1);
        if (Math.Abs(_gazeX - x) < .01 && Math.Abs(_gazeY - y) < .01 &&
            Math.Abs(_blink - blink) < .01 && _sleeping == sleeping) return;
        _gazeX = x; _gazeY = y; _blink = blink; _sleeping = sleeping;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var _style = EyeStyleCatalog.Find(StyleId);
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var color = Brush.Parse(_style.Color);
        var iris = Brush.Parse(_style.Iris);
        var shadow = Brush.Parse("#0C1019");
        var bright = Brush.Parse("#F9FEFF");
        var line = new Pen(color, 1.35);

        if (_style.Pack == "Minimal")
        {
            var cx = w / 2; var cy = h / 2;
            if (_style.Shape == "line")
                context.DrawLine(new Pen(color, 3, lineCap: PenLineCap.Round), new Point(cx - 13, cy), new Point(cx + 13, cy));
            else if (_style.Shape == "dual")
            {
                context.DrawEllipse(color, null, new Rect(cx - 12, cy - 4, 8, 8));
                context.DrawEllipse(color, null, new Rect(cx + 4, cy - 4, 8, 8));
            }
            else if (_style.Shape == "focus")
            {
                context.DrawEllipse(shadow, line, new Rect(cx - 10, cy - 10, 20, 20));
                context.DrawEllipse(color, null, new Rect(cx - 3, cy - 3, 6, 6));
                context.DrawLine(line, new Point(cx - 14,cy),new Point(cx - 9,cy));
                context.DrawLine(line, new Point(cx + 9,cy),new Point(cx + 14,cy));
            }
            else
            {
                if (_style.Id == "GlowDot")
                    context.DrawEllipse(null,new Pen(color,1),new Rect(cx-10,cy-10,20,20));
                if (_style.Id == "BlackDot")
                    context.DrawEllipse(Brush.Parse("#CBD5E1"),null,new Rect(cx-7,cy-7,14,14));
                context.DrawEllipse(color,null,new Rect(cx-5,cy-5,10,10));
            }
            return;
        }

        double step = w / 2;
        for (int i = 0; i < 2; i++)
        {
            double cx = step * (i + .5), cy = h / 2;
            double eyeW = Math.Min(23,step-2), eyeH = 21;
            var shape = _style.Shape;
            double open = _sleeping ? .10 : 1 - _blink * .90;
            if (shape is "sleepy" or "happy") open *= .68;
            double eh = Math.Max(1.2, eyeH * open);
            var eyeRect = new Rect(cx-eyeW/2,cy-eh/2,eyeW,eh);

            if (open < .15)
            {
                context.DrawLine(new Pen(color,2,lineCap:PenLineCap.Round),
                    new Point(cx-eyeW/2,cy),new Point(cx+eyeW/2,cy));
                continue;
            }

            if (shape == "pixel")
                context.DrawRectangle(color,null,new Rect(cx-eyeW/2,cy-eh/2,eyeW,eh));
            else if (shape == "hex")
            {
                // Visor técnico segmentado, generado con líneas vectoriales.
                context.DrawRectangle(shadow,null,new Rect(cx-eyeW/2+3,cy-eh/2,eyeW-6,eh));
                context.DrawLine(line,new Point(cx-eyeW/2,cy),new Point(cx-eyeW/2+3,cy-eh/2));
                context.DrawLine(line,new Point(cx-eyeW/2+3,cy-eh/2),new Point(cx+eyeW/2-3,cy-eh/2));
                context.DrawLine(line,new Point(cx+eyeW/2-3,cy-eh/2),new Point(cx+eyeW/2,cy));
                context.DrawLine(line,new Point(cx+eyeW/2,cy),new Point(cx+eyeW/2-3,cy+eh/2));
                context.DrawLine(line,new Point(cx+eyeW/2-3,cy+eh/2),new Point(cx-eyeW/2+3,cy+eh/2));
                context.DrawLine(line,new Point(cx-eyeW/2+3,cy+eh/2),new Point(cx-eyeW/2,cy));
            }
            else
            {
                context.DrawEllipse(shape is "ring" or "spiral" or "scanner" ? shadow : color,
                    shape is "ring" or "spiral" or "scanner" ? line : null,eyeRect);
            }

            double px = cx + _gazeX * 3.6, py = cy + _gazeY * Math.Min(3.2, eh*.17);
            double ir = shape == "round" ? 7.5 : 6.0;
            if (shape is "cat" or "scanner") ir = 7.0;
            if (shape is "ring" or "spiral")
            {
                context.DrawEllipse(null,new Pen(iris,1.2),new Rect(px-ir,py-ir,ir*2,ir*2));
                if (shape == "spiral")
                {
                    context.DrawLine(new Pen(iris,1),new Point(px-6,py-2),new Point(px+4,py-5));
                    context.DrawLine(new Pen(iris,1),new Point(px+4,py-5),new Point(px+5,py+5));
                }
            }
            else if (shape != "hex")
                context.DrawEllipse(iris,null,new Rect(px-ir,py-ir,ir*2,ir*2));

            if (_style.Pupil == "slit")
                context.DrawEllipse(shadow,null,new Rect(px-1.6,py-6.4,3.2,12.8));
            else if (_style.Pupil == "square")
                context.DrawRectangle(shadow,null,new Rect(px-2.6,py-2.6,5.2,5.2));
            else if (_style.Pupil == "ring")
                context.DrawEllipse(null,new Pen(shadow,2),new Rect(px-3.5,py-3.5,7,7));
            else
                context.DrawEllipse(shadow,null,new Rect(px-3.4,py-4,6.8,8));

            if (_style.Pack == "Cyber")
            {
                context.DrawLine(line,new Point(cx-eyeW/2-2,cy),new Point(cx-eyeW/2+2,cy));
                context.DrawLine(line,new Point(cx+eyeW/2-2,cy),new Point(cx+eyeW/2+2,cy));
            }
            if (_style.Pack == "Dark")
                context.DrawLine(new Pen(iris,1),new Point(cx-eyeW/3,cy-eh/2+2),new Point(cx+eyeW/3,cy-eh/2+2));
            if (_style.Pack == "Cute" && shape != "pixel")
                context.DrawEllipse(bright,null,new Rect(px-4.5,py-5.5,3.5,3.5));
            if (_style.Pack == "Creature" && shape == "cat")
            {
                context.DrawLine(line,new Point(cx-eyeW/2,cy-eh/3),new Point(cx-eyeW/2+5,cy-eh/2-2));
                context.DrawLine(line,new Point(cx+eyeW/2,cy-eh/3),new Point(cx+eyeW/2-5,cy-eh/2-2));
            }
        }
    }
}
