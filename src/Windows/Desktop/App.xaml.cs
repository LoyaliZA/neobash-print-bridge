using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public partial class App : Application
{
    private Window? _window;

    static App()
    {
        _ = StartupClock.ProcessToManaged;
    }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Neobash",
            "PrintBridge",
            "Desktop");
        Directory.CreateDirectory(root);

        var destinations = new FileDestinationStore(root);
        var jobs = new FileJobRecordStore(root);
        var log = new FileDiagnosticLog(Path.Combine(root, "diagnostic.log"));
        log.Timing("process-start", StartupClock.ProcessToManaged, ("baseline", "process-start"));
        var catalog = new CachingPrinterCatalog(new WindowsPrinterProbe(), log);
        var dispatcher = new PrintDispatcher(
            destinations,
            catalog,
            jobs,
            new PdfDocumentPreparer(),
            new PdfSpoolerAdapter());
        log.Write("NPB Desktop abierto. Los trabajos interrumpidos en recibido o preparado quedan incierto y no se reimprimen.");

        var window = new MainWindow(new DesktopHost(destinations, jobs, catalog, dispatcher, log));
        _window = window;
        log.Timing("window-activation", StartupClock.SinceManaged(), ("baseline", "managed-entry"));
        window.Activate();
        window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            log.Timing("ui-responsive", StartupClock.SinceManaged(), ("baseline", "managed-entry"));
        });
        window.BeginStartup();
    }
}
