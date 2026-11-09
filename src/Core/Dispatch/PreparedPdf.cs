namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record PreparedPdf(string Path, PrintOptions Options, IReadOnlyList<int> Pages);
