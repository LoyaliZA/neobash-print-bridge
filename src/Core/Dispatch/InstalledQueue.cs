namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record InstalledQueue(
    string QueueName,
    string DriverName,
    IReadOnlyList<string> PaperNames,
    int MaximumCopies = 1,
    bool SupportsLandscape = false,
    bool SupportsPortrait = true);
