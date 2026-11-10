using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Tests;

public sealed class PrinterCatalogTests
{
    [Fact]
    public async Task Invalidation_during_refresh_completion_is_not_lost()
    {
        var probe = new FakeProbe { Names = ["Vieja"] };
        CachingPrinterCatalog catalog = null!;
        var calls = 0;
        var log = new CallbackLog(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                probe.Names = ["Nueva"];
                catalog.Invalidate();
            }
        });
        catalog = new CachingPrinterCatalog(probe, log);

        await catalog.RefreshNamesAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, probe.ListNamesCalls);
        Assert.Equal("Nueva", Assert.Single(catalog.List()).QueueName);
        await catalog.RefreshNamesAsync();
        Assert.Equal(3, probe.ListNamesCalls);
    }

    [Fact]
    public async Task Cancelled_detail_flight_is_removed_and_can_be_read_again()
    {
        var probe = new FakeProbe();
        probe.Details["A"] = Queue("A", "Driver", "A4");
        var catalog = new CachingPrinterCatalog(probe);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ReadDetailsAsync("A", cancelled.Token));
        var result = await catalog.ReadDetailsAsync("A").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("A4", Assert.Single(result!.PaperNames));
        Assert.Equal(1, probe.ReadDetailsCalls);
    }

    [Fact]
    public async Task Concurrent_details_for_the_same_queue_share_one_probe()
    {
        var probe = new FakeProbe { BlockFirstDetail = true, FirstDetail = Queue("A", "Driver", "A4") };
        var catalog = new CachingPrinterCatalog(probe);
        var first = catalog.ReadDetailsAsync("A");
        try
        {
            await probe.DetailsEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = catalog.ReadDetailsAsync("a");
            Assert.Same(first, second);
            probe.ReleaseDetails.TrySetResult();
            Assert.Same(await first, await second);
            Assert.Equal(1, probe.ReadDetailsCalls);
        }
        finally { probe.ReleaseDetails.TrySetResult(); }
    }

    [Fact]
    public async Task Old_details_finishing_after_refresh_cannot_replace_new_capabilities()
    {
        var probe = new FakeProbe
        {
            Names = ["A"], BlockFirstDetail = true, FirstDetail = Queue("A", "Driver", "Viejo"),
        };
        probe.Details["A"] = Queue("A", "Driver", "Nuevo");
        var catalog = new CachingPrinterCatalog(probe);
        var old = catalog.ReadDetailsAsync("A");
        try
        {
            await probe.DetailsEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await catalog.RefreshNamesAsync();
            var current = await catalog.ReadDetailsAsync("A");
            probe.ReleaseDetails.TrySetResult();
            Assert.Null(await old);
            Assert.Equal("Nuevo", Assert.Single(current!.PaperNames));
            Assert.Same(current, catalog.Find("A"));
        }
        finally { probe.ReleaseDetails.TrySetResult(); }
    }

    private sealed class CallbackLog(Action callback) : IDiagnosticLog
    {
        public void Write(string message) { }
        public void Timing(string operation, TimeSpan elapsed, params (string Key, string Value)[] fields) => callback();
    }

    [Fact]
    public async Task Empty_catalog_publishes_no_queues_and_list_does_not_probe()
    {
        var probe = new FakeProbe();
        var catalog = new CachingPrinterCatalog(probe);

        Assert.Empty(catalog.List());
        Assert.Equal(0, probe.ListNamesCalls);
        Assert.Equal(0, probe.ReadDetailsCalls);

        await catalog.RefreshNamesAsync();

        Assert.Empty(catalog.List());
        Assert.Equal(1, probe.ListNamesCalls);
        Assert.Equal(0, probe.ReadDetailsCalls);
    }

    [Fact]
    public async Task One_queue_error_keeps_the_others()
    {
        var probe = new FakeProbe
        {
            Names = ["Star BSC10", "Lenta"],
        };
        probe.DetailErrors["Lenta"] = new InvalidOperationException("El controlador no respondió.");
        probe.Details["Star BSC10"] = Queue("Star BSC10", "Star", "Rollo 80 x 297 mm");
        var catalog = new CachingPrinterCatalog(probe);
        await catalog.RefreshNamesAsync();

        var failed = await catalog.ReadDetailsAsync("Lenta");
        var ready = await catalog.ReadDetailsAsync("Star BSC10");

        Assert.Null(failed);
        Assert.Null(catalog.Find("Lenta"));
        Assert.Equal("Rollo 80 x 297 mm", Assert.Single(ready!.PaperNames));
        var listed = catalog.List();
        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, queue => queue.QueueName == "Star BSC10" && queue.Detail == QueueDetail.Ready);
        Assert.Contains(listed, queue => queue.QueueName == "Lenta" && queue.Detail == QueueDetail.Unreadable);
        Assert.Contains("Lenta", Assert.Single(catalog.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_name_refreshes_share_one_enumeration()
    {
        var probe = new FakeProbe
        {
            BlockFirstNames = true,
            FirstNames = ["Star BSC10"],
            Names = ["Star BSC10"],
        };
        var catalog = new CachingPrinterCatalog(probe);
        var first = catalog.RefreshNamesAsync();
        await probe.NamesEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = catalog.RefreshNamesAsync();
        probe.ReleaseNames.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, probe.ListNamesCalls);
        Assert.Equal("Star BSC10", Assert.Single(catalog.List()).QueueName);
    }

    [Fact]
    public async Task Find_reads_only_the_named_queue()
    {
        var probe = new FakeProbe();
        probe.Details["Star BSC10"] = Queue("Star BSC10", "Star", "Rollo 80 x 297 mm");
        probe.Details["Otra"] = Queue("Otra", "Otro", "A4");
        var catalog = new CachingPrinterCatalog(probe);

        var found = catalog.Find("Star BSC10");

        Assert.Equal("Star BSC10", found!.QueueName);
        Assert.Equal(0, probe.ListNamesCalls);
        Assert.Equal(1, probe.ReadDetailsCalls);
        Assert.Equal(["Star BSC10"], probe.DetailRequests);

        var again = catalog.Find("Star BSC10");
        Assert.Same(found.PaperNames, again!.PaperNames);
        Assert.Equal(1, probe.ReadDetailsCalls);

        catalog.Invalidate();
        var refreshed = catalog.Find("Star BSC10");
        Assert.Equal(2, probe.ReadDetailsCalls);
        Assert.Equal("Rollo 80 x 297 mm", Assert.Single(refreshed!.PaperNames));
        Assert.Empty(probe.ListRequests);
    }

    [Fact]
    public async Task Name_refresh_finishes_while_a_capability_read_is_blocked()
    {
        var probe = new FakeProbe { Names = ["Star BSC10", "Otra"] };
        probe.BlockDetails = true;
        probe.Details["Star BSC10"] = Queue("Star BSC10", "Star", "Rollo 80 x 297 mm");
        var catalog = new CachingPrinterCatalog(probe);
        var details = catalog.ReadDetailsAsync("Star BSC10");
        await probe.DetailsEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await catalog.RefreshNamesAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(details.IsCompleted);
        Assert.Equal(2, catalog.List().Count);
        probe.ReleaseDetails.TrySetResult();
        await details;
    }

    [Fact]
    public async Task Stale_capability_read_does_not_overwrite_a_newer_generation()
    {
        var probe = new FakeProbe();
        probe.BlockFirstDetail = true;
        probe.FirstDetail = Queue("Star BSC10", "Star", "Viejo");
        probe.Details["Star BSC10"] = Queue("Star BSC10", "Star", "Nuevo");
        var catalog = new CachingPrinterCatalog(probe);

        var stale = catalog.ReadDetailsAsync("Star BSC10");
        await probe.DetailsEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        catalog.Invalidate();
        var current = await catalog.ReadDetailsAsync("Star BSC10");

        Assert.Equal("Nuevo", Assert.Single(current!.PaperNames));
        probe.ReleaseDetails.TrySetResult();
        var discarded = await stale;
        Assert.Null(discarded);
        var found = catalog.Find("Star BSC10");
        Assert.Equal("Nuevo", Assert.Single(found!.PaperNames));
        Assert.Equal(2, probe.ReadDetailsCalls);
    }

    [Fact]
    public async Task Stale_name_enumeration_does_not_replace_a_newer_refresh()
    {
        var probe = new FakeProbe();
        probe.BlockFirstNames = true;
        probe.FirstNames = ["Vieja"];
        probe.Names = ["Nueva"];
        var catalog = new CachingPrinterCatalog(probe);
        var refresh = catalog.RefreshNamesAsync();
        await probe.NamesEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        catalog.Invalidate();
        probe.ReleaseNames.TrySetResult();
        await refresh;

        Assert.Equal("Nueva", Assert.Single(catalog.List()).QueueName);
        Assert.Equal(2, probe.ListNamesCalls);
    }

    [Fact]
    public void Saved_queue_missing_virtual_or_present_does_not_switch()
    {
        var other = Queue("Otra impresora", "Otro", "A4");
        var missing = SavedQueueSelection.Match("Star BSC10", [other]);
        Assert.Null(missing.Queue);
        Assert.Contains("no está en el catálogo", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Otra impresora", missing.Message, StringComparison.OrdinalIgnoreCase);

        var virtualQueue = Queue("NPB - Prueba Virtual", "Microsoft Virtual Print Class Driver", "A4");
        var blocked = SavedQueueSelection.Match("NPB - Prueba Virtual", [virtualQueue, other]);
        Assert.Null(blocked.Queue);
        Assert.Contains("no se selecciona otra", blocked.Message, StringComparison.OrdinalIgnoreCase);

        var namedOnlyVirtual = new InstalledQueue("NPB - Prueba Virtual", "", [], Detail: QueueDetail.NameOnly);
        Assert.Null(SavedQueueSelection.Match(namedOnlyVirtual.QueueName, [namedOnlyVirtual, other]).Queue);

        var star = Queue("Star BSC10", "Star", "Rollo 80 x 297 mm");
        var present = SavedQueueSelection.Match("star bsc10", [other, star], "Rollo 80 x 297 mm");
        Assert.Equal("Star BSC10", present.Queue!.QueueName);
        Assert.True(present.ApplySavedPaper);

        var withoutPaper = star with { PaperNames = ["A4"] };
        var paperGone = SavedQueueSelection.Match("Star BSC10", [withoutPaper], "Rollo 80 x 297 mm");
        Assert.Equal("Star BSC10", paperGone.Queue!.QueueName);
        Assert.False(paperGone.ApplySavedPaper);

        var none = SavedQueueSelection.Match(null, []);
        Assert.Null(none.Queue);
        Assert.Contains("No hay colas físicas", none.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prolonged_wait_text_does_not_claim_the_driver_call_was_cancelled()
    {
        Assert.Contains("no se ha cancelado", CatalogWaitText.NamesWaiting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sigue en curso", CatalogWaitText.CapabilitiesWaiting("Star BSC10"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cancelad", CatalogWaitText.CapabilitiesWaiting("Star BSC10"), StringComparison.OrdinalIgnoreCase);
    }

    private static InstalledQueue Queue(string name, string driver, string paper) =>
        new(name, driver, [paper]);

    private sealed class FakeProbe : IPrinterProbe
    {
        public List<string> Names { get; set; } = [];

        public List<string> FirstNames { get; set; } = [];

        public Dictionary<string, InstalledQueue> Details { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Exception> DetailErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public InstalledQueue? FirstDetail { get; set; }

        public bool BlockDetails { get; set; }

        public bool BlockFirstDetail { get; set; }

        public bool BlockFirstNames { get; set; }

        public int ListNamesCalls { get; private set; }

        public int ReadDetailsCalls { get; private set; }

        public List<string> DetailRequests { get; } = [];

        public List<int> ListRequests { get; } = [];

        public TaskCompletionSource NamesEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseNames { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DetailsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDetails { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> ListNames()
        {
            var call = Interlocked.Increment(ref _listCalls);
            ListNamesCalls = call;
            ListRequests.Add(call);
            if (call == 1 && BlockFirstNames)
            {
                NamesEntered.TrySetResult();
                ReleaseNames.Task.GetAwaiter().GetResult();
                return FirstNames;
            }

            if (BlockDetails)
            {
                NamesEntered.TrySetResult();
            }

            return Names;
        }

        public InstalledQueue? ReadDetails(string queueName)
        {
            var call = Interlocked.Increment(ref _detailCalls);
            ReadDetailsCalls = call;
            DetailRequests.Add(queueName);
            if (call == 1 && (BlockDetails || BlockFirstDetail))
            {
                DetailsEntered.TrySetResult();
                ReleaseDetails.Task.GetAwaiter().GetResult();
                if (BlockFirstDetail)
                {
                    return FirstDetail;
                }
            }

            if (DetailErrors.TryGetValue(queueName, out var error))
            {
                throw error;
            }

            return Details.TryGetValue(queueName, out var queue) ? queue : null;
        }

        private int _listCalls;
        private int _detailCalls;
    }
}
