using System.Runtime.InteropServices;
using System.Text;
using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Desktop;

namespace Neobash.PrintBridge.WindowsPrintTests;

public sealed class NativePdfTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "npb-native-" + Guid.NewGuid().ToString("N") + ".pdf");

    [Fact]
    public void Native_page_emits_vector_commands_at_the_top_without_a_full_page_bitmap()
    {
        File.WriteAllBytes(_path, TestPdf());
        using var pdf = new PdfiumPrintSession(_path);
        pdf.ValidatePages([1]);
        var dc = CreateEnhMetaFileW(IntPtr.Zero, null, IntPtr.Zero, null);
        Assert.NotEqual(IntPtr.Zero, dc);
        IntPtr metafile;
        try
        {
            // The destination is deliberately three metres tall. No printer is opened.
            pdf.RenderToDeviceContext(dc, 1, new PagePlacement(0, 0, 3, 5, 609, 1015, 203, 203),
                new PrintableBox(0, 0, 3, 118.22));
        }
        finally { metafile = CloseEnhMetaFile(dc); }
        Assert.NotEqual(IntPtr.Zero, metafile);
        try
        {
            var size = GetEnhMetaFileBits(metafile, 0, null);
            var data = new byte[checked((int)size)];
            Assert.Equal(size, GetEnhMetaFileBits(metafile, size, data));
            var kinds = new List<uint>();
            var nativeFill = false;
            for (var offset = 0; offset + 8 <= data.Length;)
            {
                var kind = BitConverter.ToUInt32(data, offset);
                kinds.Add(kind);
                var length = checked((int)BitConverter.ToUInt32(data, offset + 4));
                Assert.True(length >= 8 && offset + length <= data.Length);
                // GDI can encode a solid fill as PATCOPY, without a source bitmap.
                if (kind == 76 && length >= 100
                    && BitConverter.ToUInt32(data, offset + 40) == 0x00F00021
                    && BitConverter.ToUInt32(data, offset + 96) == 0)
                    nativeFill = true;
                offset += length;
            }
            Assert.True(nativeFill || kinds.Contains(43u) || kinds.Contains(59u), string.Join(",", kinds));
            Assert.DoesNotContain(81u, kinds); // No whole-page EMR_STRETCHDIBITS.
            var bottom = BitConverter.ToInt32(data, 20); // EMR_HEADER.rclBounds.bottom
            Assert.InRange(bottom, 0, 100); // Only the top strip was painted, not the 3 m tail.
        }
        finally { DeleteEnhMetaFile(metafile); }
    }

    [Fact]
    public void Invalid_pdf_is_rejected_before_any_print_device_is_opened()
    {
        File.WriteAllText(_path, "This is not a PDF.");
        Assert.Throws<InvalidOperationException>(() => new PdfiumPrintSession(_path));
        // Failed initialization must release the engine lock for the next request.
        File.WriteAllBytes(_path, TestPdf());
        using var pdf = new PdfiumPrintSession(_path);
        pdf.ValidatePages([1]);
    }

    [Fact]
    public void Invalid_page_is_rejected_during_preflight()
    {
        File.WriteAllBytes(_path, TestPdf());
        using var pdf = new PdfiumPrintSession(_path);
        Assert.Throws<InvalidOperationException>(() => pdf.ValidatePages([2]));
        pdf.ValidatePages([1]);
    }

    private static byte[] TestPdf()
    {
        const string content = "0 0 0 rg 0 360 100 10 re f\n";
        string[] objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 216 370] /Resources << >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream",
        ];
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(text.Length);
            text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = text.Length;
        text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEnhMetaFileW(IntPtr reference, string? path, IntPtr bounds, string? description);
    [DllImport("gdi32.dll")] private static extern IntPtr CloseEnhMetaFile(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetEnhMetaFileBits(IntPtr metafile, uint size, [Out] byte[]? data);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteEnhMetaFile(IntPtr metafile);
}
