namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record PrintJob(
    string Id,
    string MediaType,
    string LocalPath,
    string DestinationId,
    string? PaperName,
    PrintOptions? Options = null);
