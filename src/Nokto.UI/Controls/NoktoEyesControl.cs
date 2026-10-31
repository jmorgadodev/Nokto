using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nokto.Core.Models;
using Avalonia.Threading;
using System.Diagnostics;

namespace Nokto.UI.Controls;

/// <summary>
/// Renderer de arte vectorial original, escalable y sin carga de 42 imágenes.
/// La posición y el parpadeo se suministran desde un motor de eventos externo.
/// </summary>
public sealed partial class NoktoEyesControl : Control
{
    public static readonly StyledProperty<string> StyleIdProperty =
        AvaloniaProperty.Register<NoktoEyesControl, string>(nameof(StyleId), "Eyes");
    public static readonly StyledProperty<int> EyeCountProperty =
        AvaloniaProperty.Register<NoktoEyesControl, int>(nameof(EyeCount), 2);
    public static readonly StyledProperty<string> StaticDotColorProperty =
        AvaloniaProperty.Register<NoktoEyesControl, string>(nameof(StaticDotColor), "#74E5A3");
    public string StaticDotColor { get => GetValue(StaticDotColorProperty); set => SetValue(StaticDotColorProperty, value); }
    static NoktoEyesControl() => AffectsRender<NoktoEyesControl>(StyleIdProperty, EyeCountProperty, StaticDotColorProperty);
    public int EyeCount { get => GetValue(EyeCountProperty); set => SetValue(EyeCountProperty, value); }

    private double _gazeX, _gazeY, _rightGazeX, _rightGazeY, _blink;
    private bool _sleeping;
    private DispatcherTimer? _activationTimer;
    private long _activationStart;
    private double _activationAngle;
    private double _activationGlow;
    private double _activationProgress = 1;

    public void PlayActivation()
    {
        if (EyeStyleCatalog.IsMinimal(StyleId)) return;
        _sleeping = false;
        _activationStart = Stopwatch.GetTimestamp();
        _activationProgress = 0;
        _activationTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _activationTimer.Tick -= OnActivationTick;
        _activationTimer.Tick += OnActivationTick;
        _activationTimer.Start();
    }

    public void StopActivation()
    {
        _activationTimer?.Stop();
        _activationAngle = _activationGlow = 0;
        _activationProgress = 1;
        InvalidateVisual();
    }

