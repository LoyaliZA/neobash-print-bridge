namespace Neobash.PrintBridge.Core.Dispatch;

public enum JobPhase
{
    Received,
    Prepared,
    SubmittedToSpooler,
    Failed,
    Uncertain,
}
