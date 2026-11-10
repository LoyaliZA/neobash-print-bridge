using System.Collections.ObjectModel;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class DeviceCardModel
{
    public required string QueueName { get; init; }

    public required string KindLabel { get; init; }

    public required string Status { get; init; }

    public required string Art { get; init; }

    public bool IsVirtual { get; init; }
}

public sealed class HomeViewModel : Bindable
{
    private string _title = HomeBanner.NeutralTitle;
    private string _subtitle = HomeBanner.NeutralSubtitle;
    private string _status = "Cargando colas…";

    public string Title { get => _title; private set => Set(ref _title, value); }

    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    public string Status { get => _status; private set => Set(ref _status, value); }

    public ObservableCollection<DeviceCardModel> Physical { get; } = [];

    public ObservableCollection<DeviceCardModel> Virtual { get; } = [];

    public void Refresh(DesktopSession session)
    {
        Status = session.Status;
        var installed = session.Installed;
        var active = session.ActiveDestination;
        InstalledQueue? activeQueue = null;
        if (active is not null)
        {
            activeQueue = installed.FirstOrDefault(queue => string.Equals(queue.QueueName, active.QueueName, StringComparison.OrdinalIgnoreCase));
        }

        var failed = session.CatalogFailed;
        Title = HomeBanner.Title(session.CatalogWaiting, failed, active, activeQueue);
        Subtitle = HomeBanner.Subtitle(session.CatalogWaiting, failed, active, activeQueue);
        Replace(Physical, installed.Where(queue => !DevicePresentation.IsKnownVirtual(queue)).Select(queue => Card(queue, DeviceKind.Physical)));
        Replace(Virtual, installed.Where(DevicePresentation.IsKnownVirtual).Select(queue => Card(queue, DeviceKind.Virtual)));
    }

    public static IReadOnlyList<InstalledQueue> ReadInstalled(DesktopSession session) => session.Installed;

    public static DeviceCardModel Card(InstalledQueue queue, DeviceKind kind)
    {
        var art = DevicePresentation.Art(queue, kind);
        return new DeviceCardModel
        {
            QueueName = queue.QueueName,
            KindLabel = kind == DeviceKind.Virtual ? "Impresora virtual" : "Impresora física",
            Status = DevicePresentation.Status(queue, kind, true),
            Art = art.ToString(),
            IsVirtual = kind == DeviceKind.Virtual,
        };
    }

    private static void Replace(ObservableCollection<DeviceCardModel> target, IEnumerable<DeviceCardModel> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
