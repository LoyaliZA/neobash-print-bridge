namespace Neobash.PrintBridge.Core.Dispatch;

public static class HomeBanner
{
    public const string NeutralTitle = "Estas son las impresoras de este equipo";

    public const string ReadyTitle = "Destino configurado";

    public const string NeutralSubtitle = "Una cola instalada no confirma que haya papel.";

    public const string ReadySubtitle = "Se leyeron las capacidades del destino activo. Eso no confirma que haya salido papel.";

    public static bool ClaimsReady(bool catalogWaiting, bool catalogFailed, PrintDestination? active, InstalledQueue? activeQueue) =>
        !catalogWaiting
        && !catalogFailed
        && active is not null
        && activeQueue is { Detail: QueueDetail.Ready }
        && !DevicePresentation.IsKnownVirtual(activeQueue)
        && string.Equals(active.QueueName, activeQueue.QueueName, StringComparison.OrdinalIgnoreCase)
        && LocalSetup.PreferenceHolds(active, activeQueue);

    public static string Title(bool catalogWaiting, bool catalogFailed, PrintDestination? active, InstalledQueue? activeQueue) =>
        ClaimsReady(catalogWaiting, catalogFailed, active, activeQueue) ? ReadyTitle : NeutralTitle;

    public static string Subtitle(bool catalogWaiting, bool catalogFailed, PrintDestination? active, InstalledQueue? activeQueue) =>
        ClaimsReady(catalogWaiting, catalogFailed, active, activeQueue) ? ReadySubtitle : NeutralSubtitle;
}
