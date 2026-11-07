namespace Neobash.PrintBridge.Core.Dispatch;

public static class JobRecovery
{
    public const string InterruptedDetail = "El proceso se interrumpió antes de conocer el resultado. No se reimprime.";

    public static void MarkInterruptedAsUncertain(IJobRecordStore jobs)
    {
        foreach (var record in jobs.List())
        {
            if (record.Phase is JobPhase.Received or JobPhase.Prepared)
            {
                jobs.Update(record.Id, JobPhase.Uncertain, InterruptedDetail);
            }
        }
    }
}
