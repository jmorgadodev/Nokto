namespace Nokto.Platform.Windows.Media;

/// <summary>Coalesces notifications while preventing an obsolete read from publishing.</summary>
public sealed class CoalescingMediaRefresh(Func<long, Task> read)
{
    private readonly object _gate = new();
    private long _version;
    private bool _running, _stopped;

    public void Request()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _version++;
            if (_running) return;
            _running = true;
            _ = Task.Run(PumpAsync);
        }
    }

    public bool TryCommit(long version, Action commit)
    {
        lock (_gate)
        {
            if (_stopped || version != _version) return false;
            commit();
            return true;
        }
    }

    public void Stop(Action cleanup)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            _version++;
            cleanup();
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            long version;
            lock (_gate)
            {
                if (_stopped) { _running = false; return; }
                version = _version;
            }
            try { await read(version); }
            catch (Exception) { /* The owner handles read failures; keep the pump alive. */ }
            lock (_gate)
            {
                if (_stopped || version == _version) { _running = false; return; }
            }
        }
    }
}
