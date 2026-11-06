using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Tests;

public sealed class VirtualPrinterJobCompletionTests
{
    [Fact]
    public void Deferral_and_job_completion_happen_once()
    {
        var completion = new VirtualPrinterJobCompletion();

        Assert.True(completion.TryCompleteDeferral());
        Assert.False(completion.TryCompleteDeferral());
        Assert.Equal(PrintJobOutcome.Succeeded, completion.Resolve(JobCaptureStatus.Succeeded));
        Assert.Equal(PrintJobOutcome.AlreadyCompleted, completion.Resolve(JobCaptureStatus.Succeeded));
    }

    [Fact]
    public void Cancellation_prevents_a_successful_report()
    {
        var completion = new VirtualPrinterJobCompletion();
        completion.MarkCanceled();

        Assert.Equal(PrintJobOutcome.Canceled, completion.Resolve(JobCaptureStatus.Succeeded));
    }

    [Fact]
    public void Failed_capture_stays_failed_when_the_task_is_still_active()
    {
        var completion = new VirtualPrinterJobCompletion();

        Assert.Equal(PrintJobOutcome.Failed, completion.Resolve(JobCaptureStatus.Failed));
    }
}
