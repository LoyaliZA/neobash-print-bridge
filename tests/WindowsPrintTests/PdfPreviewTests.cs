using System.Text;
using Neobash.PrintBridge.Desktop;

namespace Neobash.PrintBridge.WindowsPrintTests;

public sealed class PdfPreviewTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "npb-preview-" + Guid.NewGuid().ToString("N") + ".pdf");
    public PdfPreviewTests() => File.WriteAllBytes(_path, SamplePdf());

    [Fact]
    public void Preview_rejects_inconsistent_or_nonfinite_driver_paper_geometry()
    {
        var portrait = new PreviewPaper(3, 8, new(0, 0, 2.98, 7.98), 203, 203);
        Assert.True(portrait.HasConsistentBounds);
        Assert.False((portrait with { Width = 8, Height = 3 }).HasConsistentBounds);
        Assert.False((portrait with { Width = double.NaN }).HasConsistentBounds);
        Assert.False((portrait with { Printable = new(0, 0, double.NaN, 8) }).HasConsistentBounds);
    }

    [Fact]
    public async Task Preview_reads_requested_page_size_and_releases_its_output_stream()
    {
        var preview = new PdfPagePreview();
        using var first = await preview.RenderAsync(_path, 0, default);
        using var second = await preview.RenderAsync(_path, 1, default);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2u, first.PageCount);
        Assert.Equal(192, first.WidthDips, 2);
        Assert.Equal(96, first.HeightDips, 2);
        Assert.Equal(96, second.WidthDips, 2);
        Assert.True(first.Image.Size > 0);
        second.Dispose();
        Assert.ThrowsAny<Exception>(() => { _ = second.Image.Size; });
        Assert.Null(await preview.RenderAsync(_path, 2, default));
    }

    [Fact]
    public async Task Cancelled_and_invalid_requests_do_not_poison_later_renders()
    {
        var preview = new PdfPagePreview();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.RenderAsync(_path, 0, cancelled.Token));
        var invalid = _path + ".invalid.pdf";
        try
        {
            File.WriteAllText(invalid, "invalid pdf");
            await Assert.ThrowsAnyAsync<Exception>(() => preview.RenderAsync(invalid, 0, default));
        }
        finally { File.Delete(invalid); }
        using var valid = await preview.RenderAsync(_path, 1, default);
        Assert.NotNull(valid);
    }

    [Fact]
    public async Task Rapid_page_changes_cancel_waiters_and_last_request_completes()
    {
        var preview = new PdfPagePreview();
        var requests = new List<Task<PreviewFrame?>>();
        var tokens = new List<CancellationTokenSource>();
        for (var i = 0; i < 12; i++)
        {
            if (tokens.Count > 0) tokens[^1].Cancel();
            var token = new CancellationTokenSource();
            tokens.Add(token);
            requests.Add(preview.RenderAsync(_path, (uint)(i % 2), token.Token));
        }
        try
        {
            for (var i = 0; i < requests.Count - 1; i++)
            {
                try { using var superseded = await requests[i]; }
                catch (OperationCanceledException) { }
            }
            using var last = await requests[^1];
            Assert.NotNull(last);
            Assert.Equal(96, last.WidthDips, 2);
        }
        finally { foreach (var token in tokens) token.Dispose(); }
    }

    internal static byte[] SamplePdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 144 72] /Contents 4 0 R >>",
            "<< /Length 24 >>\nstream\n0 0 1 rg 8 8 60 40 re f\nendstream",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 144] /Contents 6 0 R >>",
            "<< /Length 24 >>\nstream\n1 0 0 rg 8 8 40 60 re f\nendstream",
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    public void Dispose() => File.Delete(_path);
}
