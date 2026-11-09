using Windows.Data.Pdf;
using Windows.Storage.Streams;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class PdfDocumentPreparer : IDocumentPreparer
{
    public async Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken)
    {
        if (job.Options is null)
        {
            throw new InvalidOperationException("El trabajo no trae opciones de impresión.");
        }

        var sizes = await MeasureAsync(job.LocalPath, cancellationToken).ConfigureAwait(false);
        var pages = PageSelection.Select(sizes.Count, job.Options.Range);
        return new PreparedDocument(PhysicalQueueRules.PdfMediaType, new PreparedPdf(job.LocalPath, job.Options, pages), job.Id);
    }

    public async Task<IReadOnlyList<(double WidthInches, double HeightInches)>> MeasureAsync(string path, CancellationToken cancellationToken)
    {
        using var opened = await OpenAsync(path, cancellationToken).ConfigureAwait(false);
        if (opened.Document.PageCount == 0)
        {
            throw new InvalidOperationException("El PDF no tiene páginas.");
        }

        var sizes = new List<(double WidthInches, double HeightInches)>(checked((int)opened.Document.PageCount));
        for (uint index = 0; index < opened.Document.PageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = opened.Document.GetPage(index);
            var (width, height) = PageLayoutCalculator.PhysicalSizeFromDips(page.Size.Width, page.Size.Height);
            sizes.Add((width, height));
        }

        return sizes;
    }

    private static async Task<OpenedPdf> OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("El PDF indicado no está en esa ruta.", path);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(stream);
        return new OpenedPdf(document, stream);
    }

    private sealed class OpenedPdf(PdfDocument document, InMemoryRandomAccessStream stream) : IDisposable
    {
        public PdfDocument Document { get; } = document;

        public void Dispose() => stream.Dispose();
    }
}
