using System.Diagnostics;
using Neobash.PrintBridge.Desktop;

namespace Neobash.PrintBridge.WindowsPrintTests;

public sealed class StaQueueTests
{
    [Fact]
    public async Task Production_queue_contract_in_an_isolated_process()
    {
        const string marker = "NPB_STA_QUEUE_TEST_CHILD";
        if (Environment.GetEnvironmentVariable(marker) == "1")
        {
            await ExerciseQueue();
            return;
        }

        // A recursive Run<T> regression can kill only the child testhost.
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(StaQueueTests).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName~StaQueueTests.Production_queue_contract_in_an_isolated_process");
        start.Environment[marker] = "1";
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        catch { child.Kill(entireProcessTree: true); throw; }
        Assert.True(child.ExitCode == 0, await stdout + "\n" + await stderr);
    }

    private static async Task ExerciseQueue()
    {
        using var queue = new StaQueue();
        var ran = false;
        queue.Run(new Action(() => ran = true));
        Assert.True(ran);
        Assert.Equal(42, queue.Run(() => 42));
        Assert.Equal(ApartmentState.STA, queue.Run(() => Thread.CurrentThread.GetApartmentState()));
        var threadId = queue.Run(() => Environment.CurrentManagedThreadId);
        Assert.Equal(threadId, queue.Run(() => Environment.CurrentManagedThreadId));
        var failure = new InvalidOperationException("STA failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => queue.Run(new Action(() => throw failure))));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => queue.Run<int>(() => throw failure)));
        Assert.Equal(7, queue.Run(() => 7));

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var work = Task.Run(() => queue.Run(new Action(() =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        })));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            queue.Dispose(); // Closing never waits for a blocked driver.
            Assert.Throws<InvalidOperationException>(() => queue.Run(new Action(() => { })));
            Assert.Throws<InvalidOperationException>(() => queue.Run(() => 1));
        }
        finally { release.Set(); }
        await work.WaitAsync(TimeSpan.FromSeconds(10)); // Accepted work drains.
        queue.Dispose();
    }
}
