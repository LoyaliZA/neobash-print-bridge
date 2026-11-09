namespace Neobash.PrintBridge.Core.Dispatch;

public interface IPrinterProbe
{
    IReadOnlyList<string> ListNames();

    /// <summary>
    /// Lee una sola cola. Devuelve null si ese nombre ya no existe.
    /// Lanza si el controlador de esa cola falla; el catálogo conserva las demás.
    /// </summary>
    InstalledQueue? ReadDetails(string queueName);
}
