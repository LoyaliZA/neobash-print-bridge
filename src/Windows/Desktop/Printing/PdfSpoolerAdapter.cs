using System.Drawing.Printing;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class PdfSpoolerAdapter : ISpoolerPort
{
    private readonly PdfDocumentPreparer _prepaper = new();

    public void Submit(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken)
    {
        if (document.Content is not PreparedPdf prepared || prepared.Pages.Count == 0)
        {
            throw new SpoolerPortException(started: false, "El documento preparado no trae páginas PDF.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Print(document.Name, prepared, queueName, paperName, cancellationToken);
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

    private void Print(string documentName, PreparedPdf prepared, string queueName, string? paperName, CancellationToken cancellationToken)
    {
        using var document = new PrintDocument();
        document.PrintController = new StandardPrintController();
        document.OriginAtMargins = false;
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

        var options = prepared.Options;
        if (options.Copies > document.PrinterSettings.MaximumCopies)
        {
            throw new SpoolerPortException(started: false, $"Esa cola admite como máximo {document.PrinterSettings.MaximumCopies} copias.");
        }

        document.DefaultPageSettings.PaperSize = match;
        document.DefaultPageSettings.Landscape = options.Orientation == PageOrientation.Landscape;
        if (options.Orientation == PageOrientation.Landscape && !document.DefaultPageSettings.Landscape)
        {
            throw new SpoolerPortException(started: false, "Esa cola no admite orientación horizontal.");
        }

        document.PrinterSettings.Copies = (short)options.Copies;
        document.PrinterSettings.PrintRange = PrintRange.AllPages;

        var printable = document.DefaultPageSettings.PrintableArea;
        var resolution = document.DefaultPageSettings.PrinterResolution;
        var box = new PrintableBox(printable.X / 100.0, printable.Y / 100.0, printable.Width / 100.0, printable.Height / 100.0);
        IReadOnlyList<(double WidthInches, double HeightInches)> sizes;
        try
        {
            sizes = Task.Run(() => _prepaper.MeasureAsync(prepared.Path, cancellationToken)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new SpoolerPortException(started: false, exception.Message);
        }

        var layouts = new List<(int Page, PagePlacement Placement)>(prepared.Pages.Count);
        foreach (var page in prepared.Pages)
        {
            if (page < 1 || page > sizes.Count)
            {
                throw new SpoolerPortException(started: false, "La página pedida no está en el PDF.");
            }

            var size = sizes[page - 1];
            var layout = PageLayoutCalculator.Place(size.WidthInches, size.HeightInches, box, resolution.X, resolution.Y, options.ScaleMode, options.ScalePercent, options.AllowClippingConfirmed);
            if (!layout.Accepted || layout.Placement is null)
            {
                throw new SpoolerPortException(started: false, $"Página {page}: {layout.Error ?? "No se pudo calcular la página."}", layout.RequiresClippingConfirmation);
            }

            layouts.Add((page, layout.Placement.Value));
        }

        PdfiumPrintSession native;
        try
        {
            native = new PdfiumPrintSession(prepared.Path);
            try { native.ValidatePages(prepared.Pages); }
            catch { native.Dispose(); throw; }
        }
        catch (Exception exception)
        {
            throw new SpoolerPortException(started: false, exception.Message);
        }

        using var nativeSession = native;
        var index = 0;
        document.PrintPage += (_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = layouts[index];
            nativeSession.Render(args, current.Page, current.Placement);
            index++;
            args.HasMorePages = index < layouts.Count;
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

}
