using Microsoft.UI.Xaml;
using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public partial class App : Application
{
    private Window? _window;

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
        JobRecovery.MarkInterruptedAsUncertain(jobs);
        var catalog = new WindowsPrinterCatalog();
        var dispatcher = new PrintDispatcher(
            destinations,
            catalog,
            jobs,
            new PdfDocumentPreparer(),
            new PdfSpoolerAdapter());
        var log = new FileDiagnosticLog(Path.Combine(root, "diagnostic.log"));
        log.Write("NPB Desktop abierto. Los trabajos interrumpidos en recibido o preparado quedan incierto y no se reimprimen.");

        _window = new MainWindow(new DesktopHost(destinations, jobs, catalog, dispatcher, log));
        _window.Activate();
    }
}
