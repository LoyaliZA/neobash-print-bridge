namespace Neobash.PrintBridge.Core.Dispatch;

// Independent from catalog readiness. A spooler report never confirms physical paper.
public enum QueueOperation { Unknown, Available, Printing, Attention, Offline, Paused, Pending, Busy }
public enum PhysicalEvidence { Unknown, Available, Offline }
public sealed record QueueObservation(uint Status, uint? PendingJobs, DateTimeOffset ObservedAt,
    string? DriverName = null, string? PortName = null, string? Error = null,
    PhysicalEvidence Physical = PhysicalEvidence.Unknown)
{
    public bool IsFresh(DateTimeOffset now) => now - ObservedAt <= TimeSpan.FromSeconds(45);
    public QueueOperation Operation(DateTimeOffset now)
    {
        if (Error is not null || !IsFresh(now)) return QueueOperation.Unknown;
        if ((Status & 0x80) != 0 || Physical == PhysicalEvidence.Offline) return QueueOperation.Offline;
        // Error, paper, toner, door, memory, intervention, deletion, not available.
        if ((Status & 0x007E_187E) != 0) return QueueOperation.Attention;
        if ((Status & 1) != 0) return QueueOperation.Paused;
        if ((Status & 0x800000) != 0) return QueueOperation.Unknown;
        if ((Status & 0x400) != 0) return QueueOperation.Printing;
        if (PendingJobs > 0) return QueueOperation.Pending;
        if ((Status & (0x4000 | 0x200 | 0x100 | 0x8000 | 0x10000)) != 0) return QueueOperation.Busy;
        if (Physical == PhysicalEvidence.Available) return QueueOperation.Available;
        return QueueOperation.Unknown;
    }

    public string Describe(DateTimeOffset now) => Operation(now) switch
    {
        QueueOperation.Offline => "Sin conexión · Windows",
        QueueOperation.Attention when (Status & 8) != 0 => "Papel atascado · Windows",
        QueueOperation.Attention when (Status & 16) != 0 => "Sin papel · Windows",
        QueueOperation.Attention => "Requiere atención · Windows",
        QueueOperation.Paused => "Cola pausada · Windows",
        QueueOperation.Printing => "Imprimiendo · Windows",
        QueueOperation.Busy => "Ocupada · Windows",
        QueueOperation.Pending => $"{PendingJobs} trabajos pendientes",
        QueueOperation.Available => "Disponible · verificada",
        _ => "Estado no confirmado",
    };

    public static int SortOrder(QueueOperation operation) => operation switch
    {
        QueueOperation.Available => 0, QueueOperation.Printing => 1, QueueOperation.Attention => 2,
        QueueOperation.Paused => 3, QueueOperation.Pending => 4, QueueOperation.Busy => 4, QueueOperation.Unknown => 5, _ => 6,
    };
}
