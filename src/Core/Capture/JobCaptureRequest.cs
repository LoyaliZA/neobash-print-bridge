namespace Neobash.PrintBridge.Core.Capture;

public sealed class JobCaptureRequest
{
    public required string PrinterName { get; init; }

    public string? PrinterUri { get; init; }

    public string? SourceApplication { get; init; }

    public required string ContentType { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }
}
