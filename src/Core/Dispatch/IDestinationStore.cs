namespace Neobash.PrintBridge.Core.Dispatch;

public interface IDestinationStore
{
    void Save(PrintDestination destination);

    bool Remove(string id);

    PrintDestination? Find(string id);

    IReadOnlyList<PrintDestination> List();
}
