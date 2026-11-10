namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record SavedQueueChoice(InstalledQueue? Queue, string Message, bool ApplySavedPaper)
{
    public bool KeepsSavedQueue => Queue is not null;
}

public static class SavedQueueSelection
{
    public static SavedQueueChoice Match(string? savedQueueName, IReadOnlyList<InstalledQueue> queues, string? savedPaperName = null)
    {
        if (string.IsNullOrWhiteSpace(savedQueueName))
        {
            var anyPhysical = queues.Any(queue => !IsBlockedVirtual(queue));
            return new SavedQueueChoice(
                null,
                anyPhysical
                    ? "Elige una cola y un papel, y guarda el destino."
                    : "No hay colas físicas en el catálogo.",
                false);
        }

        var match = queues.FirstOrDefault(queue => string.Equals(queue.QueueName, savedQueueName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return new SavedQueueChoice(null, $"La cola guardada «{savedQueueName}» no está en el catálogo. No se selecciona otra.", false);
        }

        if (IsBlockedVirtual(match))
        {
            return new SavedQueueChoice(null, $"La cola guardada «{savedQueueName}» es virtual y no puede ser destino físico. No se selecciona otra.", false);
        }

        return new SavedQueueChoice(match, "", PaperStillAnnounced(match, savedPaperName));
    }

    public static bool IsBlockedVirtual(InstalledQueue queue) =>
        string.Equals(queue.QueueName, PhysicalQueueRules.VirtualQueueName, StringComparison.OrdinalIgnoreCase)
        || (queue.Detail != QueueDetail.NameOnly && PhysicalQueueRules.IsVirtual(queue));

    public static bool PaperStillAnnounced(InstalledQueue queue, string? paperName) =>
        queue.Detail == QueueDetail.Ready
        && !string.IsNullOrWhiteSpace(paperName)
        && queue.PaperNames.Any(paper => string.Equals(paper, paperName, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<InstalledQueue> PhysicalCandidates(IReadOnlyList<InstalledQueue> queues) =>
        queues.Where(queue => !IsBlockedVirtual(queue)).ToList();
}
