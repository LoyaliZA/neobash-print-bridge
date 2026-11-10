using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Neobash.PrintBridge.Desktop;

public sealed class PdfPagePreview
{
    public const int MaxEdge = 1400;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PreviewFrame?> RenderAsync(string path, uint pageIndex, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
            using var source = await file.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var document = await PdfDocument.LoadFromStreamAsync(source).AsTask(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (document.PageCount == 0 || pageIndex >= document.PageCount) return null;
            using var page = document.GetPage(pageIndex);
            var factor = Math.Min(1, MaxEdge / Math.Max(page.Size.Width, page.Size.Height));
            var output = new InMemoryRandomAccessStream();
            try
            {
                await page.RenderToStreamAsync(output, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, page.Size.Width * factor),
                }).AsTask(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                output.Seek(0);
                return new PreviewFrame(output, document.PageCount, page.Size.Width, page.Size.Height);
            }
            catch { output.Dispose(); throw; }
            // PdfDocument has no Close/Dispose contract. Page is closed before source.
        }
        finally { _gate.Release(); }
    }
}

public sealed class PreviewFrame(IRandomAccessStream image, uint pageCount, double widthDips, double heightDips) : IDisposable
{
    public IRandomAccessStream Image { get; } = image;
    public uint PageCount { get; } = pageCount;
    public double WidthDips { get; } = widthDips;
    public double HeightDips { get; } = heightDips;
    public void Dispose() => Image.Dispose();
}
