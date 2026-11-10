namespace Neobash.PrintBridge.Core.Dispatch;

public enum DeviceKind
{
    Physical,
    Virtual,
}

public enum DeviceArtCategory
{
    Physical,
    Receipt,
    Virtual,
}

public static class DevicePresentation
{
    public const string InstalledQueue = "Cola instalada";

    public const string CapabilitiesRead = "Capacidades leídas";

    public const string CapabilitiesUnreadable = "No se pudieron leer las capacidades";

    public const string Unavailable = "No disponible";

    public const string VirtualRegistered = "Registrada";

    public const string VirtualUnchecked = "No comprobada";

    public static string Status(InstalledQueue? queue, DeviceKind kind, bool seenThisRefresh)
    {
        if (!seenThisRefresh || queue is null)
        {
            return kind == DeviceKind.Virtual ? VirtualUnchecked : Unavailable;
        }

        if (kind == DeviceKind.Virtual && queue.Detail == QueueDetail.NameOnly)
        {
            return VirtualRegistered;
        }

        return queue.Detail switch
        {
            QueueDetail.Ready => CapabilitiesRead,
            QueueDetail.Unreadable => CapabilitiesUnreadable,
            QueueDetail.NameOnly => kind == DeviceKind.Virtual ? VirtualRegistered : InstalledQueue,
            _ => Unavailable,
        };
    }

    public static DeviceArtCategory Art(InstalledQueue? queue, DeviceKind kind)
    {
        if (kind == DeviceKind.Virtual)
        {
            return DeviceArtCategory.Virtual;
        }

        if (queue is { Detail: QueueDetail.Ready }
            && queue.PaperNames.Any(paper =>
                paper.Contains("rollo", StringComparison.OrdinalIgnoreCase)
                || paper.Contains("receipt", StringComparison.OrdinalIgnoreCase)))
        {
            return DeviceArtCategory.Receipt;
        }

        return DeviceArtCategory.Physical;
    }

    public static bool IsKnownVirtual(InstalledQueue queue) =>
        string.Equals(queue.QueueName, PhysicalQueueRules.VirtualQueueName, StringComparison.OrdinalIgnoreCase)
        || (queue.Detail != QueueDetail.NameOnly && PhysicalQueueRules.IsVirtual(queue));
}
