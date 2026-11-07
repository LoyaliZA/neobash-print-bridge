namespace Neobash.PrintBridge.Core.Dispatch;

public interface ISpoolerPort
{
    void Submit(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken);
}
