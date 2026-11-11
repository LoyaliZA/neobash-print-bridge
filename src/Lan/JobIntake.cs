using System.Security.Cryptography;
using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Lan;

public enum TransferState
{
    Aceptada,
    Rechazada,
    Conflicto,
}

public sealed record JobSubmission(string SourceId, string DestinationId, string MediaType, string? QueueName, long? DeclaredLength);

public sealed record IntakeResult(TransferState Transfer, string? JobId, JobPhase? Phase, string Detail);

public sealed class JobIntake
{
    private readonly AgentDatabase _database;
    private readonly PrintDispatcher _dispatcher;
    private readonly Func<DateTimeOffset> _now;

    public JobIntake(AgentDatabase database, PrintDispatcher dispatcher, Func<DateTimeOffset>? now = null)
    {
        _database = database;
        _dispatcher = dispatcher;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<IntakeResult> AcceptAsync(DeviceRecord device, JobSubmission submission, Stream body, CancellationToken cancellationToken)
    {
        if (device.Revoked)
        {
            return Reject(device, submission, null, "revocado", "El dispositivo está revocado.");
        }

        if (!string.IsNullOrWhiteSpace(submission.QueueName))
        {
            return Reject(device, submission, null, "cola", "La petición no puede nombrar una cola de Windows. Hay que enviar el identificador del destino publicado.");
        }

        if (!IsPdf(submission.MediaType))
        {
            return Reject(device, submission, null, "tipo", "El tipo de documento no está admitido. Esta entrega solo prepara application/pdf.");
        }

        if (!IsSafeSource(submission.SourceId))
        {
            return Reject(device, submission, null, "origen", "El identificador de origen no es seguro.");
        }

        var destination = _database.FindPublished(submission.DestinationId);
        if (destination is null || !_database.IsGranted(device.Id, submission.DestinationId))
        {
            return Reject(device, submission, null, "destino", "El destino no está publicado para este dispositivo.");
        }

        if (submission.DeclaredLength is > AgentDatabase.MaxBodyBytes)
        {
            return Reject(device, submission, submission.DeclaredLength, "tamano", "El PDF supera 32 MiB.");
        }

        TempFile temp;
        try
        {
            temp = await WriteTempAsync(body, submission.DeclaredLength, cancellationToken).ConfigureAwait(false);
        }
        catch (TransferRejectedException exception)
        {
            return Reject(device, submission, exception.Size, exception.Code, exception.Message);
        }

        var jobId = "j" + Guid.NewGuid().ToString("N");
        string published;
        try
        {
            published = _database.Publish(temp.Path, jobId);
        }
        catch
        {
            _database.DeleteFile(temp.Path);
            throw;
        }

        var options = PrintOptions.ForPaper(destination.PaperName);
        CommitOutcome outcome;
        try
        {
            outcome = _database.TryCommit(
            device.Id,
            submission.SourceId,
            temp.Sha256,
            destination.Id,
            jobId,
            published,
            PhysicalQueueRules.PdfMediaType,
            destination.QueueName,
            destination.PaperName,
            options,
            _now());
        }
        catch
        {
            _database.DeleteIfUnreferenced(published);
            throw;
        }

        if (outcome.Kind != CommitKind.Created)
        {
            _database.DeleteIfUnreferenced(published);
            return outcome.Kind switch
            {
                CommitKind.Replay => Accept(device, submission, temp.Length, outcome.JobId, outcome.Phase, "La clave ya estaba aceptada. Se devuelve la fase guardada.", dispatched: false),
                CommitKind.Conflict => Conflict(device, submission, temp.Length, outcome.JobId),
                CommitKind.RateLimited => Reject(device, submission, temp.Length, "frecuencia", "Se alcanzó el máximo de 30 trabajos aceptados en 10 minutos."),
                _ => Reject(device, submission, temp.Length, "destino", "El destino no está publicado para este dispositivo."),
            };
        }

        _database.ArmDispatch(jobId);
        var printJob = new PrintJob(jobId, PhysicalQueueRules.PdfMediaType, published, destination.Id, destination.PaperName, options);
        DispatchResult dispatched;
        try
        {
            dispatched = await _dispatcher.DispatchAsync(printJob, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _database.Update(jobId, JobPhase.Uncertain, JobRecovery.InterruptedDetail);
            _database.Audit("despacho", device.Id, destination.Id, submission.SourceId, jobId, temp.Length, nameof(JobPhase.Uncertain), exception.GetType().Name);
            var uncertain = _database.Find(jobId);
            return new IntakeResult(TransferState.Aceptada, jobId, uncertain?.Phase ?? JobPhase.Uncertain, JobRecovery.InterruptedDetail);
        }

        var stored = _database.Find(jobId);
        if (stored is { Phase: JobPhase.Received }
            && dispatched.Phase == JobPhase.Failed
            && dispatched.Detail != PrintDispatcher.DuplicateDetail)
        {
            _database.Update(jobId, JobPhase.Failed, dispatched.Detail);
            stored = _database.Find(jobId);
        }

        return Accept(device, submission, temp.Length, jobId, stored?.Phase ?? dispatched.Phase, stored?.Detail ?? dispatched.Detail, dispatched: true);
    }

    public static bool IsSafeSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            return false;
        }

        try
        {
            JobIdRules.EnsureSafe(sourceId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsPdf(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        var type = mediaType.Split(';', 2)[0].Trim();
        return string.Equals(type, PhysicalQueueRules.PdfMediaType, StringComparison.OrdinalIgnoreCase);
    }

    private IntakeResult Reject(DeviceRecord device, JobSubmission submission, long? size, string code, string detail)
    {
        _database.Audit("transferencia", device.Id, submission.DestinationId, submission.SourceId, null, size, null, code);
        return new IntakeResult(TransferState.Rechazada, null, null, detail);
    }

    private IntakeResult Conflict(DeviceRecord device, JobSubmission submission, long size, string? jobId)
    {
        _database.Audit("transferencia", device.Id, submission.DestinationId, submission.SourceId, jobId, size, null, "conflicto");
        return new IntakeResult(TransferState.Conflicto, jobId, null, "Esa clave ya tiene otro documento o destino.");
    }

    private IntakeResult Accept(DeviceRecord device, JobSubmission submission, long size, string? jobId, JobPhase? phase, string detail, bool dispatched)
    {
        _database.Audit("transferencia", device.Id, submission.DestinationId, submission.SourceId, jobId, size, phase?.ToString(), dispatched ? "aceptada" : "repetida");
        return new IntakeResult(TransferState.Aceptada, jobId, phase, detail);
    }

    private async Task<TempFile> WriteTempAsync(Stream body, long? declaredLength, CancellationToken cancellationToken)
    {
        var path = _database.NewTempPath();
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read;
                try
                {
                    read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException)
                {
                    throw new TransferRejectedException("incompleta", "La transferencia quedó incompleta. No se aceptó el trabajo.", total);
                }

                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > AgentDatabase.MaxBodyBytes)
                {
                    throw new TransferRejectedException("tamano", "El PDF supera 32 MiB.", total);
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer.AsSpan(0, read));
            }

            if (total == 0)
            {
                throw new TransferRejectedException("incompleta", "La transferencia quedó incompleta. No se aceptó el trabajo.", total);
            }

            if (declaredLength is long expected && expected != total)
            {
                throw new TransferRejectedException("incompleta", "La transferencia quedó incompleta. No se aceptó el trabajo.", total);
            }

            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                file.Flush(true);
            }
            catch (IOException)
            {
                file.Flush();
            }

            return new TempFile(path, total, Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch
        {
            _database.DeleteFile(path);
            throw;
        }
    }

    private sealed record TempFile(string Path, long Length, string Sha256);

    private sealed class TransferRejectedException(string code, string message, long size) : Exception(message)
    {
        public string Code { get; } = code;

        public long Size { get; } = size;
    }
}
