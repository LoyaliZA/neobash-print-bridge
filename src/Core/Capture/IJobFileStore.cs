namespace Neobash.PrintBridge.Core.Capture;

public interface IJobFileStore
{
    IJobWorkspace Open(string jobId);
}
