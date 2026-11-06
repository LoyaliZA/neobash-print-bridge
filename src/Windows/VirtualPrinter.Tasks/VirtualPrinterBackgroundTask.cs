using System.Runtime.InteropServices.WindowsRuntime;
using Neobash.PrintBridge.Core.Capture;
using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;

namespace Neobash.PrintBridge.VirtualPrinter.Tasks;

public sealed class VirtualPrinterBackgroundTask : IBackgroundTask
{
    public const string PrinterDisplayName = "NPB - Prueba Virtual";
    public const string PrinterUri = "neobash-print-bridge:npb-prueba-virtual";

    private readonly VirtualPrinterJobCompletion _completion = new();
    private BackgroundTaskDeferral? _deferral;
    private IDiagnosticLog? _log;
    private JobCaptureStore? _store;
    private int _owner;

    public void Run(IBackgroundTaskInstance taskInstance)
    {
        _deferral = taskInstance.GetDeferral();
        taskInstance.Canceled += OnCanceled;

        try
        {
            var localFolder = ApplicationData.Current.LocalFolder.Path;
            _log = new FileDiagnosticLog(Path.Combine(localFolder, "Neobash", "PrintBridge", "Logs", "task.log"));
            _store = new JobCaptureStore(new FileSystemJobFileStore(Path.Combine(localFolder, "Neobash", "PrintBridge", "Jobs")));

            if (taskInstance.TriggerDetails is not PrintWorkflowVirtualPrinterTriggerDetails details)
            {
                _log.Write("La activación no incluye una sesión de impresora virtual.");
                FinishDeferral();
                return;
            }

            var session = details.VirtualPrinterSession;
            session.VirtualPrinterDataAvailable += OnDataAvailable;
            session.Start();
            _log.Write("Sesión de impresora virtual iniciada.");
        }
        catch (Exception exception)
        {
            _log?.Write("Error al iniciar la tarea: " + exception);
            FinishDeferral();
        }
    }

    private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
    {
        _completion.MarkCanceled();
        _log?.Write("Cancelación recibida: " + reason);
        if (Interlocked.CompareExchange(ref _owner, 2, 0) == 0)
        {
            FinishDeferral();
        }
    }

    private async void OnDataAvailable(PrintWorkflowVirtualPrinterSession sender, PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
    {
        Interlocked.CompareExchange(ref _owner, 1, 0);
        var captureStatus = JobCaptureStatus.Failed;

        try
        {
            if (_completion.IsCanceled)
            {
                captureStatus = JobCaptureStatus.Canceled;
                _log?.Write("El trabajo llegó después de la cancelación.");
                return;
            }

            if (_store is null)
            {
                _log?.Write("El almacén de trabajos no está disponible.");
                return;
            }

            var source = args.SourceContent;
            var contentType = string.IsNullOrWhiteSpace(source?.ContentType) ? "application/octet-stream" : source.ContentType;
            var input = source?.GetInputStream();
            if (input is null)
            {
                _log?.Write("El trabajo no incluye un flujo de entrada.");
                return;
            }

            using var stream = input.AsStreamForRead();
            var result = await _store.CaptureAsync(new JobCaptureRequest
            {
                PrinterName = PrinterDisplayName,
                PrinterUri = TryPrinterUri(sender) ?? PrinterUri,
                SourceApplication = TrySourceApplication(args),
                ContentType = contentType,
                ReceivedAt = DateTimeOffset.UtcNow
            }, stream);

            captureStatus = result.Status;
            _log?.Write($"Captura {result.JobId} con estado {result.Status}. {result.Error}");
        }
        catch (Exception exception)
        {
            captureStatus = JobCaptureStatus.Failed;
            _log?.Write("Error al capturar el trabajo: " + exception);
        }
        finally
        {
            var outcome = _completion.Resolve(captureStatus);
            if (outcome != PrintJobOutcome.AlreadyCompleted)
            {
                try
                {
                    args.CompleteJob(Map(outcome));
                    _log?.Write("Trabajo finalizado con " + outcome);
                }
                catch (Exception exception)
                {
                    _log?.Write("No se pudo finalizar el trabajo: " + exception);
                }
            }

            FinishDeferral();
        }
    }

    private static PrintWorkflowSubmittedStatus Map(PrintJobOutcome outcome) => outcome switch
    {
        PrintJobOutcome.Succeeded => PrintWorkflowSubmittedStatus.Succeeded,
        PrintJobOutcome.Canceled => PrintWorkflowSubmittedStatus.Canceled,
        _ => PrintWorkflowSubmittedStatus.Failed
    };

    private static string? TryPrinterUri(PrintWorkflowVirtualPrinterSession session)
    {
        try
        {
            return session.Printer?.PrinterUri?.AbsoluteUri;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TrySourceApplication(PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
    {
        try
        {
            var configuration = args.Configuration;
            var property = configuration?.GetType().GetProperty("SourceAppDisplayName");
            return property?.GetValue(configuration) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void FinishDeferral()
    {
        if (_completion.TryCompleteDeferral())
        {
            _deferral?.Complete();
        }
    }
}
