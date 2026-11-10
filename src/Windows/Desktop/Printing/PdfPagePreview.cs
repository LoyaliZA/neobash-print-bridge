using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Neobash.PrintBridge.Desktop;

public sealed class PdfPagePreview
{
    public const int MaxEdge = 1000;

    public async Task<PreviewFrame?> RenderAsync(string path, uint pageIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(stream);
        if (document.PageCount == 0 || pageIndex >= document.PageCount)
        {
            stream.Dispose();
            return null;
        }

        using var page = document.GetPage(pageIndex);
        var longest = Math.Max(page.Size.Width, page.Size.Height);
        var scale = longest > MaxEdge ? MaxEdge / longest : 1;
        var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale),
        });
        output.Seek(0);
        return new PreviewFrame(output, document.PageCount, page.Size.Width, page.Size.Height, stream);
    }
}

public sealed class PreviewFrame(IRandomAccessStream image, uint pageCount, double widthDips, double heightDips, IDisposable document) : IDisposable
{
    public IRandomAccessStream Image { get; } = image;

    public uint PageCount { get; } = pageCount;

    public double WidthDips { get; } = widthDips;

    public double HeightDips { get; } = heightDips;

    public void Dispose()
    {
        Image.Dispose();
        document.Dispose();
    }
}
