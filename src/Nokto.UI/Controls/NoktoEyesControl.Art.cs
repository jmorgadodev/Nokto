using Avalonia;
using Avalonia.Media;
using Nokto.Core.Models;

namespace Nokto.UI.Controls;

/// <summary>
/// Signature geometry rendered over each eye. Distinct hand-authored identity
/// per design at 100 % and 140 % scale; no borrowed characters or assets.
/// </summary>
public sealed partial class NoktoEyesControl
{
    // Solo los iris, sin accesorios laterales; el recorte anima el parpadeo.
    private void DrawSharingan(DrawingContext c, double width, double height)
    {
        bool rinnegan = StyleId == "UchihaMadaraRinnegan";
        var irisOuter = Solid(rinnegan ? "#342046" : "#4D0711");
        var irisRed = Solid(rinnegan ? "#A58BC8" : "#C81227");
        var irisLight = Solid(rinnegan ? "#E2D5F3" : "#EF3D42");
        var ink = Solid("#100609");
        var shine = Solid("#FFE8E5");
        int eyes = EyeCount == 1 ? 1 : 2;
        double step = width / eyes, rx = Math.Min(step * .46, height * .55);
        double ry = Math.Min(height * .35, step * .34);
        double anticipation = _activationProgress < .18
            ? Math.Sin(Math.PI * _activationProgress / .18) * .87 : 0;
        double open = _sleeping ? .08 : Math.Clamp(1 - Math.Max(_blink, anticipation) * .96, .04, 1);

        for (int eye = 0; eye < eyes; eye++)
        {
            double x = step * (eye + .5), y = height * .53;
            double radius = Math.Min(rx * .83, ry * 1.18);
            double halfHeight = Math.Max(.5, radius * open);
            if (open > .12)
            {
                using (c.PushClip(new Rect(x - rx, y - halfHeight, rx * 2, halfHeight * 2)))
                {
                    double gx = eye == 0 ? _gazeX : _rightGazeX;
                    double gy = eye == 0 ? _gazeY : _rightGazeY;
                    double px = x + gx * rx * .38, py = y + gy * ry * .30;
                    if (_activationGlow > .01)
                        c.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(
                            (byte)(100 * _activationGlow), 235, 25, 45)), Math.Max(1, radius * .16)),
                            RectAt(px, py, radius * 1.08, radius * 1.08));
                    c.DrawEllipse(irisOuter, null, RectAt(px, py, radius, radius));
                    c.DrawEllipse(irisRed, null, RectAt(px, py, radius * .88, radius * .88));
                    c.DrawEllipse(null, new Pen(irisLight, Math.Max(.5, radius * .06)),
                        RectAt(px, py, radius * .78, radius * .78));
                    bool tomoeStyle = StyleId is "CrimsonRing" or "SharinganOne" or "SharinganTwo";
                    double reveal = tomoeStyle ? 0 : _activationProgress < .08 ? 1 : Math.Clamp((_activationProgress - .70) / .16, 0, 1);
                    using (c.PushOpacity(1 - reveal))
                    {
                    if (tomoeStyle || reveal < 1)
                        c.DrawEllipse(null, new Pen(ink, Math.Max(.65, radius * .065)),
                            RectAt(px, py, radius * .56, radius * .56));
                    int count = StyleId == "SharinganOne" ? 1 : StyleId == "SharinganTwo" ? 2 : 3;
                    for (int i = 0; i < count && (tomoeStyle || reveal < 1); i++)
                    {
                        double emergence = _activationProgress < .08 || _activationProgress >= .96 ? 1 :
                            Math.Clamp((_activationProgress - (.15 + i * .11)) / .075, 0, 1);
                        if (emergence <= 0) continue;
                        using var opacity = c.PushOpacity(emergence);
                        double angle = -Math.PI / 2 + i * Math.PI * 2 / count + _activationAngle;
                        Point P(double radial, double tangent) => new(
                            px + radius * (radial * Math.Cos(angle) - tangent * Math.Sin(angle)),
                            py + radius * (radial * Math.Sin(angle) + tangent * Math.Cos(angle)));
                        var tomoe = new StreamGeometry();
                        using (var g = tomoe.Open())
                        {
                            g.BeginFigure(P(.56, -.15), true);
                            g.CubicBezierTo(P(.77, -.15), P(.76, .11), P(.62, .15));
                            g.CubicBezierTo(P(.49, .18), P(.39, .11), P(.42, .01));
                            g.CubicBezierTo(P(.43, -.10), P(.48, -.16), P(.56, -.15));
                            g.EndFigure(true);
                        }
                        c.DrawGeometry(ink, null, tomoe);
                        // La cola se curva tangencialmente y afina hacia el círculo.
                        var tail = new StreamGeometry();
                        using (var g = tail.Open())
                        {
                            g.BeginFigure(P(.61, .12), true);
                            g.CubicBezierTo(P(.65, .27), P(.58, .37), P(.40, .46));
                            g.CubicBezierTo(P(.51, .32), P(.50, .23), P(.49, .13));
                            g.EndFigure(true);
                        }
                        c.DrawGeometry(ink, null, tail);
                    }
                    }
                    if (!tomoeStyle && reveal > 0)
                        using (c.PushOpacity(reveal)) DrawMangekyo(c, ink, px, py, radius, StyleId, _activationAngle);
                    c.DrawEllipse(ink, null, RectAt(px, py, radius * .22, radius * .22));
                    c.DrawEllipse(shine, null, RectAt(px - radius * .31, py - radius * .43,
                        radius * .095, radius * .07));
                    if (_activationGlow > .01)
                        c.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(
                            (byte)(130 * _activationGlow), 255, 122, 110)), Math.Max(.7, radius * .12)),
                            RectAt(px, py, radius * .91, radius * .91));
                }
            }
        }
    }

    private static void DrawMangekyo(DrawingContext c, IBrush ink, double x, double y,
        double radius, string style, double rotation)
    {
        if (style == "UchihaMadaraRinnegan")
        {
            foreach (double ring in new[] { .36, .54, .72, .87 })
                c.DrawEllipse(null, new Pen(ink, Math.Max(.65, radius * .055)),
                    RectAt(x, y, radius * ring, radius * ring));
            return;
        }
        int blades = style switch
        {
            "UchihaShisui" => 4,
            "UchihaSasuke" or "UchihaSasukeEternal" or "UchihaMadaraEternal" => 6,
            "UchihaSarada" => 8,
            _ => 3
        };
        bool curved = style is "UchihaItachi" or "UchihaObito" or "UchihaShisui" or "UchihaMadara";
        double inner = style is "UchihaObito" or "UchihaMadara" ? .30 : .16;
        double tip = style == "UchihaSarada" ? .86 : .83;
        for (int i = 0; i < blades; i++)
        {
            double a = rotation - Math.PI / 2 + i * Math.PI * 2 / blades;
            Point P(double radial, double tangent) => new(
                x + radius * (radial * Math.Cos(a) - tangent * Math.Sin(a)),
                y + radius * (radial * Math.Sin(a) + tangent * Math.Cos(a)));
            var blade = new StreamGeometry();
            using (var g = blade.Open())
            {
                g.BeginFigure(P(inner, -.10), true);
                if (curved)
                {
                    double sweep = style switch { "UchihaShisui" => .32, "UchihaMadara" => .42, _ => .23 };
                    g.CubicBezierTo(P(.38, -.14), P(.68, -sweep), P(tip, -sweep));
                    g.CubicBezierTo(P(.68, .03), P(.45, .17), P(inner, .12));
                }
                else
                {
                    double thickness = style == "UchihaSarada" ? .075 : .14;
                    g.LineTo(P(tip, 0));
                    g.LineTo(P(.35, thickness));
                    g.LineTo(P(inner, .10));
                }
                g.EndFigure(true);
            }
            c.DrawGeometry(ink, null, blade);
        }
        if (style is "UchihaObito" or "UchihaMadara" or "UchihaMadaraEternal")
            c.DrawEllipse(null, new Pen(ink, Math.Max(.7, radius * .09)), RectAt(x, y, radius * .46, radius * .46));
        if (style == "UchihaSasukeEternal")
        {
            for (int i = 0; i < 3; i++)
            {
                double a = rotation + i * Math.PI * 2 / 3;
                c.DrawEllipse(ink, null, RectAt(x + Math.Cos(a) * radius * .48,
                    y + Math.Sin(a) * radius * .48, radius * .10, radius * .10));
            }
        }
        if (style == "UchihaSarada")
            c.DrawEllipse(null, new Pen(ink, Math.Max(.65, radius * .07)), RectAt(x, y, radius * .52, radius * .52));
    }

    private void DrawSeriesEye(DrawingContext c, double width, double height, string style)
    {
        int eyes = EyeCount == 1 ? 1 : 2;
        double step = width / eyes, radius = Math.Min(step * .38, height * .40);
        double reaction = Math.Sin(Math.PI * _activationProgress);
        double blink = Math.Max(_blink, .4 * reaction * Math.Max(0, Math.Sin(_activationProgress * Math.PI * 2)));
        double openness = _sleeping ? .08 : Math.Clamp(1 - blink, .05, 1);
        var dark = Solid("#15101D");
        for (int eye = 0; eye < eyes; eye++)
        {
            double x = step * (eye + .5), y = height * .5;
            double gx = eye == 0 ? _gazeX : _rightGazeX;
            double gy = eye == 0 ? _gazeY : _rightGazeY;
            double px = x + gx * radius * .22, py = y + gy * radius * .16;
            using (c.PushClip(new Rect(x - radius * 1.35, y - radius * openness,
                radius * 2.7, radius * 2 * openness)))
            {
                string color = style switch
                {
                    "SeriesByakugan" => "#D9D3E9", "SeriesGeass" => "#7F548A",
                    "SeriesSixEyes" => "#48C5EE", _ => "#D89842"
                };
                var baseColor = Solid(color);
                var bright = Solid(style == "SeriesSixEyes" ? "#E3FBFF" : "#FFF1E7");
                c.DrawEllipse(dark, null, RectAt(px, py, radius, radius));
                c.DrawEllipse(baseColor, null, RectAt(px, py, radius * .88, radius * .88));
                if (style == "SeriesByakugan")
                {
                    c.DrawEllipse(null, new Pen(Solid("#A69ABC"), Math.Max(.8, radius * .08)),
                        RectAt(px, py, radius * (.46 + .12 * reaction), radius * (.46 + .12 * reaction)));
                    c.DrawEllipse(Solid("#C6BDD9"), null, RectAt(px, py, radius * .18, radius * .18));
                }
                else if (style == "SeriesGeass")
                {
                    c.DrawEllipse(dark, null, RectAt(px, py, radius * .20, radius * .20));
                    var symbol = new StreamGeometry();
                    using (var g = symbol.Open())
                    {
                        g.BeginFigure(new Point(px - radius * .68, py - radius * .30), true);
                        g.LineTo(new Point(px - radius * .20, py - radius * .10));
                        g.LineTo(new Point(px, py + radius * .39));
                        g.LineTo(new Point(px + radius * .20, py - radius * .10));
                        g.LineTo(new Point(px + radius * .68, py - radius * .30));
                        g.LineTo(new Point(px + radius * .35, py + radius * .22));
                        g.LineTo(new Point(px, py + radius * .60));
                        g.LineTo(new Point(px - radius * .35, py + radius * .22));
                        g.EndFigure(true);
                    }
                    c.DrawGeometry(Solid(reaction > .3 ? "#FF5A69" : "#D52A46"), null, symbol);
                }
                else if (style == "SeriesSixEyes")
                {
                    for (int k = 0; k < 12; k++)
                    {
                        double a = k * Math.PI / 6 + reaction * .12;
                        c.DrawLine(new Pen(bright, Math.Max(.55, radius * .045)),
                            new Point(px + Math.Cos(a) * radius * .25, py + Math.Sin(a) * radius * .25),
                            new Point(px + Math.Cos(a) * radius * (.70 + reaction * .08), py + Math.Sin(a) * radius * (.70 + reaction * .08)));
                    }
                    c.DrawEllipse(Solid("#2774A8"), null, RectAt(px, py, radius * .21, radius * .21));
                    c.DrawEllipse(bright, null, RectAt(px, py, radius * .10, radius * .10));
                }
                else
                {
                    c.DrawEllipse(null, new Pen(Solid("#873B37"), Math.Max(.7, radius * .08)), RectAt(px, py, radius * .64, radius * .64));
                    c.DrawEllipse(dark, null, RectAt(px, py, radius * (.10 + reaction * .09), radius * .49));
                }
                c.DrawEllipse(bright, null, RectAt(px - radius * .32, py - radius * .39, radius * .08, radius * .06));
            }
        }
    }

    private void DrawFeline(DrawingContext c, double width, double height)
    {
        var dark = Solid("#17100C");
        var iris = Solid("#EAB95F");
        var inner = Solid("#AF762F");
        var shine = Solid("#FFF3CE");
        int eyes = EyeCount == 1 ? 1 : 2;
        double step = width / eyes, rx = Math.Min(step * .44, height * .5);
        double ry = Math.Min(height * .31, step * .33);
        double reaction = Math.Sin(Math.PI * _activationProgress);
        double slowBlink = .72 * Math.Pow(reaction, 2);
        double openness = _sleeping ? .08 : Math.Clamp(1 - Math.Max(_blink, slowBlink), .06, 1);
        for (int eye = 0; eye < eyes; eye++)
        {
            double x = step * (eye + .5), y = height * .53;
            double h = Math.Max(.5, ry * openness);
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                g.BeginFigure(new Point(x - rx, y), true);
                g.CubicBezierTo(new Point(x - rx * .55, y - h * 1.3), new Point(x + rx * .55, y - h * 1.3), new Point(x + rx, y));
                g.CubicBezierTo(new Point(x + rx * .55, y + h * 1.3), new Point(x - rx * .55, y + h * 1.3), new Point(x - rx, y));
                g.EndFigure(true);
            }
            c.DrawGeometry(iris, new Pen(dark, Math.Max(1, height * .045)), shape);
            using (c.PushGeometryClip(shape))
            {
                double gx = eye == 0 ? _gazeX : _rightGazeX;
                double gy = eye == 0 ? _gazeY : _rightGazeY;
                double gaze = Math.Clamp(gx + reaction * .22, -1, 1);
                double px = x + gaze * rx * .24, py = y + gy * ry * .18;
                c.DrawEllipse(inner, null, RectAt(px, py, rx * .43, ry * .92));
                c.DrawEllipse(dark, null, RectAt(px, py, Math.Max(.8, rx * (.09 + .07 * reaction)), ry * .85));
                c.DrawEllipse(shine, null, RectAt(px - rx * .17, py - ry * .42, rx * .07, ry * .09));
            }
        }
    }

    private static void DrawSignature(DrawingContext c, EyeStyleDefinition s, int side,
        double x,double y,double rx,double ry,double px,double py)
    {
        var glow = new SolidColorBrush(Color.Parse(s.Iris));
        var rim = new SolidColorBrush(Color.Parse(s.Color));
        var subtle = new SolidColorBrush(Color.Parse("#748AA0"));
        var thin = new Pen(glow, 1.1, lineCap:PenLineCap.Round);
        var bold = new Pen(rim, 1.6, lineCap:PenLineCap.Round);
        var pale = new Pen(subtle,.75,lineCap:PenLineCap.Round);
        double t = side==0 ? -1 : 1;

        switch(s.Id)
        {
            case "Eyes":
                Arc(c,pale,x,y-ry*1.17,rx*.65,ry*.14);
                c.DrawEllipse(null,new Pen(glow,.7),RectAt(px,py,rx*.53,ry*.57));
                break;
            case "ClassicSoft":
                Arc(c,new Pen(rim,1.8),x,y+ry*.86,rx*.68,ry*.18);
                Dot(c,glow,x-rx*.72,y+ry*.94,1.1);
                break;
            case "ClassicNeon":
                Ring(c,glow,x,y,rx*1.18,ry*1.17,1.05);
                for(int k=0;k<3;k++)Dot(c,rim,x+rx*Math.Cos(k*2.09)*1.27,y+ry*Math.Sin(k*2.09)*1.28,1);
                break;
            case "ClassicMono":
                Line(c,pale,x-rx*1.13,y-ry*.1,x-rx*.8,y-ry*.1);
                Line(c,pale,x+rx*.8,y-ry*.1,x+rx*1.13,y-ry*.1);
                break;
            case "ClassicGhost":
                Ring(c,glow,x,y,rx*1.12,ry*1.09,.8);
                Dot(c,rim,x+rx*.55,y-ry*1.24,1.9);
                Dot(c,rim,x-rx*.68,y+ry*1.16,.8);
                break;
            case "ClassicSleepy":
                Arc(c,bold,x,y-ry*.48,rx*.95,ry*.27);
                Line(c,pale,x+rx*.58,y-ry*.32,x+rx*.96,y-ry*.65);
                break;

            case "Orbital":
                Ring(c,glow,px,py,rx*.82,ry*.82,1);
                Dot(c,rim,x+rx*1.04,y-ry*.8,2);
                Line(c,thin,x-rx*.9,y+ry*.85,x-rx*.55,y+ry*1.1);
                break;
            case "Sentinel":
                Line(c,bold,x-rx,y-ry*.95,x-rx*.36,y-ry*1.24);
                Line(c,bold,x+rx*.36,y-ry*1.24,x+rx,y-ry*.95);
                Dot(c,glow,x,y-ry*1.29,1.6);
                break;
            case "Scanner":
                for (int k=-1;k<=1;k++) Line(c,new Pen(glow,.65),x-rx*.72,y+k*ry*.31,x+rx*.72,y+k*ry*.31);
                Line(c,bold,x-rx*1.11,y-ry*.6,x-rx*1.11,y+ry*.6);
                break;
            case "Pulse":
                Ring(c,glow,x,y,rx*1.18,ry*1.15,.9);
                Line(c,bold,x-rx*1.25,y,x-rx*.97,y-ry*.34);
                Line(c,bold,x+rx*.97,y+ry*.34,x+rx*1.25,y);
                break;
            case "Echo":
                Ring(c,glow,px,py,rx*.66,ry*.69,.75);
                Ring(c,rim,px,py,rx*.94,ry*.92,.65);
                Ring(c,glow,x,y,rx*1.19,ry*1.19,.55);
                break;
            case "Vector":
                Line(c,bold,x-rx*1.21,y-ry*.55,x-rx*.92,y-ry*.8);
                Line(c,bold,x-rx*1.21,y-ry*.55,x-rx*.92,y-ry*.28);
                Line(c,bold,x+rx*1.21,y+ry*.55,x+rx*.92,y+ry*.8);
                Line(c,bold,x+rx*1.21,y+ry*.55,x+rx*.92,y+ry*.28);
                break;

            case "Vortex":
                for(int k=0;k<4;k++)
                {
                    double a=k*Math.PI*.5+.45;
                    Line(c,thin,px+Math.Cos(a)*rx*.33,py+Math.Sin(a)*ry*.35,
                        px+Math.Cos(a+.5)*rx*.75,py+Math.Sin(a+.5)*ry*.79);
                }
                break;
            case "LunarVein":
                for(int k=-1;k<=1;k++)
                    Line(c,thin,x+k*rx*.53,y-ry*.85,x+k*rx*.7,y-ry*1.23);
                Dot(c,rim,x,y-ry*1.34,1);
                break;
            case "SpiralFocus":
                Ring(c,glow,px,py,rx*.55,ry*.52,.8);
                Line(c,thin,px-rx*.52,py-ry*.11,px+rx*.18,py-ry*.46);
                Line(c,thin,px+rx*.18,py-ry*.46,px+rx*.5,py+ry*.13);
                break;
            case "Ember":
                Line(c,bold,x-rx*.84,y-ry*.76,x-rx*.44,y-ry*1.3);
                Line(c,bold,x-rx*.44,y-ry*1.3,x-rx*.13,y-ry*.9);
                Line(c,bold,x+rx*.45,y-ry*.83,x+rx*.63,y-ry*1.26);
                break;
            case "PhantomGaze":
                Ring(c,glow,px,py,rx*.75,ry*.78,.7);
                Dot(c,rim,x-rx*.9,y-ry*.85,1.6);
                Dot(c,glow,x+rx*.89,y+ry*.82,1.1);
                break;

            case "Feline":
                for(int k=0;k<2;k++)
                  Line(c,pale,x+t*rx*.8,y+ry*(.43+k*.25),
                      x+t*rx*(1.35+k*.07),y+ry*(.39+k*.32));
                break;
            case "Reptile":
                for(int k=-1;k<=1;k++) Ring(c,glow,x+k*rx*.55,y-ry*1.11,rx*.24,ry*.18,.9);
                Line(c,bold,x-rx*.76,y-ry*.65,x+rx*.73,y-ry*.82);
                break;
            case "Owl":
                Ring(c,glow,x,y,rx*1.21,ry*1.17,1.4);
                Arc(c,pale,x,y-ry*1.12,rx*.9,ry*.3);
                Dot(c,rim,x+rx*.78,y-ry*.95,1.6);
                break;
            case "Fox":
                Line(c,bold,x-rx*1.18,y-ry*.4,x-rx*.57,y-ry*1.15);
                Line(c,bold,x+rx*1.18,y-ry*.4,x+rx*.57,y-ry*1.15);
                break;
            case "Wolf":
                Line(c,bold,x-rx*1.13,y-ry*1.15,x+rx*.24,y-ry*.9);
                Line(c,pale,x+rx*.32,y+ry*.96,x+rx*.95,y+ry*.72);
                break;
            case "Dragon":
                for(int k=0;k<3;k++)
                {
                    double xx=x-rx*.7+k*rx*.68;
                    Line(c,bold,xx,y-ry*.8,xx+rx*.2,y-ry*1.37);
                }
                break;

            case "Kawaii":
                Dot(c,glow,x-rx*1.13,y+ry*.61,2);
                Dot(c,glow,x+rx*1.13,y+ry*.61,2);
                Dot(c,rim,x+rx*.9,y-ry*1.1,1.1);
                break;
            case "Surprised":
                Ring(c,glow,x,y,rx*1.16,ry*1.15,.9);
                Dot(c,rim,x+rx*.93,y-ry*.9,2);
                break;
            case "Shy":
                Arc(c,thin,x,y-ry*.55,rx*.9,ry*.35);
                Dot(c,glow,x+rx*.99,y+ry*.68,1.7);
                break;
            case "Happy":
                Arc(c,bold,x,y-ry*.27,rx*.9,ry*.55);
                Dot(c,glow,x-rx*.98,y+ry*.75,1.8);
                break;
            case "Pixel":
                for(int k=0;k<3;k++)
                  c.DrawRectangle(glow,null,new Rect(x-rx*1.3+k*rx*.28,y-ry*1.16,rx*.19,ry*.18));
                c.DrawRectangle(rim,null,new Rect(x+rx*1.08,y+ry*.82,rx*.2,ry*.22));
                break;
            case "TinySleepy":
                Arc(c,pale,x,y-ry*.42,rx*.88,ry*.25);
                Line(c,thin,x+rx*.72,y-ry*1.1,x+rx*.96,y-ry*1.1);
                Line(c,thin,x+rx*.96,y-ry*1.1,x+rx*.72,y-ry*.83);
                break;

            case "Phantom":
                Ring(c,glow,x,y,rx*1.12,ry*1.1,.65);
                Line(c,pale,x-rx*.3,y+ry,x-rx*.1,y+ry*1.28);
                Dot(c,glow,x+rx*.69,y-ry*1.05,1.4);
                break;
            case "Spectral":
                Ring(c,glow,px,py,rx*.73,ry*.75,1);
                for(int k=0;k<3;k++)Dot(c,rim,x+rx*(k-1)*.5,y+ry*1.13,1);
                break;
            case "Shadow":
                Line(c,bold,x-rx*1.04,y-ry*.85,x+rx*.96,y-ry*.44);
                Arc(c,pale,x,y+ry*.93,rx*.68,ry*.13);
                break;
            case "Glitch":
                for(int k=0;k<3;k++)
                    c.DrawRectangle(k%2==0?glow:rim,null,new Rect(x-rx*(1.18-k*.12),y+ry*(k*.45-.8),rx*(.45+k*.1),ry*.11));
                break;
            case "Void":
                Ring(c,glow,px,py,rx*.85,ry*.86,.85);
                Ring(c,rim,x,y,rx*1.16,ry*1.14,.7);
                Dot(c,glow,x-rx*1.16,y,1.5);
                break;
            case "Hollow":
                Ring(c,glow,x,y,rx*1.16,ry*1.16,.85);
                Line(c,new Pen(rim,2.7),x+rx*.79,y-ry*.93,x+rx*1.16,y-ry*.66);
                Dot(c,glow,x-rx*.85,y+ry*1.16,1);
                break;
        }
    }

    private static void Dot(DrawingContext c,IBrush fill,double x,double y,double rad)
        => c.DrawEllipse(fill,null,RectAt(x,y,rad,rad));
    private static void Ring(DrawingContext c,IBrush fill,double x,double y,double rx,double ry,double width)
        => c.DrawEllipse(null,new Pen(fill,width),RectAt(x,y,rx,ry));
    private static void Line(DrawingContext c,Pen pen,double x1,double y1,double x2,double y2)
        => c.DrawLine(pen,new Point(x1,y1),new Point(x2,y2));
    private static void Arc(DrawingContext c,Pen pen,double x,double y,double rx,double bend)
    {
        Line(c,pen,x-rx,y+bend,x,y-bend);
        Line(c,pen,x,y-bend,x+rx,y+bend);
    }
}
