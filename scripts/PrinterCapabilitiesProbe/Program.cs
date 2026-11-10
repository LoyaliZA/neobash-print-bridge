using System.Printing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

// Read-only diagnostic: no Commit, input DEVMODE, Print, RAW, or manufacturer commands.
internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var server = new LocalPrintServer();
        var results = new List<object>();
        using var queues = server.GetPrintQueues();
        var names = queues.Select(q => q.Name).Where(n => n.Contains("BSC10", StringComparison.OrdinalIgnoreCase)
            || n.Contains("POS80", StringComparison.OrdinalIgnoreCase) || n.Contains("HL-1210", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var name in names)
        {
            try
            {
                using var queue = server.GetPrintQueue(name);
                using var xml = queue.GetPrintCapabilitiesAsXml();
                var document = XDocument.Load(xml);
                var capabilities = queue.GetPrintCapabilities();
                var ticket = queue.UserPrintTicket ?? queue.DefaultPrintTicket;
                results.Add(new
                {
                    queue = name, driver = queue.QueueDriver.Name, port = queue.QueuePort.Name.StartsWith("WSD-", StringComparison.OrdinalIgnoreCase) ? "WSD (identifier omitted)" : queue.QueuePort.Name,
                    devMode = ReadDevMode(name),
                    status = queue.QueueStatus.ToString(), jobs = queue.NumberOfJobs,
                    features = document.Descendants().Where(e => e.Name.LocalName == "Feature").Select(e => new
                    {
                        name = (string?)e.Attribute("name"),
                        options = e.Elements().Where(o => o.Name.LocalName == "Option").Select(o => (string?)o.Attribute("name")).ToArray()
                    }).ToArray(),
                    papers = capabilities.PageMediaSizeCapability.Select(p => new { name = p.PageMediaSizeName.ToString(), p.Width, p.Height }).ToArray(),
                    orientations = capabilities.PageOrientationCapability.Select(o => o.ToString()).ToArray(),
                    resolutions = capabilities.PageResolutionCapability.Select(r => new { r.X, r.Y, quality = r.QualitativeResolution.ToString() }).ToArray(),
                    defaults = new { paper = ticket.PageMediaSize?.PageMediaSizeName?.ToString(), orientation = ticket.PageOrientation?.ToString(), ticket.CopyCount },
                    writableValidated = false
                });
            }
            catch (Exception exception) { results.Add(new { queue = name, error = exception.Message, writableValidated = false }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { observedAt = DateTimeOffset.UtcNow, queues = results }, new JsonSerializerOptions { WriteIndented = true }));
    }
    static object ReadDevMode(string name)
    {
        if (!OpenPrinter(name, out var printer, IntPtr.Zero)) return new { error = Marshal.GetLastWin32Error() };
        try
        {
            var length = DocumentProperties(IntPtr.Zero, printer, name, IntPtr.Zero, IntPtr.Zero, 0);
            if (length < 76 || length > 16 * 1024 * 1024) return new { invalidSize = length };
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                // DM_OUT_BUFFER copies current settings to our memory; no input or prompt.
                if (DocumentProperties(IntPtr.Zero, printer, name, buffer, IntPtr.Zero, 2) != 1)
                    return new { error = "Cannot read DEVMODE" };
                return new
                {
                    totalBytes = length, publicBytes = (ushort)Marshal.ReadInt16(buffer, 68),
                    privateBytes = (ushort)Marshal.ReadInt16(buffer, 70),
                    fields = unchecked((uint)Marshal.ReadInt32(buffer, 72)).ToString("X8"),
                    privateDataDecoded = false
                };
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ClosePrinter(printer); }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
    static extern int DocumentProperties(IntPtr window, IntPtr handle, string name, IntPtr output, IntPtr input, uint mode);

}
