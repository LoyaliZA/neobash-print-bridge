namespace Neobash.PrintBridge.Core.Dispatch;

public readonly record struct PrintableBox(double XInches, double YInches, double WidthInches, double HeightInches);

public readonly record struct PagePlacement(
    double XInches,
    double YInches,
    double WidthInches,
    double HeightInches,
    int PixelWidth,
    int PixelHeight,
    double DpiX,
    double DpiY);

public readonly record struct LayoutResult(PagePlacement? Placement, string? Error, bool RequiresClippingConfirmation = false)
{
    public bool Accepted => Placement is not null && Error is null;
}

public static class PageLayoutCalculator
{
    public const int MaxRasterPixels = 48_000_000;

    public const double PdfDipsPerInch = 96;

    public static (double WidthInches, double HeightInches) PhysicalSizeFromDips(double widthDips, double heightDips) =>
        (widthDips / PdfDipsPerInch, heightDips / PdfDipsPerInch);

    public static LayoutResult Place(
        double pdfWidthInches,
        double pdfHeightInches,
        PrintableBox printable,
        double dpiX,
        double dpiY,
        ScaleMode mode,
        int? scalePercent,
        bool allowClippingConfirmed = false)
    {
        if (pdfWidthInches <= 0 || pdfHeightInches <= 0 || !double.IsFinite(pdfWidthInches) || !double.IsFinite(pdfHeightInches))
        {
            return new LayoutResult(null, "La página del PDF no tiene un tamaño físico utilizable.");
        }

        if (printable.WidthInches <= 0 || printable.HeightInches <= 0)
        {
            return new LayoutResult(null, "El área imprimible de esa cola no tiene un tamaño utilizable.");
        }

        if (dpiX <= 0 || dpiY <= 0 || !double.IsFinite(dpiX) || !double.IsFinite(dpiY))
        {
            return new LayoutResult(null, "La cola no informa una resolución efectiva. No se usa un DPI genérico.");
        }

        if (mode != ScaleMode.Percentage && scalePercent is not null)
        {
            return new LayoutResult(null, "El porcentaje solo se aplica en el modo de escala por porcentaje. No se combina con otro ajuste.");
        }

        double factor = mode switch
        {
            ScaleMode.ActualSize => 1,
            ScaleMode.FitPrintableArea => Math.Min(printable.WidthInches / pdfWidthInches, printable.HeightInches / pdfHeightInches),
            ScaleMode.Percentage when scalePercent is int percent => percent / 100.0,
            _ => double.NaN,
        };

        if (!double.IsFinite(factor) || factor <= 0)
        {
            return new LayoutResult(null, "El modo de escala no está admitido.");
        }

        var width = pdfWidthInches * factor;
        var height = pdfHeightInches * factor;
        const double tolerance = 0.01;
        if (mode != ScaleMode.FitPrintableArea
            && (width > printable.WidthInches + tolerance || height > printable.HeightInches + tolerance))
        {
            if (!allowClippingConfirmed)
            {
                var axes = new List<string>();
                if (width > printable.WidthInches + tolerance) axes.Add("ancho");
                if (height > printable.HeightInches + tolerance) axes.Add("alto");
                var detail = FormattableString.Invariant($"La página escalada mide {width * 25.4:F2} × {height * 25.4:F2} mm; el área imprimible mide {printable.WidthInches * 25.4:F2} × {printable.HeightInches * 25.4:F2} mm (ancho × alto).");
                return new LayoutResult(null,
                    $"Se excede el {string.Join(" y el ", axes)}. {detail} Se compara toda la página, incluidos sus espacios en blanco; no se ha comprobado si se perdería contenido. No se aplica un segundo ajuste. Puedes cancelar y elegir Ajustar al área imprimible, o confirmar la impresión con posible recorte.",
                    RequiresClippingConfirmation: true);
            }
        }

        var pixelWidth = Math.Max(1, (int)Math.Round(width * dpiX, MidpointRounding.AwayFromZero));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * dpiY, MidpointRounding.AwayFromZero));
        if ((long)pixelWidth * pixelHeight > MaxRasterPixels)
        {
            return new LayoutResult(null, "La rasterización de esa página superaría el límite de memoria. No se amplía una imagen más pequeña para sustituirla.");
        }

        return new LayoutResult(
            new PagePlacement(printable.XInches, printable.YInches, width, height, pixelWidth, pixelHeight, dpiX, dpiY),
            null);
    }
}
