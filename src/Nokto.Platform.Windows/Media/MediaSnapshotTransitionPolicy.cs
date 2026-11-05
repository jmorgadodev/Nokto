namespace Nokto.Platform.Windows.Media;

/// <summary>Evita mostrar una pausa vacía durante los cambios breves entre sesiones o pistas.</summary>
public static class MediaSnapshotTransitionPolicy
{
    public static TimeSpan GracePeriod(MediaSessionSnapshot previous, MediaSessionSnapshot next)
    {
        if (previous.Available && !next.Available)
            return TimeSpan.FromMilliseconds(700);

        bool changedTrack = !string.Equals(previous.Title, next.Title, StringComparison.Ordinal) ||
            !string.Equals(previous.Artist, next.Artist, StringComparison.Ordinal);
        if (previous.IsPlaying && next.Available && !next.IsPlaying && changedTrack)
            return TimeSpan.FromMilliseconds(500);

        return TimeSpan.Zero;
    }
}
