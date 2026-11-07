using Windows.Data.Pdf;
using Windows.Storage.Streams;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class PdfDocumentPreparer : IDocumentPreparer
{
    public async Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(job.LocalPath))
        {
            throw new FileNotFoundException("El PDF indicado no está en esa ruta.", job.LocalPath);
        }

        var bytes = await File.ReadAllBytesAsync(job.LocalPath, cancellationToken).ConfigureAwait(false);
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);

        var document = await PdfDocument.LoadFromStreamAsync(stream);
        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("El PDF no tiene páginas.");
        }

        var pages = new List<byte[]>(checked((int)document.PageCount));
        for (uint index = 0; index < document.PageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = document.GetPage(index);
            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered);
            rendered.Seek(0);
            var size = checked((uint)rendered.Size);
            var reader = new DataReader(rendered.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var image = new byte[size];
            reader.ReadBytes(image);
            reader.DetachStream();
            pages.Add(image);
        }

        return new PreparedDocument(PhysicalQueueRules.PdfMediaType, pages, job.Id);
    }
}
