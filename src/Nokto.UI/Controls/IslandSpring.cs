namespace Nokto.UI.Controls;

/// <summary>Resorte amortiguado con continuidad de posición y velocidad al invertir.</summary>
public static class IslandSpring
{
    public static (double Position, double Velocity) Sample(double from, double target, double velocity, double seconds)
    {
        const double frequency = 14, damping = .94;
        double decay = frequency * damping;
        double oscillation = frequency * Math.Sqrt(1 - damping * damping);
        double a = from - target, b = (velocity + decay * a) / oscillation;
        double t = Math.Max(0, seconds), exp = Math.Exp(-decay * t);
        double cos = Math.Cos(oscillation * t), sin = Math.Sin(oscillation * t);
        double offset = a * cos + b * sin;
        return (target + exp * offset, exp * ((-a * oscillation * sin + b * oscillation * cos) - decay * offset));
    }
}
