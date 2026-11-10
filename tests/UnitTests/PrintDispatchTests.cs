using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Tests;

public sealed class PrintDispatchTests
{
    private readonly MemoryDestinationStore _destinations = new();
    private readonly MemoryCatalog _catalog = new();
    private readonly MemoryJobStore _jobs = new();
    private readonly RecordingPreparer _preparer = new();
    private readonly RecordingPort _port = new();
    private readonly PrintDispatcher _dispatcher;

    public PrintDispatchTests()
    {
        _dispatcher = new PrintDispatcher(_destinations, _catalog, _jobs, _preparer, _port);
    }

    [Fact]
    public async Task Submits_a_pdf_once_to_the_named_queue()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));

        var result = await _dispatcher.DispatchAsync(Job("trabajo-001", "application/pdf", destination));

        Assert.Equal(JobPhase.SubmittedToSpooler, result.Phase);
        Assert.Null(_jobs.Find("trabajo-001")!.PhysicalConfirmation);
        var submission = Assert.Single(_port.Submissions);
        Assert.Equal("Star BSC10", submission.QueueName);
        Assert.Equal("Rollo 80 x 297 mm", submission.PaperName);
        Assert.Same(_preparer.Document, submission.Document);
    }

    [Fact]
    public async Task Clipping_confirmation_is_requested_only_before_printing_starts()
    {
        var destination = Destination("dest-star", "Star BSC10", "A4");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "A4"));
        _port.Exception = new SpoolerPortException(false, "Excede el ancho.", true);

        var result = await _dispatcher.DispatchAsync(Job("sin-recorte", "application/pdf", destination));

        Assert.True(result.RequiresClippingConfirmation);
        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.False(new SpoolerPortException(true, "Envío incierto", true).RequiresClippingConfirmation);
    }

    [Fact]
    public async Task Saved_defaults_cannot_authorize_clipping_for_new_jobs()
    {
        var destination = Destination("dest-star", "Star BSC10", "A4") with
        {
            Defaults = PrintOptions.ForPaper("A4") with { AllowClippingConfirmed = true },
        };
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "A4"));

        await _dispatcher.DispatchAsync(Job("sin-consentimiento", "application/pdf", destination));

        Assert.False(_jobs.Find("sin-consentimiento")!.Options!.AllowClippingConfirmed);
    }

    [Fact]
    public async Task Explicit_job_consent_is_persisted_without_changing_defaults()
    {
        var destination = Destination("dest-star", "Star BSC10", "A4");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "A4"));
        var job = Job("con-consentimiento", "application/pdf", destination) with
        {
            Options = PrintOptions.ForPaper("A4") with { AllowClippingConfirmed = true },
        };

        await _dispatcher.DispatchAsync(job);

        Assert.True(_jobs.Find(job.Id)!.Options!.AllowClippingConfirmed);
        Assert.Null(_destinations.Find(destination.Id)!.Defaults);
    }

    [Fact]
    public async Task Does_not_follow_a_different_queue_when_the_saved_one_is_missing()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Otra impresora", "Otro controlador", "A4"));

        var result = await _dispatcher.DispatchAsync(Job("trabajo-ausente", "application/pdf", destination));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Empty(_preparer.Calls);
        Assert.DoesNotContain("Otra impresora", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_jobs.Find("trabajo-ausente"));
    }

    [Theory]
    [InlineData("application/oxps")]
    [InlineData("image/png")]
    [InlineData("")]
    public async Task Rejects_unsupported_media_before_the_port(string mediaType)
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));

        var result = await _dispatcher.DispatchAsync(Job("trabajo-tipo", mediaType, destination));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Empty(_preparer.Calls);
    }

    [Theory]
    [InlineData("NPB - Prueba Virtual", "Star BSC10")]
    [InlineData("Cola fisica", "Microsoft Virtual Print Class Driver")]
    public async Task Rejects_the_virtual_queue_and_its_driver(string queueName, string driverName)
    {
        var destination = Destination("dest-virtual", queueName, "A4");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue(queueName, driverName, "A4"));

        var result = await _dispatcher.DispatchAsync(Job("trabajo-virtual", "application/pdf", destination));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Empty(_preparer.Calls);
    }

    [Fact]
    public async Task Rejects_paper_that_the_queue_does_not_announce()
    {
        var destination = Destination("dest-star", "Star BSC10", "A4");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));

        var result = await _dispatcher.DispatchAsync(Job("trabajo-papel", "application/pdf", destination));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Null(_jobs.Find("trabajo-papel"));
    }

    [Fact]
    public async Task Stops_a_second_send_of_the_same_identifier()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));
        var job = Job("trabajo-unico", "application/pdf", destination);

        await _dispatcher.DispatchAsync(job);
        var second = await _dispatcher.DispatchAsync(job);

        Assert.Equal(JobPhase.SubmittedToSpooler, second.Phase);
        Assert.Single(_port.Submissions);
        Assert.Contains("no se envía de nuevo", second.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Keeps_the_intent_when_preparation_fails()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));
        _preparer.Exception = new InvalidOperationException("El PDF no se puede preparar.");

        var result = await _dispatcher.DispatchAsync(Job("trabajo-prep", "application/pdf", destination));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        var record = _jobs.Find("trabajo-prep");
        Assert.NotNull(record);
        Assert.Equal(JobPhase.Failed, record.Phase);
        Assert.Equal("application/pdf", record.MediaType);
    }

    [Fact]
    public async Task Marks_a_failure_after_submission_starts_as_uncertain_without_retry()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));
        _port.Exception = new SpoolerPortException(started: true, "El spooler no confirmó el cierre.");

        var result = await _dispatcher.DispatchAsync(Job("trabajo-incierto", "application/pdf", destination));

        Assert.Equal(JobPhase.Uncertain, result.Phase);
        Assert.Single(_port.Submissions);
        Assert.Equal(JobPhase.Uncertain, _jobs.Find("trabajo-incierto")!.Phase);
        Assert.Null(_jobs.Find("trabajo-incierto")!.PhysicalConfirmation);
    }

    [Fact]
    public async Task Marks_a_port_failure_before_submission_as_failed()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));
        _port.Exception = new SpoolerPortException(started: false, "La cola rechazó la configuración.");

        var result = await _dispatcher.DispatchAsync(Job("trabajo-previo", "application/pdf", destination));

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Single(_port.Submissions);
        Assert.Equal(JobPhase.Failed, _jobs.Find("trabajo-previo")!.Phase);
    }

    [Fact]
    public async Task Dispatch_returns_before_the_spooler_finishes()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));
        _port.HoldOpen = true;

        var dispatch = _dispatcher.DispatchAsync(Job("trabajo-abierto", "application/pdf", destination));
        var signaled = await Task.WhenAny(_port.SubmitEntered.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(_port.SubmitEntered.Task, signaled);
        Assert.False(dispatch.IsCompleted);
        _port.ReleaseSubmit.TrySetResult();
        var result = await dispatch;
        Assert.Equal(JobPhase.SubmittedToSpooler, result.Phase);
        Assert.Single(_port.Submissions);
    }

    [Fact]
    public async Task Cancellation_before_submission_is_failed_and_is_not_retried()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _destinations.Save(destination);
        _catalog.Queues.Add(Queue("Star BSC10", "Star BSC10", "Rollo 80 x 297 mm"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await _dispatcher.DispatchAsync(Job("trabajo-cancelado", "application/pdf", destination), cancellation.Token);

        Assert.Equal(JobPhase.Failed, result.Phase);
        Assert.Empty(_port.Submissions);
        Assert.Equal(JobPhase.Failed, _jobs.Find("trabajo-cancelado")!.Phase);
    }

    [Fact]
    public void Records_physical_confirmation_without_printing_again()
    {
        _jobs.TryBegin(Job("trabajo-papel-visto", "application/pdf", Destination("dest-star", "Star BSC10", "A4")), "Star BSC10");
        _jobs.Update("trabajo-papel-visto", JobPhase.SubmittedToSpooler, "El spooler aceptó el trabajo.");

        _jobs.RecordPhysicalConfirmation("trabajo-papel-visto", "Quien opera vio el papel.");

        Assert.Empty(_port.Submissions);
        Assert.Equal(JobPhase.SubmittedToSpooler, _jobs.Find("trabajo-papel-visto")!.Phase);
        Assert.Equal("Quien opera vio el papel.", _jobs.Find("trabajo-papel-visto")!.PhysicalConfirmation);
    }

    [Fact]
    public void Recovery_marks_an_interrupted_job_uncertain_and_does_not_send_it_again()
    {
        var destination = Destination("dest-star", "Star BSC10", "Rollo 80 x 297 mm");
        _jobs.TryBegin(Job("trabajo-cortado", "application/pdf", destination), "Star BSC10");
        _jobs.Update("trabajo-cortado", JobPhase.Prepared, "Páginas listas.");

        JobRecovery.MarkInterruptedAsUncertain(_jobs);

        Assert.Equal(JobPhase.Uncertain, _jobs.Find("trabajo-cortado")!.Phase);
        Assert.False(_jobs.TryBegin(Job("trabajo-cortado", "application/pdf", destination), "Star BSC10"));
        Assert.Empty(_port.Submissions);
    }

    private static PrintDestination Destination(string id, string queueName, string paperName) =>
        new(id, queueName, queueName, paperName);

    private static PrintJob Job(string id, string mediaType, PrintDestination destination) =>
        new(id, mediaType, $"C:\\documentos\\{id}.pdf", destination.Id, destination.PaperName);

    private static InstalledQueue Queue(string name, string driver, params string[] papers) =>
        new(name, driver, papers);

    private sealed class MemoryDestinationStore : IDestinationStore
    {
        private readonly Dictionary<string, PrintDestination> _items = new(StringComparer.Ordinal);

        public void Save(PrintDestination destination) => _items[destination.Id] = destination;

        public bool Remove(string id) => _items.Remove(id);

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

        public IReadOnlyList<JobRecord> List() => _items.Values.OrderBy(record => record.ReceivedAt).ToList();

        public IReadOnlyList<string> ListIds() => _items.Keys.ToList();
    }

    private sealed class RecordingPreparer : IDocumentPreparer
    {
        public PreparedDocument Document { get; } = new("application/pdf", "paginas");

        public List<string> Calls { get; } = [];

        public Exception? Exception { get; set; }

        public Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken)
        {
            Calls.Add(job.Id);
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Document);
        }
    }

    private sealed class RecordingPort : ISpoolerPort
    {
        public List<Submission> Submissions { get; } = [];

        public Exception? Exception { get; set; }

        public bool HoldOpen { get; set; }

        public TaskCompletionSource SubmitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSubmit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SubmitAsync(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken)
        {
            Submissions.Add(new Submission(document, queueName, paperName));
            SubmitEntered.TrySetResult();
            if (HoldOpen)
            {
                await ReleaseSubmit.Task;
            }

            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }

    private sealed record Submission(PreparedDocument Document, string QueueName, string? PaperName);
}
