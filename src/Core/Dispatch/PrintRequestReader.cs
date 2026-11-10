namespace Neobash.PrintBridge.Core.Dispatch;

public static class PrintRequestReader
{
    public static bool TryRead(
        string? paper,
        string? copiesText,
        bool allPages,
        string? firstText,
        string? lastText,
        bool landscape,
        ScaleMode scaleMode,
        string? percentText,
        out PrintOptions options,
        out string error)
    {
        options = PrintOptions.ForPaper(paper ?? "");
        if (string.IsNullOrWhiteSpace(paper))
        {
            error = "Elige un papel anunciado por esa cola.";
            return false;
        }

        if (!int.TryParse(copiesText, out var copies))
        {
            error = "Las copias tienen que ser un número entero.";
            return false;
        }

        PageRange? range = null;
        if (!allPages)
        {
            if (!int.TryParse(firstText, out var first) || !int.TryParse(lastText, out var last))
            {
                error = "El rango de páginas tiene que usar números enteros.";
                return false;
            }

            range = new PageRange(first, last);
        }

        int? percent = null;
        if (scaleMode == ScaleMode.Percentage)
        {
            if (!int.TryParse(percentText, out var parsed))
            {
                error = "El porcentaje tiene que ser un número entero del tamaño físico del PDF.";
                return false;
            }

            percent = parsed;
        }

        options = new PrintOptions
        {
            PaperName = paper,
            Copies = copies,
            Range = range,
            Orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait,
            ScaleMode = scaleMode,
            ScalePercent = percent,
            PagesPerSheet = 1,
        };
        error = "";
        return true;
    }
}
