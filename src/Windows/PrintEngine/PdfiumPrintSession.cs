using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

// Keep PDF objects as native drawing commands instead of flattening every page
// into a display bitmap. PDFium may still rasterize individual PDF effects.
internal sealed class PdfiumPrintSession : IDisposable
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private IntPtr _document;
    private bool _ownsGate;

    public PdfiumPrintSession(string path)
    {
        Monitor.Enter(Gate);
        _ownsGate = true;
        try
        {
            if (!_initialized)
            {
                FPDF_InitLibrary();
                _initialized = true;
            }

            if (FPDF_SetPrintMode(0) == 0) // FPDF_PRINTMODE_EMF
                throw new InvalidOperationException("PDFium no pudo seleccionar la salida de impresión Windows.");
            _document = FPDF_LoadDocument(path, IntPtr.Zero);
            if (_document == IntPtr.Zero)
                throw new InvalidOperationException($"No se pudo abrir el PDF para impresión nativa (código {FPDF_GetLastError()}).");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void ValidatePages(IEnumerable<int> pages)
    {
        foreach (var number in pages)
        {
            var page = LoadPage(number);
            FPDF_ClosePage(page);
        }
    }

    public void Render(PrintPageEventArgs args, int pageNumber, PagePlacement placement)
    {
        var graphics = args.Graphics ?? throw new InvalidOperationException("El controlador no entregó un contexto de impresión.");
        var area = args.PageSettings.PrintableArea;
        var dc = graphics.GetHdc();
        try
        {
            RenderToDeviceContext(dc, pageNumber, placement,
                new PrintableBox(area.X / 100.0, area.Y / 100.0, area.Width / 100.0, area.Height / 100.0));
        }
        finally { graphics.ReleaseHdc(dc); }
    }

    internal void RenderToDeviceContext(IntPtr dc, int pageNumber, PagePlacement placement, PrintableBox area)
    {
        var page = LoadPage(pageNumber);
        try
        {
            var saved = SaveDC(dc);
            if (saved == 0) throw new InvalidOperationException("No se pudo conservar el contexto de impresión.");
            try
            {
                var dpiX = GetDeviceCaps(dc, 88); // LOGPIXELSX
                var dpiY = GetDeviceCaps(dc, 90); // LOGPIXELSY
                if (dpiX <= 0 || dpiY <= 0)
                    throw new InvalidOperationException("El contexto de impresión no informa una resolución válida.");
                var offsetX = GetDeviceCaps(dc, 112); // PHYSICALOFFSETX
                var offsetY = GetDeviceCaps(dc, 113); // PHYSICALOFFSETY
                if (SetMapMode(dc, 1) == 0 || !SetWindowOrgEx(dc, 0, 0, IntPtr.Zero)
                    || !SetViewportOrgEx(dc, 0, 0, IntPtr.Zero))
                    throw new InvalidOperationException("No se pudo preparar la escala del contexto de impresión.");

                int X(double inches) => checked((int)Math.Round(inches * dpiX)) - offsetX;
                int Y(double inches) => checked((int)Math.Round(inches * dpiY)) - offsetY;
                if (IntersectClipRect(dc, X(area.XInches), Y(area.YInches),
                    X(area.XInches + area.WidthInches), Y(area.YInches + area.HeightInches)) == 0)
                    throw new InvalidOperationException("No se pudo limitar el área imprimible.");

                // Start at the printable origin; do not center in the long virtual
                // receipt page or paint a page-sized white background.
                var width = checked((int)Math.Round(placement.WidthInches * dpiX));
                var height = checked((int)Math.Round(placement.HeightInches * dpiY));
                if (FPDF_RenderPage(dc, page, X(placement.XInches), Y(placement.YInches),
                    width, height, 0, 0x801) == 0) // FPDF_PRINTING | FPDF_ANNOT
                    throw new InvalidOperationException("PDFium no pudo dibujar la página en el controlador.");
            }
            finally { RestoreDC(dc, saved); }
        }
        finally { FPDF_ClosePage(page); }
    }

    private IntPtr LoadPage(int number)
    {
        var page = FPDF_LoadPage(_document, number - 1);
        if (page == IntPtr.Zero)
            throw new InvalidOperationException($"No se pudo preparar la página {number} para impresión nativa.");
        return page;
    }

    public void Dispose()
    {
        try
        {
            if (_document != IntPtr.Zero)
            {
                FPDF_CloseDocument(_document);
                _document = IntPtr.Zero;
            }
        }
        finally
        {
            if (_ownsGate)
            {
                _ownsGate = false;
                Monitor.Exit(Gate);
            }
        }
    }

    [DllImport("pdfium", ExactSpelling = true)] private static extern void FPDF_InitLibrary();
    [DllImport("pdfium", ExactSpelling = true)] private static extern int FPDF_SetPrintMode(int mode);
    [DllImport("pdfium", ExactSpelling = true)] private static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr password);
    [DllImport("pdfium", ExactSpelling = true)] private static extern uint FPDF_GetLastError();
    [DllImport("pdfium", ExactSpelling = true)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport("pdfium", ExactSpelling = true)] private static extern int FPDF_RenderPage(IntPtr dc, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    [DllImport("pdfium", ExactSpelling = true)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium", ExactSpelling = true)] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport("gdi32.dll")] private static extern int SaveDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RestoreDC(IntPtr dc, int saved);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("gdi32.dll")] private static extern int SetMapMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetViewportOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("gdi32.dll")] private static extern int IntersectClipRect(IntPtr dc, int left, int top, int right, int bottom);
}
