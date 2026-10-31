namespace Nokto.ConsoleTest;

/// <summary>Explicit release gate: no native adapter, user storage or personal audio.</summary>
internal static class ReleaseQaTests
{
    public static async Task<int> RunAsync()
    {
        int failed = 0;
        foreach (var suite in new (string Name, Func<Task<int>> Run)[]
        {
            ("Persistence", () => Task.FromResult(PersistenceSafetyTests.Run())),
            ("Power dispatch (fake native backend)", PowerSafetyTests.RunAsync),
            ("Regression/configuration/quotas/updater", () => Task.FromResult(RegressionTests.Run())),
            ("Workflow schedules/pipelines", LinearWorkflowTests.RunAsync),
            ("Workflow concurrency", ConcurrentWorkflowTests.RunAsync),
            ("Workflow pause/cancel/process cleanup", WorkflowLifecycleSafetyTests.RunAsync),
            ("LAN server with fake actions", RemoteServerTests.RunAsync)
        })
        {
            try
            {
                int result = await suite.Run();
                if (result != 0) failed++;
                Console.WriteLine($"[{(result == 0 ? "PASS" : "FAIL")}] Release suite: {suite.Name}");
            }
            catch (Exception ex) { failed++; Console.WriteLine($"[FAIL] Release suite: {suite.Name}: {ex}"); }
        }
        return failed == 0 ? 0 : 1;
    }
}
