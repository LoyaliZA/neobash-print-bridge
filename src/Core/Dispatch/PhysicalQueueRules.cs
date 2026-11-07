namespace Neobash.PrintBridge.Core.Dispatch;

public static class PhysicalQueueRules
{
    public const string VirtualQueueName = "NPB - Prueba Virtual";

    public const string VirtualDriverName = "Microsoft Virtual Print Class Driver";

    public const string PdfMediaType = "application/pdf";

    public static bool IsVirtual(InstalledQueue queue) =>
        string.Equals(queue.QueueName, VirtualQueueName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(queue.DriverName, VirtualDriverName, StringComparison.OrdinalIgnoreCase);

    public static bool IsPdf(string? mediaType) =>
        string.Equals(mediaType, PdfMediaType, StringComparison.OrdinalIgnoreCase);
}
