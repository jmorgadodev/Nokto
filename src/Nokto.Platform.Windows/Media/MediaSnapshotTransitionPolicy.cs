namespace Nokto.Platform.Windows.Media;

/// <summary>Evita mostrar una pausa vacía durante los cambios breves entre sesiones o pistas.</summary>
public static class MediaSnapshotTransitionPolicy
{
    public static TimeSpan GracePeriod(MediaSessionSnapshot previous, MediaSessionSnapshot next)
    {
        if (previous.Available && !next.Available)
            return TimeSpan.FromMilliseconds(700);

        // Do not delay a newly reported track; the renderer now loads album art separately.

        return TimeSpan.Zero;
    }
}
