using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class WindowsPrinterCatalog : IPrinterCatalog
{
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public InstalledQueue? Find(string queueName) =>
        List().FirstOrDefault(queue => string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<InstalledQueue> List()
    {
        var queues = new List<InstalledQueue>();
        var warnings = new List<string>();
        foreach (string? name in PrinterSettings.InstalledPrinters)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                queues.Add(Read(name));
            }
            catch (Exception exception)
            {
                warnings.Add($"No se pudo leer la cola «{name}»: {exception.Message}");
            }
        }

        Warnings = warnings;
        return queues;
    }

    private static InstalledQueue Read(string queueName)
    {
        var settings = new PrinterSettings { PrinterName = queueName };
        var papers = new List<string>();
        foreach (PaperSize size in settings.PaperSizes)
        {
            if (string.IsNullOrWhiteSpace(size.PaperName))
            {
                continue;
            }

            if (papers.Exists(paper => string.Equals(paper, size.PaperName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            papers.Add(size.PaperName);
        }

        return new InstalledQueue(queueName, ReadDriverName(queueName), papers);
    }

    private static string ReadDriverName(string queueName)
    {
        if (!OpenPrinter(queueName, out var handle, IntPtr.Zero))
        {
            return "";
        }

        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var needed);
            if (needed == 0)
            {
                return "";
            }

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetPrinter(handle, 2, buffer, needed, out _))
                {
                    return "";
                }

                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                return info.pDriverName ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, uint level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public string? pServerName;
        public string? pPrinterName;
        public string? pShareName;
        public string? pPortName;
        public string? pDriverName;
        public string? pComment;
        public string? pLocation;
        public IntPtr pDevMode;
        public string? pSepFile;
        public string? pPrintProcessor;
        public string? pDatatype;
        public string? pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }
}
