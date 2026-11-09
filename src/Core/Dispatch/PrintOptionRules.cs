namespace Neobash.PrintBridge.Core.Dispatch;

public static class PrintOptionRules
{
    public const int MinimumScalePercent = 1;

    public const int MaximumScalePercent = 400;

    public static string? Check(PrintOptions options, InstalledQueue queue)
    {
        if (string.IsNullOrWhiteSpace(options.PaperName)
            || !queue.PaperNames.Any(paper => string.Equals(paper, options.PaperName, StringComparison.OrdinalIgnoreCase)))
        {
            return "El papel elegido no está entre los que anuncia esa cola.";
        }

        if (options.Copies < 1)
        {
            return "El número de copias tiene que ser al menos 1.";
        }

        if (options.Copies > queue.MaximumCopies)
        {
            return $"Esa cola admite como máximo {queue.MaximumCopies} copias.";
        }

        if (options.PagesPerSheet != 1)
        {
            return "Esta entrega solo admite una página por hoja.";
        }

        if (options.Orientation == PageOrientation.Landscape && !queue.SupportsLandscape)
        {
            return "Esa cola no admite orientación horizontal.";
        }

        if (options.Orientation == PageOrientation.Portrait && !queue.SupportsPortrait)
        {
            return "Esa cola no admite orientación vertical.";
        }

        if (options.Range is { } range && (range.FirstPage < 1 || range.LastPage < range.FirstPage))
        {
            return "El rango de páginas no es válido.";
        }

        switch (options.ScaleMode)
        {
            case ScaleMode.ActualSize:
            case ScaleMode.FitPrintableArea:
                if (options.ScalePercent is not null)
                {
                    return "El porcentaje solo se aplica en el modo de escala por porcentaje. No se combina con otro ajuste.";
                }

                break;
            case ScaleMode.Percentage:
                if (options.ScalePercent is null)
                {
                    return "El modo de porcentaje necesita un porcentaje explícito del tamaño físico del PDF.";
                }

                if (options.ScalePercent < MinimumScalePercent || options.ScalePercent > MaximumScalePercent)
                {
                    return $"El porcentaje tiene que estar entre {MinimumScalePercent} y {MaximumScalePercent}.";
                }

                break;
            default:
                return "El modo de escala no está admitido.";
        }

        return null;
    }
}
