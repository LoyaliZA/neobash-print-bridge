using Neobash.PrintBridge.Core.Capture;
using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.PrintSupport;
using Windows.Storage;

namespace Neobash.PrintBridge.VirtualPrinter.Tasks;

public sealed class PrintSupportExtensionBackgroundTask : IBackgroundTask
{
    private readonly object _lifetime = new();
    private BackgroundTaskDeferral? _deferral;
    private IDiagnosticLog? _log;
    private int _activeHandlers;
    private bool _canceled;

    public void Run(IBackgroundTaskInstance taskInstance)
    {
        _deferral = taskInstance.GetDeferral();
        taskInstance.Canceled += OnCanceled;

        try
        {
            _log = CreateLog();
            if (taskInstance.TriggerDetails is not PrintSupportExtensionTriggerDetails details || details.Session is null)
            {
                _log?.Write("La activación no incluye una sesión de extensión de impresión.");
                CompleteDeferral();
                return;
            }

            var session = details.Session;
            session.PrintTicketValidationRequested += OnPrintTicketValidationRequested;
            session.Start();
            _log?.Write("Sesión de extensión de impresión iniciada.");
        }
        catch (Exception exception)
        {
            _log?.Write("Error al iniciar la extensión de impresión: " + exception);
            CompleteDeferral();
        }
    }

    private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
    {
        _log?.Write("Cancelación de la extensión de impresión: " + reason);
        lock (_lifetime)
        {
            _canceled = true;
            if (_activeHandlers == 0)
            {
                CompleteDeferral();
            }
        }
    }

    private void OnPrintTicketValidationRequested(PrintSupportExtensionSession sender, PrintSupportPrintTicketValidationRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        lock (_lifetime)
        {
            _activeHandlers++;
            if (_canceled)
            {
                _log?.Write("La validación del PrintTicket llegó después de la cancelación.");
            }
        }

        try
        {
            if (IsCanceled)
            {
                return;
            }

            var xml = args.PrintTicket?.XmlNode?.GetXml();
            var valid = VirtualPrinterPrintTicket.IsValid(xml);
            args.SetPrintTicketValidationStatus(valid
                ? WorkflowPrintTicketValidationStatus.Resolved
                : WorkflowPrintTicketValidationStatus.Invalid);
            _log?.Write(valid ? "PrintTicket resuelto." : "PrintTicket no válido.");
        }
        catch (Exception exception)
        {
            _log?.Write("Error al validar el PrintTicket: " + exception);
            TrySetInvalid(args);
        }
        finally
        {
            try
            {
                deferral.Complete();
            }
            catch (Exception exception)
            {
                _log?.Write("No se pudo cerrar el aplazamiento de la validación: " + exception);
            }

            lock (_lifetime)
            {
                _activeHandlers--;
                if (_canceled && _activeHandlers == 0)
                {
                    CompleteDeferral();
                }
            }
        }
    }

    private bool IsCanceled
    {
        get
        {
            lock (_lifetime)
            {
                return _canceled;
            }
        }
    }

    private void TrySetInvalid(PrintSupportPrintTicketValidationRequestedEventArgs args)
    {
        if (IsCanceled)
        {
            return;
        }

        try
        {
            args.SetPrintTicketValidationStatus(WorkflowPrintTicketValidationStatus.Invalid);
        }
        catch (Exception exception)
        {
            _log?.Write("No se pudo informar el PrintTicket no válido: " + exception);
        }
    }

    private static IDiagnosticLog? CreateLog()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder.Path;
            return new FileDiagnosticLog(Path.Combine(localFolder, "Neobash", "PrintBridge", "Logs", "task.log"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void CompleteDeferral()
    {
        var deferral = Interlocked.Exchange(ref _deferral, null);
        try
        {
            deferral?.Complete();
        }
        catch (Exception exception)
        {
            _log?.Write("No se pudo cerrar el aplazamiento de la extensión: " + exception);
        }
    }
}
