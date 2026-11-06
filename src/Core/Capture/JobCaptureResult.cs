namespace Neobash.PrintBridge.Core.Capture;

public sealed class JobCaptureResult
{
    public string? JobId { get; init; }

    public required JobCaptureStatus Status { get; init; }

    public string? DirectoryPath { get; init; }

    public string? DocumentPath { get; init; }

    public long SizeBytes { get; init; }

    public string? Error { get; init; }

    public bool OpenXpsStructureValidated => false;
}
