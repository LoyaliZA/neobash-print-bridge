using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class DesktopHost
{
    public DesktopHost(
        IDestinationStore destinations,
        IJobRecordStore jobs,
        WindowsPrinterCatalog catalog,
        PrintDispatcher dispatcher,
        IDiagnosticLog log)
    {
        Destinations = destinations;
        Jobs = jobs;
        Catalog = catalog;
        Dispatcher = dispatcher;
        Log = log;
    }

    public IDestinationStore Destinations { get; }

    public IJobRecordStore Jobs { get; }

    public WindowsPrinterCatalog Catalog { get; }

    public PrintDispatcher Dispatcher { get; }

    public IDiagnosticLog Log { get; }
}
