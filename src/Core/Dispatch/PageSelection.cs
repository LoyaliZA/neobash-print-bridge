namespace Neobash.PrintBridge.Core.Dispatch;

public static class PageSelection
{
    public static IReadOnlyList<int> Select(int pageCount, PageRange? range)
    {
        if (pageCount < 1)
        {
            throw new InvalidOperationException("El PDF no tiene páginas.");
        }

        if (range is null)
        {
            return Enumerable.Range(1, pageCount).ToArray();
        }

        if (range.FirstPage < 1 || range.LastPage < range.FirstPage || range.LastPage > pageCount)
        {
            throw new InvalidOperationException($"El rango {range.FirstPage}-{range.LastPage} no está dentro de las {pageCount} páginas.");
        }

        return Enumerable.Range(range.FirstPage, range.LastPage - range.FirstPage + 1).ToArray();
    }
}
