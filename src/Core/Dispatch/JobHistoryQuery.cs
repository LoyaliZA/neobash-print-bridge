namespace Neobash.PrintBridge.Core.Dispatch;

public static class JobHistoryQuery
{
    public static IReadOnlyList<JobRecord> Apply(
        IEnumerable<JobRecord> records,
        JobPhase? phase,
        string? destinationId,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(records);
        var term = search?.Trim();
        return records
            .Where(record => phase is null || record.Phase == phase)
            .Where(record => string.IsNullOrWhiteSpace(destinationId)
                || string.Equals(record.DestinationId, destinationId, StringComparison.Ordinal))
            .Where(record => string.IsNullOrWhiteSpace(term) || Matches(record, term))
            .OrderByDescending(record => record.ReceivedAt)
            .ToList();
    }

    private static bool Matches(JobRecord record, string term)
    {
        var file = Path.GetFileName(record.LocalPath);
        return Contains(record.Id, term)
            || Contains(file, term)
            || Contains(record.QueueName, term)
            || Contains(record.DestinationId, term);
    }

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
