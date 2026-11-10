using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Tests;

public sealed class ExperienceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "npb-a15-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Several_destinations_round_trip_and_edits_keep_their_ids()
    {
        var store = new FileDestinationStore(_root);
        store.Save(new PrintDestination("mostrador", "Mostrador", "Star BSC10", "Rollo 80 mm"));
        store.Save(new PrintDestination("cocina", "Cocina", "Láser", "A4", PrintOptions.ForPaper("A4")));
        store.Save(new PrintDestination("mostrador", "Barra", "Star BSC10", "Rollo 80 mm"));

        var reloaded = new FileDestinationStore(_root).List();
        Assert.Equal(2, reloaded.Count);
        Assert.Equal("Barra", reloaded.Single(item => item.Id == "mostrador").DisplayName);
        Assert.Equal("Cocina", reloaded.Single(item => item.Id == "cocina").DisplayName);
    }

    [Fact]
    public void Removing_a_destination_keeps_the_job_record()
    {
        var destinations = new FileDestinationStore(_root);
        destinations.Save(new PrintDestination("mostrador", "Mostrador", "Star BSC10", "A4"));
        var jobs = new FileJobRecordStore(_root);
        Assert.True(jobs.TryBegin(new PrintJob("trabajo-1", "application/pdf", @"C:\ticket.pdf", "mostrador", "A4"), "Star BSC10"));

        Assert.True(destinations.Remove("mostrador"));
        Assert.False(destinations.Remove("mostrador"));
        Assert.Empty(destinations.List());
        Assert.Equal("mostrador", jobs.Find("trabajo-1")!.DestinationId);
    }

    [Fact]
    public void Active_destination_does_not_fall_back_to_another_id()
    {
        var destinations = new List<PrintDestination>
        {
            new("uno", "Uno", "Cola A", "A4"),
            new("dos", "Dos", "Cola B", "A4"),
        };

        Assert.Equal("dos", ActiveDestinationPolicy.Resolve(new DesktopPreferences("dos", AppearancePreference.Light), destinations)!.Id);
        Assert.Null(ActiveDestinationPolicy.Resolve(new DesktopPreferences("ausente", AppearancePreference.Light), destinations));
        Assert.Null(ActiveDestinationPolicy.Resolve(DesktopPreferences.Default, destinations));
        Assert.Equal("uno", ActiveDestinationPolicy.Resolve(DesktopPreferences.Default, [destinations[0]])!.Id);
    }

    [Fact]
    public void Appearance_preference_reloads()
    {
        var store = new FilePreferenceStore(_root);
        Assert.Equal(AppearancePreference.Light, store.Load().Appearance);
        Assert.Null(store.Load().ActiveDestinationId);

        store.Save(new DesktopPreferences("mostrador", AppearancePreference.Graphite));
        var loaded = new FilePreferenceStore(_root).Load();
        Assert.Equal("mostrador", loaded.ActiveDestinationId);
        Assert.Equal(AppearancePreference.Graphite, loaded.Appearance);
    }

    [Fact]
    public void History_filters_by_phase_destination_and_search()
    {
        var older = Record("trabajo-a", "dest-a", "Cola A", @"C:\cocina.pdf", JobPhase.Failed, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var newer = Record("trabajo-b", "dest-b", "Cola B", @"C:\barra.pdf", JobPhase.Uncertain, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        var records = new[] { older, newer };

        Assert.Equal("trabajo-b", JobHistoryQuery.Apply(records, null, null, null)[0].Id);
        Assert.Equal("trabajo-a", Assert.Single(JobHistoryQuery.Apply(records, JobPhase.Failed, null, null)).Id);
        Assert.Equal("trabajo-b", Assert.Single(JobHistoryQuery.Apply(records, null, "dest-b", null)).Id);
        Assert.Equal("trabajo-a", Assert.Single(JobHistoryQuery.Apply(records, null, null, "cocina")).Id);
    }

    [Fact]
    public void Print_request_is_rejected_before_dispatch_when_options_are_invalid()
    {
        var read = PrintRequestReader.TryRead("A4", "cero", true, null, null, false, ScaleMode.ActualSize, null, out _, out var error);
        Assert.False(read);
        Assert.Contains("número entero", error, StringComparison.OrdinalIgnoreCase);

        var queue = new InstalledQueue("Cola", "Driver", ["A4"], MaximumCopies: 5, SupportsLandscape: true);
        var combined = new PrintOptions
        {
            PaperName = "A4",
            Copies = 1,
            ScaleMode = ScaleMode.ActualSize,
            ScalePercent = 90,
        };
        Assert.Contains("No se combina", PrintOptionRules.Check(combined, queue), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_second_begin_does_not_send_the_same_job_again()
    {
        var destinations = new FileDestinationStore(_root);
        var destination = new PrintDestination("dest", "Mostrador", "Cola", "A4", PrintOptions.ForPaper("A4"));
        destinations.Save(destination);
        var jobs = new FileJobRecordStore(_root);
        var port = new OncePort();
        var dispatcher = new PrintDispatcher(destinations, new ReadyCatalog(), jobs, new PassPreparer(), port);
        var job = new PrintJob("mismo", "application/pdf", @"C:\doc.pdf", "dest", "A4", PrintOptions.ForPaper("A4"));

        var first = await dispatcher.DispatchAsync(job);
        var second = await dispatcher.DispatchAsync(job);

        Assert.Equal(JobPhase.SubmittedToSpooler, first.Phase);
        Assert.Equal(PrintDispatcher.DuplicateDetail, second.Detail);
        Assert.Equal(1, port.Count);
    }

    [Fact]
    public void Missing_virtual_or_unreadable_queue_is_not_described_as_another_device()
    {
        var physical = new InstalledQueue("Star", "Star", [], Detail: QueueDetail.NameOnly);
        var unreadable = new InstalledQueue("Star", "Star", [], Detail: QueueDetail.Unreadable);
        var virtualQueue = new InstalledQueue(PhysicalQueueRules.VirtualQueueName, "Driver", [], Detail: QueueDetail.NameOnly);

        Assert.Equal(DevicePresentation.InstalledQueue, DevicePresentation.Status(physical, DeviceKind.Physical, true));
        Assert.Equal(DevicePresentation.CapabilitiesUnreadable, DevicePresentation.Status(unreadable, DeviceKind.Physical, true));
        Assert.Equal(DevicePresentation.VirtualRegistered, DevicePresentation.Status(virtualQueue, DeviceKind.Virtual, true));
        Assert.Equal(DevicePresentation.VirtualUnchecked, DevicePresentation.Status(null, DeviceKind.Virtual, false));
        Assert.Equal(HomeBanner.NeutralTitle, HomeBanner.Title(false, false, null, physical));
        Assert.Equal(HomeBanner.ReadyTitle, HomeBanner.Title(false, false, new PrintDestination("d", "D", "Star", "A4"), new InstalledQueue("Star", "Star", ["A4"])));
        Assert.Equal(HomeBanner.NeutralTitle, HomeBanner.Title(true, false, new PrintDestination("d", "D", "Star", "A4"), new InstalledQueue("Star", "Star", ["A4"])));
    }

    private static JobRecord Record(string id, string destinationId, string queue, string path, JobPhase phase, DateTimeOffset received) =>
        new()
        {
            Id = id,
            MediaType = "application/pdf",
            LocalPath = path,
            DestinationId = destinationId,
            QueueName = queue,
            Phase = phase,
            ReceivedAt = received,
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class ReadyCatalog : IPrinterCatalog
    {
        public InstalledQueue? Find(string queueName) => new(queueName, "Driver", ["A4"], MaximumCopies: 9, SupportsLandscape: true);

        public IReadOnlyList<InstalledQueue> List() => [];
    }

    private sealed class PassPreparer : IDocumentPreparer
    {
        public Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedDocument("application/pdf", "paginas", job.Id));
    }

    private sealed class OncePort : ISpoolerPort
    {
        public int Count { get; private set; }

        public Task SubmitAsync(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
