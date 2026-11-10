namespace Neobash.PrintBridge.Core.Dispatch;

public enum DeviceKind
{
    Physical,
    Virtual,
    Software,
    Unknown,
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
        if (kind is DeviceKind.Virtual or DeviceKind.Software)
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

    public static DeviceKind Kind(InstalledQueue queue, QueueObservation? observation = null)
    {
        if (IsKnownVirtual(queue)) return DeviceKind.Virtual;
        var driver = observation?.DriverName ?? queue.DriverName;
        var name = queue.QueueName + " " + driver;
        if (new[] { "Microsoft Print to PDF", "Microsoft XPS", "OneNote", "PDFCreator", "PDF24", "Adobe PDF", "Fax" }
            .Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase))) return DeviceKind.Software;
        var port = observation?.PortName ?? "";
        if (port.Contains("PORTPROMPT", StringComparison.OrdinalIgnoreCase) || port.Equals("FILE:", StringComparison.OrdinalIgnoreCase))
            return DeviceKind.Software;
        if (port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) || port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase)
            || port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) || port.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
            || port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return DeviceKind.Physical;
        return DeviceKind.Unknown;
    }

    public static string KindLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.Virtual => "Cola virtual NPB",
        DeviceKind.Software => "Impresora de software",
        DeviceKind.Physical => "Impresora física",
        _ => "Cola · tipo sin verificar",
    };

    public static bool IsKnownVirtual(InstalledQueue queue) =>
        string.Equals(queue.QueueName, PhysicalQueueRules.VirtualQueueName, StringComparison.OrdinalIgnoreCase)
        || (queue.Detail != QueueDetail.NameOnly && PhysicalQueueRules.IsVirtual(queue));
}
