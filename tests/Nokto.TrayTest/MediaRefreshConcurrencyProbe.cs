using Nokto.Platform.Windows.Media;

internal static class MediaRefreshConcurrencyProbe
{
    public static async Task RunAsync()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();
        string current = "old";
        int reads = 0;
        CoalescingMediaRefresh? refresh = null;
        refresh = new CoalescingMediaRefresh(async version =>
        {
            string captured = current;
            if (Interlocked.Increment(ref reads) == 1) { entered.SetResult(); await release.Task; }
            refresh!.TryCommit(version, () => { published.Add(captured); if (captured == "new") completed.TrySetResult(); });
        });
        refresh.Request();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        current = "new";
        refresh.Request();
        release.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (reads != 2 || published.Count != 1 || published[0] != "new")
            throw new Exception("Se perdió una actualización o se publicó una lectura obsoleta.");
        refresh.Stop(() => { });

        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool committed = false;
        refresh = new CoalescingMediaRefresh(async version =>
        {
            entered.SetResult(); await release.Task;
            refresh!.TryCommit(version, () => committed = true);
            completed.SetResult();
        });
        refresh.Request();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refresh.Stop(() => { });
        release.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (committed) throw new Exception("Una lectura cancelada publicó datos después del cierre.");
        Console.WriteLine("[PASS] Lecturas multimedia pendientes y cancelación sin sesiones reales");
    }
}
