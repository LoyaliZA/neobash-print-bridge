namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record InstalledQueue(string QueueName, string DriverName, IReadOnlyList<string> PaperNames);
