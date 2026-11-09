namespace Neobash.PrintBridge.Core.Dispatch;

public interface ISpoolerPort
{
    Task SubmitAsync(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken);
}
