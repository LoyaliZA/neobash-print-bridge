namespace Neobash.PrintBridge.Core.Dispatch;

public interface IJobRecordStore
{
    bool TryBegin(PrintJob job, string queueName);

    void Update(string id, JobPhase phase, string? detail);

    void RecordPhysicalConfirmation(string id, string note);

    JobRecord? Find(string id);

    IReadOnlyList<JobRecord> List();
}
