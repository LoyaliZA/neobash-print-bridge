namespace Neobash.PrintBridge.Core.Dispatch;

public interface IDocumentPreparer
{
    Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken);
}
