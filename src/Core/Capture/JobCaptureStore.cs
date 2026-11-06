using System.Text.Json;

namespace Neobash.PrintBridge.Core.Capture;

public sealed class JobCaptureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IJobFileStore _store;
    private readonly IJobIdGenerator _ids;

    public JobCaptureStore(IJobFileStore store, IJobIdGenerator? ids = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ids = ids ?? new TimeGuidJobIdGenerator();
    }

    public async Task<JobCaptureResult> CaptureAsync(
        JobCaptureRequest request,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        if (cancellationToken.IsCancellationRequested)
        {
            return new JobCaptureResult
            {
                Status = JobCaptureStatus.Canceled,
                Error = "La captura se canceló antes de escribir el documento."
            };
        }

        string jobId;
        try
        {
            jobId = _ids.Create();
            JobIdRules.EnsureSafe(jobId);
        }
        catch (Exception exception)
        {
            return new JobCaptureResult
            {
                Status = JobCaptureStatus.Failed,
                Error = exception.Message
            };
        }

        IJobWorkspace? workspace = null;
        string? documentName = null;
        try
        {
            workspace = _store.Open(jobId);
            documentName = DocumentName(request.ContentType);
            var documentPath = Path.Combine(workspace.Location, documentName);
            long total = 0;

            await using (var destination = workspace.CreateExclusive(documentName))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var size = new FileInfo(documentPath).Length;
            if (size <= 0 || size != total)
            {
                return Finish(
                    workspace,
                    request,
                    jobId,
                    documentName,
                    size,
                    JobCaptureStatus.Failed,
                    "El documento no se cerró con contenido.");
            }

            return Finish(workspace, request, jobId, documentName, size, JobCaptureStatus.Succeeded, null);
        }
        catch (OperationCanceledException)
        {
            return Finish(
                workspace,
                request,
                jobId,
                documentName ?? DocumentName(request.ContentType),
                workspace is null || documentName is null ? 0 : SafeLength(workspace, documentName),
                JobCaptureStatus.Canceled,
                "La captura se canceló.");
        }
        catch (Exception exception)
        {
            return Finish(
                workspace,
                request,
                jobId,
                documentName ?? DocumentName(request.ContentType),
                workspace is null || documentName is null ? 0 : SafeLength(workspace, documentName),
                JobCaptureStatus.Failed,
                exception.Message);
        }
    }

    private static JobCaptureResult Finish(
        IJobWorkspace? workspace,
        JobCaptureRequest request,
        string jobId,
        string documentName,
        long size,
        JobCaptureStatus status,
        string? error)
    {
        if (workspace is null)
        {
            return new JobCaptureResult
            {
                JobId = jobId,
                Status = status,
                SizeBytes = size,
                Error = error
            };
        }

        var metadata = new JobMetadata
        {
            JobId = jobId,
            ReceivedAt = request.ReceivedAt ?? DateTimeOffset.UtcNow,
            PrinterName = request.PrinterName,
            PrinterUri = request.PrinterUri,
            SourceApplication = request.SourceApplication,
            ContentType = request.ContentType,
            SizeBytes = size,
            Status = status.ToString(),
            DocumentFileName = documentName,
            Error = error
        };

        try
        {
            workspace.WriteText("metadata.json", JsonSerializer.Serialize(metadata, JsonOptions));
            if (error is not null)
            {
                workspace.WriteText("error.txt", error);
            }
        }
        catch (Exception exception)
        {
            return new JobCaptureResult
            {
                JobId = jobId,
                Status = JobCaptureStatus.Failed,
                DirectoryPath = workspace.Location,
                DocumentPath = Path.Combine(workspace.Location, documentName),
                SizeBytes = size,
                Error = exception.Message
            };
        }

        return new JobCaptureResult
        {
            JobId = jobId,
            Status = status,
            DirectoryPath = workspace.Location,
            DocumentPath = Path.Combine(workspace.Location, documentName),
            SizeBytes = size,
            Error = error
        };
    }

    private static long SafeLength(IJobWorkspace workspace, string documentName)
    {
        var path = Path.Combine(workspace.Location, documentName);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static string DocumentName(string contentType) =>
        string.Equals(contentType, "application/oxps", StringComparison.OrdinalIgnoreCase)
            ? "document.oxps"
            : "document.bin";
}
