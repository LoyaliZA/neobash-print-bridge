using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Tests;

public sealed class PrintOptionTests
{
    private readonly MemoryDestinationStore _destinations = new();
    private readonly MemoryCatalog _catalog = new();
    private readonly MemoryJobStore _jobs = new();
    private readonly RecordingPreparer _preparer = new();
    private readonly RecordingPort _port = new();
    private readonly PrintDispatcher _dispatcher;

    public PrintOptionTests()
    {
        _dispatcher = new PrintDispatcher(_destinations, _catalog, _jobs, _preparer, _port);
    }

    [Fact]
    public async Task Job_options_override_destination_defaults_and_are_stored()
    {
        var defaults = PrintOptions.ForPaper("A4");
        var destination = new PrintDestination("dest-star", "Star de mostrador", "Star BSC10", "A4", defaults);
        _destinations.Save(destination);
        _catalog.Queues.Add(new InstalledQueue(
            "Star BSC10",
            "Star BSC10",
            ["A4", "80mm x Receipt (48 Columns)"],
            MaximumCopies: 1,
            SupportsLandscape: false));
        var requested = new PrintOptions
        {
            PaperName = "80mm x Receipt (48 Columns)",
            Copies = 1,
            Range = null,
            Orientation = PageOrientation.Portrait,
            ScaleMode = ScaleMode.Percentage,
            ScalePercent = 90,
            PagesPerSheet = 1,
        };

        var result = await _dispatcher.DispatchAsync(Job(destination, requested));

        Assert.Equal(JobPhase.SubmittedToSpooler, result.Phase);
        Assert.Equal("80mm x Receipt (48 Columns)", Assert.Single(_port.Submissions).PaperName);
        var stored = _jobs.Find("trabajo-opciones");
        Assert.NotNull(stored);
        Assert.Equal(ScaleMode.Percentage, stored.Options!.ScaleMode);
        Assert.Equal(90, stored.Options.ScalePercent);
        Assert.Equal(ScaleMode.ActualSize, _destinations.Find("dest-star")!.Defaults!.ScaleMode);
    }

    [Fact]
    public async Task Unsupported_pages_per_sheet_never_reaches_the_port()
    {
        var destination = new PrintDestination("dest-star", "Star", "Star BSC10", "A4");
        _destinations.Save(destination);
        _catalog.Queues.Add(new InstalledQueue("Star BSC10", "Star BSC10", ["A4"], MaximumCopies: 5, SupportsLandscape: true));

        var result = await _dispatcher.DispatchAsync(Job(destination, new PrintOptions
        {
            PaperName = "A4",
            Copies = 1,
            PagesPerSheet = 2,
        }));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Contains("una página por hoja", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Copies_above_the_queue_maximum_are_rejected()
    {
        var destination = new PrintDestination("dest-star", "Star", "Star BSC10", "A4");
        _destinations.Save(destination);
        _catalog.Queues.Add(new InstalledQueue("Star BSC10", "Star BSC10", ["A4"], MaximumCopies: 1));

        var result = await _dispatcher.DispatchAsync(Job(destination, new PrintOptions
        {
            PaperName = "A4",
            Copies = 2,
        }));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Contains("máximo 1", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Range_outside_the_document_is_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PageSelection.Select(3, new PageRange(2, 4)));

        Assert.Contains("no está dentro", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Percentage_combined_with_actual_size_is_rejected()
    {
        var queue = new InstalledQueue("Star BSC10", "Star BSC10", ["A4"], MaximumCopies: 1);
        var error = PrintOptionRules.Check(new PrintOptions
        {
            PaperName = "A4",
            Copies = 1,
            ScaleMode = ScaleMode.ActualSize,
            ScalePercent = 90,
        }, queue);

        Assert.Contains("No se combina", error, StringComparison.OrdinalIgnoreCase);
    }

    private static PrintJob Job(PrintDestination destination, PrintOptions options) =>
        new("trabajo-opciones", "application/pdf", @"C:\documentos\muestra.pdf", destination.Id, options.PaperName, options);

    private sealed class MemoryDestinationStore : IDestinationStore
    {
        private readonly Dictionary<string, PrintDestination> _items = new(StringComparer.Ordinal);

        public void Save(PrintDestination destination) => _items[destination.Id] = destination;

        public PrintDestination? Find(string id) => _items.TryGetValue(id, out var destination) ? destination : null;

        public IReadOnlyList<PrintDestination> List() => _items.Values.ToList();
    }

    private sealed class MemoryCatalog : IPrinterCatalog
    {
        public List<InstalledQueue> Queues { get; } = [];

        public InstalledQueue? Find(string queueName) =>
            Queues.FirstOrDefault(queue => string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<InstalledQueue> List() => Queues;
    }

    private sealed class MemoryJobStore : IJobRecordStore
    {
        private readonly Dictionary<string, JobRecord> _items = new(StringComparer.Ordinal);

        public bool TryBegin(PrintJob job, string queueName)
        {
            if (_items.ContainsKey(job.Id))
            {
                return false;
            }

            _items[job.Id] = new JobRecord
            {
                Id = job.Id,
                MediaType = job.MediaType,
                LocalPath = job.LocalPath,
                DestinationId = job.DestinationId,
                PaperName = job.PaperName,
                Options = job.Options,
                QueueName = queueName,
                Phase = JobPhase.Received,
                ReceivedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }

        public void Update(string id, JobPhase phase, string? detail)
        {
            var record = _items[id];
            record.Phase = phase;
            record.Detail = detail;
        }

        public void RecordPhysicalConfirmation(string id, string note) => _items[id].PhysicalConfirmation = note;

        public JobRecord? Find(string id) => _items.TryGetValue(id, out var record) ? record : null;

        public IReadOnlyList<JobRecord> List() => _items.Values.ToList();
    }

    private sealed class RecordingPreparer : IDocumentPreparer
    {
        public Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedDocument("application/pdf", "paginas", job.Id));
    }

    private sealed class RecordingPort : ISpoolerPort
    {
        public List<(string QueueName, string? PaperName)> Submissions { get; } = [];

        public void Submit(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken) =>
            Submissions.Add((queueName, paperName));
    }
}
