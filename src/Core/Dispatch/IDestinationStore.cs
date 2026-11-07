namespace Neobash.PrintBridge.Core.Dispatch;

public interface IDestinationStore
{
    void Save(PrintDestination destination);

    PrintDestination? Find(string id);

    IReadOnlyList<PrintDestination> List();
}
