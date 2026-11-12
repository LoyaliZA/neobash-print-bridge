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

    public Task<DispatchResult> DispatchAsync(PrintJob job, CancellationToken cancellationToken = default) =>
        Task.Run(() => ExecuteAsync(job, cancellationToken));

    private async Task<DispatchResult> ExecuteAsync(PrintJob job, CancellationToken cancellationToken)
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

        if (LocalSetup.DriverChanged(destination, queue))
        {
            return new DispatchResult(JobPhase.Failed, "El controlador de la cola cambió. Confirma de nuevo la impresora y el papel antes de enviar.");
        }

        var options = job.Options ?? destination.Defaults ?? PrintOptions.ForPaper(destination.PaperName ?? "");
        // Saved defaults cannot authorize clipping for a new job.
        options = options with { AllowClippingConfirmed = job.Options?.AllowClippingConfirmed == true };
        var optionError = PrintOptionRules.Check(options, queue);
        if (optionError is not null)
        {
            return new DispatchResult(JobPhase.Failed, optionError);
        }

        if (!string.IsNullOrWhiteSpace(job.PaperName)
            && !string.Equals(job.PaperName, options.PaperName, StringComparison.OrdinalIgnoreCase))
        {
            return new DispatchResult(JobPhase.Failed, "El papel del trabajo no coincide con las opciones.");
        }

        var effective = job with { PaperName = options.PaperName, Options = options };
        if (!_jobs.TryBegin(effective, queue.QueueName))
        {
            var existing = _jobs.Find(effective.Id);
            return new DispatchResult(existing?.Phase ?? JobPhase.Failed, DuplicateDetail);
        }

        PreparedDocument document;
        try
        {
            document = await _preparer.PrepareAsync(effective, cancellationToken).ConfigureAwait(false);
            _jobs.Update(effective.Id, JobPhase.Prepared, "El documento quedó preparado.");
        }
        catch (Exception exception)
        {
            _jobs.Update(effective.Id, JobPhase.Failed, exception.Message);
            return new DispatchResult(JobPhase.Failed, exception.Message);
        }

        try
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException exception)
            {
                const string cancelled = "La espera se canceló antes de entregar el trabajo al spooler.";
                _jobs.Update(effective.Id, JobPhase.Failed, cancelled);
                return new DispatchResult(JobPhase.Failed, string.IsNullOrWhiteSpace(exception.Message) ? cancelled : cancelled + " " + exception.Message);
            }

            await _spooler.SubmitAsync(document, queue.QueueName, effective.PaperName, cancellationToken).ConfigureAwait(false);
        }
        catch (SpoolerPortException exception) when (!exception.Started)
        {
            _jobs.Update(effective.Id, JobPhase.Failed, exception.Message);
            return new DispatchResult(JobPhase.Failed, exception.Message, exception.RequiresClippingConfirmation);
        }
        catch (Exception exception)
        {
            _jobs.Update(effective.Id, JobPhase.Uncertain, exception.Message);
            return new DispatchResult(JobPhase.Uncertain, exception.Message);
        }

        const string accepted = "El spooler aceptó el trabajo. Eso no confirma el papel.";
        _jobs.Update(effective.Id, JobPhase.SubmittedToSpooler, accepted);
        return new DispatchResult(JobPhase.SubmittedToSpooler, accepted);
    }
}
