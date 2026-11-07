namespace Neobash.PrintBridge.Core.Dispatch;

public interface IPrinterCatalog
{
    InstalledQueue? Find(string queueName);

    IReadOnlyList<InstalledQueue> List();
}
