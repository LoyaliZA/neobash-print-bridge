using System.Drawing;
using System.Drawing.Printing;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class PdfSpoolerAdapter : ISpoolerPort
{
    public void Submit(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken)
    {
        if (document.Content is not IReadOnlyList<byte[]> pages || pages.Count == 0)
        {
            throw new SpoolerPortException(started: false, "El PDF preparado no tiene páginas.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Print(document.Name, pages, queueName, paperName);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void Print(string documentName, IReadOnlyList<byte[]> pages, string queueName, string? paperName)
    {
        using var document = new PrintDocument();
        document.PrintController = new StandardPrintController();
        document.DocumentName = documentName;
        document.PrinterSettings.PrinterName = queueName;
        if (!document.PrinterSettings.IsValid)
        {
            throw new SpoolerPortException(started: false, $"La cola «{queueName}» no acepta el trabajo.");
        }

        PaperSize? match = null;
        foreach (PaperSize size in document.PrinterSettings.PaperSizes)
        {
            if (string.Equals(size.PaperName, paperName, StringComparison.OrdinalIgnoreCase))
            {
                match = size;
                break;
            }
        }

        if (match is null)
        {
            throw new SpoolerPortException(started: false, "El papel elegido ya no está entre los que anuncia el controlador.");
        }

        document.DefaultPageSettings.PaperSize = match;
        var index = 0;
        document.PrintPage += (_, args) =>
        {
            using var stream = new MemoryStream(pages[index], writable: false);
            using var bitmap = new Bitmap(stream);
            DrawWithin(args.Graphics!, bitmap, args.PageBounds);
            index++;
            args.HasMorePages = index < pages.Count;
        };

        try
        {
            document.Print();
        }
        catch (SpoolerPortException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SpoolerPortException(started: true, exception.Message);
        }
    }

    private static void DrawWithin(Graphics graphics, Bitmap bitmap, Rectangle bounds)
    {
        var scale = Math.Min(bounds.Width / (float)bitmap.Width, bounds.Height / (float)bitmap.Height);
        if (float.IsInfinity(scale) || scale <= 0)
        {
            throw new InvalidOperationException("La página rasterizada no tiene un tamaño utilizable.");
        }

        var width = bitmap.Width * scale;
        var height = bitmap.Height * scale;
        var x = bounds.X + ((bounds.Width - width) / 2f);
        var y = bounds.Y + ((bounds.Height - height) / 2f);
        graphics.DrawImage(bitmap, x, y, width, height);
    }
}
