namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class JobRecord
{
    public required string Id { get; init; }

    public required string MediaType { get; init; }

    public required string LocalPath { get; init; }

    public required string DestinationId { get; init; }

    public string? PaperName { get; init; }

    public required string QueueName { get; init; }

    public JobPhase Phase { get; set; }

    public string? Detail { get; set; }

    public string? PhysicalConfirmation { get; set; }

    public DateTimeOffset ReceivedAt { get; init; }
}
