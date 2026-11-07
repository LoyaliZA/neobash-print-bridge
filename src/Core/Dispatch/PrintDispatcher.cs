namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class PrintDispatcher
{
    public const string DuplicateDetail = "El identificador ya fue registrado y no se envía de nuevo.";

    private readonly IDestinationStore _destinations;
    private readonly IPrinterCatalog _catalog;
    private readonly IJobRecordStore _jobs;
    private readonly IDocumentPreparer _preparer;
    private readonly ISpoolerPort _spooler;

    public PrintDispatcher(
        IDestinationStore destinations,
        IPrinterCatalog catalog,
        IJobRecordStore jobs,
        IDocumentPreparer preparer,
        ISpoolerPort spooler)
    {
        _destinations = destinations;
        _catalog = catalog;
        _jobs = jobs;
        _preparer = preparer;
        _spooler = spooler;
    }

    public async Task<DispatchResult> DispatchAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        var destination = _destinations.Find(job.DestinationId);
        if (destination is null)
        {
            return new DispatchResult(JobPhase.Failed, "El destino no está guardado.");
        }

        if (!PhysicalQueueRules.IsPdf(job.MediaType))
        {
            return new DispatchResult(JobPhase.Failed, "El tipo de documento no está admitido. Esta entrega solo prepara application/pdf.");
        }

        var queue = _catalog.Find(destination.QueueName);
        if (queue is null)
        {
            return new DispatchResult(JobPhase.Failed, $"La cola «{destination.QueueName}» no está disponible.");
        }

        if (PhysicalQueueRules.IsVirtual(queue))
        {
            return new DispatchResult(JobPhase.Failed, "Esa cola es virtual y no puede ser destino físico.");
        }

        if (!AnnouncesPaper(queue, destination.PaperName) || !SamePaper(job.PaperName, destination.PaperName))
        {
            return new DispatchResult(JobPhase.Failed, "El papel elegido no está entre los que anuncia esa cola.");
        }

        if (!_jobs.TryBegin(job, queue.QueueName))
        {
            var existing = _jobs.Find(job.Id);
            return new DispatchResult(existing?.Phase ?? JobPhase.Failed, DuplicateDetail);
        }

        PreparedDocument document;
        try
        {
            document = await _preparer.PrepareAsync(job, cancellationToken).ConfigureAwait(false);
            _jobs.Update(job.Id, JobPhase.Prepared, "El documento quedó preparado.");
        }
        catch (Exception exception)
        {
            _jobs.Update(job.Id, JobPhase.Failed, exception.Message);
            return new DispatchResult(JobPhase.Failed, exception.Message);
        }

        try
        {
            _spooler.Submit(document, queue.QueueName, destination.PaperName, cancellationToken);
        }
        catch (SpoolerPortException exception) when (!exception.Started)
        {
            _jobs.Update(job.Id, JobPhase.Failed, exception.Message);
            return new DispatchResult(JobPhase.Failed, exception.Message);
        }
        catch (Exception exception)
        {
            _jobs.Update(job.Id, JobPhase.Uncertain, exception.Message);
            return new DispatchResult(JobPhase.Uncertain, exception.Message);
        }

        const string accepted = "El spooler aceptó el trabajo. Eso no confirma el papel.";
        _jobs.Update(job.Id, JobPhase.SubmittedToSpooler, accepted);
        return new DispatchResult(JobPhase.SubmittedToSpooler, accepted);
    }

    private static bool AnnouncesPaper(InstalledQueue queue, string? paperName) =>
        !string.IsNullOrWhiteSpace(paperName)
        && queue.PaperNames.Any(paper => string.Equals(paper, paperName, StringComparison.OrdinalIgnoreCase));

    private static bool SamePaper(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
