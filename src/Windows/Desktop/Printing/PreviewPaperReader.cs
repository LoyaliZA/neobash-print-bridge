using System.Drawing.Printing;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed record PreviewPaper(double Width, double Height, PrintableBox Printable, int DpiX, int DpiY)
{
    public bool HasConsistentBounds => double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0
        && double.IsFinite(Printable.XInches) && double.IsFinite(Printable.YInches)
        && double.IsFinite(Printable.WidthInches) && double.IsFinite(Printable.HeightInches)
        && Printable.WidthInches > 0 && Printable.HeightInches > 0
        && Printable.XInches >= 0 && Printable.YInches >= 0
        && Printable.XInches + Printable.WidthInches <= Width + 0.02
        && Printable.YInches + Printable.HeightInches <= Height + 0.02;
}

// Read-only driver access, isolated from the UI and the print submission STA.
public sealed class PreviewPaperReader
{
    private readonly StaQueue _sta = new();
    private readonly Dictionary<(string, string, PageOrientation), (DateTimeOffset At, PreviewPaper Paper)> _cache = new();
    public Task<PreviewPaper> ReadAsync(string queue, string paper, PageOrientation orientation, CancellationToken token) => Task.Run(() => _sta.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var key = (queue, paper, orientation);
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(30)) return cached.Paper;
        using var document = new PrintDocument();
        document.PrinterSettings.PrinterName = queue;
        if (!document.PrinterSettings.IsValid) throw new InvalidOperationException("La cola no está disponible para consultar el papel.");
        var match = document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => string.Equals(p.PaperName, paper, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("El papel guardado ya no está anunciado por el controlador.");
        document.DefaultPageSettings.PaperSize = match;
        document.DefaultPageSettings.Landscape = orientation == PageOrientation.Landscape;
        var settings = document.DefaultPageSettings;
        var area = settings.PrintableArea;
        var bounds = settings.Bounds;
        var result = new PreviewPaper(bounds.Width / 100.0, bounds.Height / 100.0,
            new(area.X / 100.0, area.Y / 100.0, area.Width / 100.0, area.Height / 100.0), settings.PrinterResolution.X, settings.PrinterResolution.Y);
        token.ThrowIfCancellationRequested();
        _cache[key] = (DateTimeOffset.UtcNow, result);
        return result;
    }), token);
}