    private void OnActivationTick(object? sender, EventArgs e)
    {
        bool sharingan = EyeStyleCatalog.IsSharingan(StyleId);
        double duration = sharingan ? 3.2 : EyeStyleCatalog.IsSeriesEye(StyleId) ? 2.2 : 1.45;
        double t = Math.Clamp(Stopwatch.GetElapsedTime(_activationStart).TotalSeconds / duration, 0, 1);
        double spin = Math.Clamp((t - .44) / .42, 0, 1);
        double eased = spin * spin * (3 - 2 * spin);
        _activationProgress = t;
        _activationAngle = Math.PI * 2 * eased - (t < .18 ? .18 * Math.Sin(Math.PI * t / .18) : 0);
        _activationGlow = .45 * Math.Sin(Math.PI * Math.Clamp((t - .12) / .55, 0, 1)) +
            .55 * Math.Sin(Math.PI * Math.Clamp((t - .62) / .35, 0, 1));
        InvalidateVisual();
        if (t >= 1) { _activationTimer?.Stop(); _activationAngle = _activationGlow = 0; _activationProgress = 1; }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopActivation();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StyleIdProperty && EyeStyleCatalog.IsMinimal(StyleId)) StopActivation();
    }

    public string StyleId
    {
        get => GetValue(StyleIdProperty);
        set => SetValue(StyleIdProperty, value);
    }

    // Datos observables para pruebas sin mover físicamente el cursor del usuario.
    public (double X, double Y, double Blink, bool Sleeping) Pose => (_gazeX, _gazeY, _blink, _sleeping);

    public void SetPose(double x, double y, double blink, bool sleeping)
        => SetBinocularPose(x, y, x, y, blink, sleeping);

    public void SetBinocularPose(double x, double y, double rightX, double rightY, double blink, bool sleeping)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        rightX = Math.Clamp(rightX, -1, 1);
        rightY = Math.Clamp(rightY, -1, 1);
        blink = Math.Clamp(blink, 0, 1);
        if (Math.Abs(_gazeX-x) < .008 && Math.Abs(_gazeY-y) < .008 &&
            Math.Abs(_rightGazeX-rightX) < .008 && Math.Abs(_rightGazeY-rightY) < .008 &&
            Math.Abs(_blink-blink) < .008 && _sleeping==sleeping) return;
        _gazeX=x; _gazeY=y; _rightGazeX=rightX; _rightGazeY=rightY; _blink=blink; _sleeping=sleeping;
        InvalidateVisual();
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);
        var s = EyeStyleCatalog.Find(StyleId);
        if (s.Id == "None") return;
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (s.Id == "StaticDot")
        {
            var color = Color.TryParse(StaticDotColor, out var selected) ? selected : Color.Parse("#74E5A3");
            double radius = Math.Min(w, h) * .18;
            c.DrawEllipse(new SolidColorBrush(color), null, new Point(w / 2, h / 2), radius, radius);
            return;
        }
        if (s.Id.StartsWith("Cinema", StringComparison.Ordinal)) { DrawCinemaEye(c, w, h); return; }
        if (EyeStyleCatalog.IsSharingan(s.Id))
        {
            DrawSharingan(c, w, h);
            return;
        }
        if (EyeStyleCatalog.IsSeriesEye(s.Id))
        {
            DrawSeriesEye(c, w, h, s.Id);
            return;
        }
        if (s.Id == "Feline")
        {
            DrawFeline(c, w, h);
            return;
        }
        var light = Solid(s.Color);
        var iris = Solid(s.Iris);
        var ink = Solid("#0A1020");
        var shine = Solid("#FAFFFF");
        var neutral = Solid("#94A5B9");
        var stroke = new Pen(light, Math.Max(.85, h*.055), lineCap:PenLineCap.Round);
        double cx=w/2, cy=h/2;
        int variant = Math.Max(0, EyeStyleCatalog.IndexOf(s.Id) % 6);
        double reaction = Math.Sin(Math.PI * _activationProgress);
        double gesture = s.Pack switch
        {
            "Cyber" => Math.Sin(_activationProgress * Math.PI * 2) * reaction,
            "Cute" => Math.Sin(_activationProgress * Math.PI * 3) * reaction,
            "Creature" => Math.Sin(_activationProgress * Math.PI * 1.5) * reaction,
            "Dark" => -.45 * reaction,
            _ => Math.Sin(_activationProgress * Math.PI * 2) * reaction * .65
        };
        gesture *= (variant % 2 == 0 ? 1 : -1) * (.75 + variant * .05);
        using var pulseOpacity = c.PushOpacity(s.Pack == "Minimal" ? 1 - .38 * reaction : 1);

        if (s.Pack=="Minimal")
        {
            double unit=Math.Min(w*.4,h*.35);
            if (s.Id=="SensorLine")
            {
                c.DrawLine(new Pen(light,Math.Max(2,h*.15),lineCap:PenLineCap.Round),
                    new Point(cx-unit,cy),new Point(cx+unit,cy));
                c.DrawEllipse(light,null,RectAt(cx,cy,unit*.18,unit*.18));
            }
            else if (s.Id=="DualDot")
            {
                if (EyeCount == 1) c.DrawEllipse(light,null,RectAt(cx,cy,unit*.38,unit*.38));
                else
                {
                    c.DrawEllipse(light,null,RectAt(cx-unit*.55,cy,unit*.38,unit*.38));
                    c.DrawEllipse(iris,null,RectAt(cx+unit*.55,cy,unit*.38,unit*.38));
                }
            }
            else if (s.Id=="FocusDot")
            {
                c.DrawEllipse(null,stroke,RectAt(cx,cy,unit*.78,unit*.78));
                c.DrawEllipse(iris,null,RectAt(cx,cy,unit*.2,unit*.2));
                c.DrawLine(stroke,new Point(cx-unit*1.2,cy),new Point(cx-unit*.85,cy));
                c.DrawLine(stroke,new Point(cx+unit*.85,cy),new Point(cx+unit*1.2,cy));
            }
            else
            {
                if(s.Id=="GlowDot")
                {
                    c.DrawEllipse(null,new Pen(light,Math.Max(.75,h*.04)),RectAt(cx,cy,unit*1.05,unit*1.05));
                    c.DrawEllipse(null,new Pen(light,.65),RectAt(cx,cy,unit*.8,unit*.8));
                }
                if(s.Id=="BlackDot")
                    c.DrawEllipse(neutral,null,RectAt(cx,cy,unit*.6,unit*.6));
                c.DrawEllipse(light,null,RectAt(cx,cy,unit*.43,unit*.43));
            }
            return;
        }

        int eyes = EyeCount == 1 ? 1 : 2;
        double step=w/eyes;
        double erx=Math.Min(step*.41,h*.49), ery=Math.Min(h*.39,step*.42);
        double gazeRange=Math.Min(erx*.48,h*.25);
        for(int eye=0;eye<eyes;eye++)
        {
            double x=step*(eye+.5), y=cy;
            double expressionBlink = s.Pack switch
            {
                "Creature" => .65 * reaction,
                "Cute" => (.25 + Math.Max(0, Math.Sin(_activationProgress * Math.PI * 4)) * .55) * reaction,
                "Dark" => .35 * reaction,
                _ => Math.Max(0, Math.Sin(_activationProgress * Math.PI * 3)) * reaction * .45
            };
            double openness=_sleeping ? .10 : Math.Clamp(1-Math.Max(_blink, expressionBlink)*.96, .045, 1);
            if (s.Shape=="sleepy") openness*=.56;
            if (s.Shape=="happy") openness*=.50;
            double hh=Math.Max(.9,ery*openness);
            double gx = eye == 0 ? _gazeX : _rightGazeX;
            double gy = eye == 0 ? _gazeY : _rightGazeY;
            double px=x+Math.Clamp(gx + gesture * .45, -1, 1)*gazeRange,py=y+gy*gazeRange*.78*openness;
            var outer=RectAt(x,y,erx,hh);
            if(openness < .16)
            {
                c.DrawLine(new Pen(light,Math.Max(1.6,h*.08),lineCap:PenLineCap.Round),
                    new Point(x-erx*.9,y),new Point(x+erx*.9,y));
                continue;
            }

            // Cada familia usa un lenguaje de formas distinto; la variante modifica
            // geometría y ornamentos, no únicamente el color.
            switch(s.Pack)
            {
                case "Nokto":
                    if(variant==2)
                    {
                        c.DrawEllipse(ink,new Pen(light,2.4),outer);
                        c.DrawEllipse(null,new Pen(iris,.9),RectAt(x,y,erx*.72,hh*.72));
                    }
                    else if(variant==4)
                    {
                        c.DrawEllipse(light,null,outer);
                        c.DrawLine(new Pen(light,1.5),new Point(x-erx*.65,y+hh*.7),new Point(x-erx*.86,y+hh*1.2));
                    }
                    else c.DrawEllipse(light,null,outer);
                    if(variant==1)c.DrawEllipse(null,new Pen(iris,.7),RectAt(x,y,erx*.88,hh*.88));
                    break;

                case "Cyber":
                    if(variant is 1 or 5)
                    {
                        PathLines(c,stroke,(x-erx,y),(x-erx*.73,y-hh),
                            (x+erx*.73,y-hh),(x+erx,y),(x+erx*.73,y+hh),
                            (x-erx*.73,y+hh),(x-erx,y));
                    }
                    else
                    {
                        c.DrawEllipse(ink,new Pen(light,2),outer);
                        c.DrawEllipse(null,new Pen(iris,.8),RectAt(x,y,erx*.8,hh*.8));
                    }
                    if(variant==2)
                        c.DrawLine(new Pen(iris,1.7),new Point(x-erx*.76,y+hh*.4),new Point(x+erx*.76,y+hh*.4));
                    if(variant==4)
                        c.DrawEllipse(null,new Pen(light,.9),RectAt(x,y,erx*1.14,hh*1.12));
                    break;

                case "Anime":
                    c.DrawEllipse(ink,new Pen(light,2),outer);
                    c.DrawEllipse(null,new Pen(iris,1.7),RectAt(px,py,erx*.75,hh*.77));
                    c.DrawEllipse(null,new Pen(light,1),RectAt(px,py,erx*.48,hh*.50));
                    if(variant is 1 or 3)
                    {
                        PathLines(c,new Pen(iris,1.15),(px-erx*.47,py-hh*.15),(px,py-hh*.65),
                            (px+erx*.46,py-hh*.16),(px+erx*.3,py+hh*.35));
                    }
                    if(variant is 2 or 4)
                        for(int j=-1;j<=1;j++)
                            c.DrawLine(stroke,new Point(x+j*erx*.55,y-hh*.92),
                                new Point(x+j*erx*.75,y-hh*1.35));
                    break;

                case "Creature":
                    if(variant is 0 or 1 or 3 or 5)
                    {
                        c.DrawEllipse(light,null,outer);
                        PathLines(c,stroke,(x-erx*1.05,y),(x-erx*.52,y-hh*.98),
                            (x+erx*.54,y-hh*.90),(x+erx*1.05,y));
                    }
                    else c.DrawEllipse(light,null,outer);
                    if(variant==2)
                        c.DrawEllipse(null,new Pen(iris,2.2),RectAt(x,y,erx*.9,hh*.92));
                    if(variant==5)
                        c.DrawLine(new Pen(iris,1),new Point(x-erx*.75,y+hh*.7),new Point(x-erx*1.08,y+hh*1.15));
                    break;

                case "Cute":
                    if(variant==4)c.DrawRectangle(light,null,new Rect(x-erx,y-hh,2*erx,2*hh));
                    else c.DrawEllipse(light,null,outer);
                    if(variant==1)c.DrawEllipse(null,new Pen(iris,.9),RectAt(x,y,erx*1.15,hh*1.1));
                    if(variant is 0 or 3)
                        c.DrawEllipse(shine,null,RectAt(x-erx*.35,y-hh*.38,erx*.18,hh*.23));
                    if(variant is 2 or 5)
                        c.DrawLine(new Pen(iris,1),new Point(x-erx*.8,y-hh*.1),new Point(x+erx*.6,y-hh*.38));
                    break;

                case "Dark":
                    if(variant==3)c.DrawRectangle(ink,new Pen(light,2),new Rect(x-erx,y-hh,erx*2,hh*2));
                    else c.DrawEllipse(ink,new Pen(light,2),outer);
                    if(variant==1)c.DrawEllipse(null,new Pen(iris,1),RectAt(x,y,erx*1.15,hh*1.20));
                    if(variant==2)c.DrawLine(new Pen(light,3),new Point(x-erx,y-hh*.5),new Point(x+erx,y-hh*.8));
                    if(variant==4)c.DrawEllipse(iris,null,RectAt(x,y,erx*.72,hh*.7));
                    break;
            }

            // Los iris se trasladan 4–8 px de verdad, no solo una fracción imperceptible.
            double irx=erx*(s.Pack=="Cute"?.33:.38);
            double iry=hh*(s.Pack=="Creature"?.60:.58);
            if(s.Pack=="Cyber" && variant is 1 or 5)
            {
                c.DrawEllipse(iris,null,RectAt(px,py,irx*.65,iry*.65));
            }
            else if(s.Pack!="Anime" && !(s.Pack=="Dark" && variant==4))
            {
                c.DrawEllipse(iris,null,RectAt(px,py,irx,iry));
            }

            if(s.Pupil=="slit")
                c.DrawEllipse(ink,null,RectAt(px,py,Math.Max(1.2,irx*.22),iry*.85));
            else if(s.Pupil=="square")
                c.DrawRectangle(ink,null,new Rect(px-irx*.43,py-iry*.55,irx*.86,iry*1.1));
            else if(s.Pupil=="ring")
                c.DrawEllipse(null,new Pen(ink,Math.Max(1.1,h*.06)),RectAt(px,py,irx*.60,iry*.62));
            else
                c.DrawEllipse(ink,null,RectAt(px,py,irx*.53,iry*.62));

            if(s.Pack=="Cute" || s.Pack=="Nokto")
                c.DrawEllipse(shine,null,RectAt(px-irx*.4,py-iry*.48,irx*.26,iry*.24));
            if(s.Pack=="Cyber")
            {
                c.DrawLine(stroke,new Point(x-erx*1.28,y),new Point(x-erx*.88,y));
                c.DrawLine(stroke,new Point(x+erx*.88,y),new Point(x+erx*1.28,y));
            }
            if(s.Pack=="Dark" && variant==3)
            {
                c.DrawRectangle(iris,null,new Rect(x-erx*1.10,y+hh*.65,erx*.4,hh*.11));
                c.DrawRectangle(light,null,new Rect(x+erx*.7,y-hh*.86,erx*.55,hh*.13));
            }
            // Firma gráfica específica para cada diseño: rasgos, orbitas y marcas
            // propios, sin texturas externas ni recursos de terceros.
            DrawSignature(c, s, eye, x, y, erx, hh, px, py);
        }
    }

    private static SolidColorBrush Solid(string hex) => new(Color.Parse(hex));
    private static Rect RectAt(double cx,double cy,double rx,double ry)
        => new(cx-rx,cy-ry,rx*2,ry*2);
    private static void PathLines(DrawingContext c, Pen pen, params (double X, double Y)[] points)
    {
        for (int i=1;i<points.Length;i++)
            c.DrawLine(pen,new Point(points[i-1].X,points[i-1].Y),new Point(points[i].X,points[i].Y));
    }
}
