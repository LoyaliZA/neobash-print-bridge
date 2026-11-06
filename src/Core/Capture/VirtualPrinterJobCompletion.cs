namespace Neobash.PrintBridge.Core.Capture;

public sealed class VirtualPrinterJobCompletion
{
    private readonly CompletionGate _job = new();
    private readonly CompletionGate _deferral = new();
    private int _canceled;

    public bool IsCanceled => Volatile.Read(ref _canceled) == 1;

    public void MarkCanceled() => Interlocked.Exchange(ref _canceled, 1);

    public bool TryCompleteDeferral() => _deferral.TryEnter();

    public PrintJobOutcome Resolve(JobCaptureStatus status)
    {
        if (!_job.TryEnter())
        {
            return PrintJobOutcome.AlreadyCompleted;
        }

        if (IsCanceled || status == JobCaptureStatus.Canceled)
        {
            return PrintJobOutcome.Canceled;
        }

        return status == JobCaptureStatus.Succeeded
            ? PrintJobOutcome.Succeeded
            : PrintJobOutcome.Failed;
    }
}
