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

            case "CrimsonRing":
                Ring(c,glow,px,py,rx*.83,ry*.84,1.4);
                for(int k=0;k<3;k++)Dot(c,rim,px+Math.Cos(.55+k*2.09)*rx*.69,py+Math.Sin(.55+k*2.09)*ry*.69,1.4);
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
