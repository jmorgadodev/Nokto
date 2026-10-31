namespace Nokto.UI.Controls;

/// <summary>Dirección independiente desde cada ojo hacia un punto de pantalla.</summary>
public static class BinocularGaze
{
    public static (double X, double Y) Toward(double pointerX, double pointerY,
        double eyeX, double eyeY, double sensitivity)
    {
        double dx = pointerX - eyeX, dy = pointerY - eyeY;
        double strength = .65 + Math.Clamp(sensitivity, 0, 1) * .35;
        double x = Math.Tanh(dx / (80 + Math.Abs(dy) * .9)) * strength;
        double y = Math.Tanh(dy / (150 + Math.Abs(dx) * .6)) * strength;
        return (x, y);
    }
}
