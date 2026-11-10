using System.Collections.Concurrent;
using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Desktop;

namespace Neobash.PrintBridge.Tests;

public sealed class DesktopCoordinationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "npb-ui-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Refresh_disables_save_and_print_even_with_old_ready_paper()
    {
        var probe = new Probe();
        var session = Create(probe);
        await session.LoadNamesAsync();
        await session.LoadDetailsAsync("B");
        session.SelectPaper("B paper");
        await session.RecoverAsync();
        Assert.True(session.CanSave);
        Assert.True(session.CanPrint);
        probe.BlockNames = true;
        var refresh = session.LoadNamesAsync();
        try
        {
            await probe.NamesEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.CanSave);
            Assert.False(session.CanPrint);
        }
        finally { probe.ReleaseNames.TrySetResult(); }
        await refresh;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_A_success_or_failure_cannot_replace_cached_B(bool fail)
    {
        var probe = new Probe { FailA = fail };
        var session = Create(probe);
        await session.LoadNamesAsync();
        await session.LoadDetailsAsync("B");
        session.SelectQueue("A");
        await probe.DetailEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = session.LoadDetailsAsync("A");
        session.SelectQueue("B");
        session.SelectPaper("B paper");
        var message = session.Status;
        Assert.Contains("«B»", message, StringComparison.Ordinal);
        probe.ReleaseDetail.TrySetResult();
        await pending;
        Assert.Equal("B", session.SelectedQueueName);
        Assert.Equal("B paper", session.SelectedPaper);
        Assert.Equal("B paper", Assert.Single(session.Papers));
        Assert.Equal(message, session.Status);
    }

    [Fact]
    public async Task Recovery_reloads_history_and_discards_an_older_snapshot_that_finishes_last()
    {
        var disk = new FileJobRecordStore(_root);
        var job = new PrintJob("interrupted", "application/pdf", "test.pdf", "dest", "B paper");
        disk.TryBegin(job, "B");
        disk.Update(job.Id, JobPhase.Prepared, "prepared");
        var jobs = new DelayedHistory(disk);
        var queue = new UiQueue();
        var session = Create(new Probe(), jobs, queue);
        var oldHistory = session.LoadHistoryAsync(null);
        try
        {
            await jobs.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.RecoverAsync();
            await jobs.SecondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.LoadHistoryAsync(null);
            queue.Drain();
            Assert.Contains("incierto", Assert.Single(session.HistoryLabels), StringComparison.OrdinalIgnoreCase);
        }
        finally { jobs.Release.TrySetResult(); }
        await oldHistory;
        queue.Drain();
        Assert.Contains("incierto", Assert.Single(session.HistoryLabels), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JobPhase.Uncertain, new FileJobRecordStore(_root).Find(job.Id)!.Phase);
        Assert.False(disk.TryBegin(job, "B"));
    }

    private DesktopSession Create(Probe probe, IJobRecordStore? jobs = null, UiQueue? queue = null)
    {
        var destinations = new FileDestinationStore(_root);
        destinations.Save(new PrintDestination("dest", "B", "B", "B paper"));
        return new DesktopSession(new DesktopHost(
            destinations,
            jobs ?? new FileJobRecordStore(_root),
            new CachingPrinterCatalog(probe),
            null!,
            new SilentLog()), queue ?? new UiQueue());
    }

    private sealed class UiQueue : IUiQueue
    {
        private readonly ConcurrentQueue<Action> _pending = new();

        public bool TryEnqueue(Action action)
        {
            _pending.Enqueue(action);
            return true;
        }

        public void Drain()
        {
            while (_pending.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class Probe : IPrinterProbe
    {
        public bool BlockNames, FailA;
        public TaskCompletionSource NamesEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseNames = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DetailEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDetail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> ListNames()
        {
            if (BlockNames) { NamesEntered.TrySetResult(); ReleaseNames.Task.GetAwaiter().GetResult(); }
            return ["A", "B"];
        }
        public InstalledQueue? ReadDetails(string name)
        {
            if (name == "A")
            {
                DetailEntered.TrySetResult();
                ReleaseDetail.Task.GetAwaiter().GetResult();
                if (FailA) throw new InvalidOperationException("A failed");
            }
            return new InstalledQueue(name, "Driver", [name + " paper"]);
        }
    }

    private sealed class DelayedHistory(FileJobRecordStore disk) : IJobRecordStore
    {
        private int _reads;
        public TaskCompletionSource Captured = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<JobRecord> List()
        {
            var snapshot = disk.List();
            if (Interlocked.Increment(ref _reads) == 1)
            {
                Captured.TrySetResult();
                Release.Task.GetAwaiter().GetResult();
            }
            else SecondRead.TrySetResult();
            return snapshot;
        }
        public IReadOnlyList<string> ListIds() => disk.ListIds();
        public JobRecord? Find(string id) => disk.Find(id);
        public bool TryBegin(PrintJob job, string queueName) => disk.TryBegin(job, queueName);
        public void Update(string id, JobPhase phase, string? detail) => disk.Update(id, phase, detail);
        public void RecordPhysicalConfirmation(string id, string note) => disk.RecordPhysicalConfirmation(id, note);
    }

    private sealed class SilentLog : IDiagnosticLog
    {
        public void Write(string message) { }
        public void Timing(string operation, TimeSpan elapsed, params (string Key, string Value)[] fields) { }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
