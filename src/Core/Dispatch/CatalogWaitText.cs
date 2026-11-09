namespace Neobash.PrintBridge.Core.Dispatch;

public static class CatalogWaitText
{
    public const string NamesWaiting = "Windows sigue enumerando las colas. La consulta no se ha cancelado.";

    public static string CapabilitiesWaiting(string queueName) =>
        $"La consulta a «{queueName}» sigue en curso. El controlador todavía no ha respondido.";
}
