using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Desktop;
using Neobash.PrintBridge.Lan;

namespace Neobash.PrintBridge.Agent;

public sealed class WindowsPrintEngine : IPrintEngine, IDisposable
{
    private readonly WindowsPrinterProbe _probe = new();

    public WindowsPrintEngine()
    {
        Preparer = new PdfDocumentPreparer();
        Spooler = new PdfSpoolerAdapter();
        Catalog = new ProbeCatalog(_probe);
    }

    public IDocumentPreparer Preparer { get; }

    public ISpoolerPort Spooler { get; }

    public IPrinterCatalog Catalog { get; }

    public void Dispose() => _probe.Dispose();

    private sealed class ProbeCatalog(WindowsPrinterProbe probe) : IPrinterCatalog
    {
        public InstalledQueue? Find(string queueName) => probe.ReadDetails(queueName);

        public IReadOnlyList<InstalledQueue> List()
        {
            var queues = new List<InstalledQueue>();
            foreach (var name in probe.ListNames())
            {
                var queue = probe.ReadDetails(name);
                if (queue is not null)
                {
                    queues.Add(queue);
                }
            }

            return queues;
        }
    }
}
