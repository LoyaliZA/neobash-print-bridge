namespace Neobash.PrintBridge.Core.Dispatch;

public static class JobRecovery
{
    public const string InterruptedDetail = "El proceso se interrumpió antes de conocer el resultado. No se reimprime.";

    public static void MarkInterruptedAsUncertain(IJobRecordStore jobs, IReadOnlyList<string>? onlyIds = null)
    {
        var records = onlyIds is null
            ? jobs.List()
            : onlyIds.Select(jobs.Find).OfType<JobRecord>().ToArray();
        foreach (var record in records)
        {
            if (record.Phase is JobPhase.Received or JobPhase.Prepared)
            {
                jobs.Update(record.Id, JobPhase.Uncertain, InterruptedDetail);
            }
        }
    }
}
