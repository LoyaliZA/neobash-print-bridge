namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record PrintDestination(
    string Id,
    string DisplayName,
    string QueueName,
    string? PaperName,
    PrintOptions? Defaults = null);
