namespace Neobash.PrintBridge.Desktop;

public interface IUiQueue
{
    bool TryEnqueue(Action action);
}
