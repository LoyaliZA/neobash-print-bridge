namespace Neobash.PrintBridge.Core.Dispatch;

public enum ScaleMode
{
    ActualSize,
    FitPrintableArea,
    Percentage,
}

public enum PageOrientation
{
    Portrait,
    Landscape,
}

public sealed record PageRange(int FirstPage, int LastPage);

public sealed record PrintOptions
{
    public required string PaperName { get; init; }

    public int Copies { get; init; } = 1;

    public PageRange? Range { get; init; }

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    public ScaleMode ScaleMode { get; init; } = ScaleMode.ActualSize;

    public int? ScalePercent { get; init; }

    public int PagesPerSheet { get; init; } = 1;

    // Consent for this job only; never a destination default.
    public bool AllowClippingConfirmed { get; init; }

    public static PrintOptions ForPaper(string paperName) => new()
    {
        PaperName = paperName,
        Copies = 1,
        Range = null,
        Orientation = PageOrientation.Portrait,
        ScaleMode = ScaleMode.ActualSize,
        ScalePercent = null,
        PagesPerSheet = 1,
    };

    public string Describe()
    {
        var scale = ScaleMode switch
        {
            ScaleMode.ActualSize => "tamaño real",
            ScaleMode.FitPrintableArea => "ajustar al área imprimible",
            ScaleMode.Percentage => $"{ScalePercent} % del tamaño físico del PDF",
            _ => ScaleMode.ToString(),
        };
        var pages = Range is null ? "todas las páginas" : $"páginas {Range.FirstPage}-{Range.LastPage}";
        var orientation = Orientation == PageOrientation.Landscape ? "horizontal" : "vertical";
        var clipping = AllowClippingConfirmed ? ", posible recorte confirmado para este trabajo" : "";
        return $"{PaperName}, {Copies} copias, {pages}, {orientation}, {scale}, una página por hoja{clipping}";
    }
}
