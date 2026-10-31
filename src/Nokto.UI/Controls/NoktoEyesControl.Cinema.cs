using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nokto.UI.Controls;

public sealed partial class NoktoEyesControl
{
    private static readonly Dictionary<string, Bitmap> CinemaTextures = new();

    private void DrawCinemaEye(DrawingContext c, double width, double height)
    {
        string name = StyleId switch { "CinemaSauron" => "sauron", "CinemaToothless" => "toothless", _ => "terminator" };
        if (!CinemaTextures.TryGetValue(name, out var image))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Nokto/Assets/Themes/{name}.png"));
            image = new Bitmap(stream);
            CinemaTextures.Add(name, image);
        }
        int count = EyeCount == 1 ? 1 : 2;
        double step = width / count;
        double reaction = Math.Sin(Math.PI * _activationProgress);
        for (int i = 0; i < count; i++)
        {
            double cx = step*(i+.5), cy = height*.5;
            double gx = i == 0 ? _gazeX : _rightGazeX;
            double gy = i == 0 ? _gazeY : _rightGazeY;
            bool robot = StyleId == "CinemaT800";
            double w = Math.Min(step*.98, height*(robot ? 1.05 : 1.7));
            double h = robot ? w : Math.Min(height*.9, w*.65);
            double open = robot || StyleId == "CinemaSauron" ? 1 : _sleeping ? .10 : Math.Max(.06, 1-_blink*.96);
            var aperture = new Rect(cx-w/2,cy-h*open/2,w,h*open);
            using var clip = c.PushGeometryClip(new EllipseGeometry(aperture));
            double zoom = 1.1 + reaction*(robot ? .1 : .08);
            // La textura se mueve bajo una apertura fija: mirada, párpado y reacción son independientes.
            var target = new Rect(cx-w*zoom/2+gx*w*.065,cy-h*zoom/2+gy*h*.055,w*zoom,h*zoom);
            using var opacity = c.PushOpacity(robot ? .82+.18*reaction : 1);
            c.DrawImage(image,new Rect(image.Size),target);
        }
    }
}
