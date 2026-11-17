using System.Collections.ObjectModel;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed record DeviceCardModel
{
    public string QueueName { get; init; } = "";

    public string KindLabel { get; init; } = "";

    public string Status { get; init; } = "Estado no confirmado";

    public string Art { get; init; } = "Physical";

    public string Tone { get; init; } = "Unknown";
    public int SortOrder { get; init; }

    public bool IsSoftware { get; init; }
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

    public ObservableCollection<DeviceCardModel> Software { get; } = [];

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
        var setup = session.Setup;
        var attention = setup.Kind is LocalSetupKind.NeedsFirstSetup
            or LocalSetupKind.PrinterMissing
            or LocalSetupKind.PrinterChanged
            or LocalSetupKind.PaperNotAnnounced
            or LocalSetupKind.OptionsNeedReview
            or LocalSetupKind.CapabilitiesUnreadable;
        if (attention && !string.IsNullOrWhiteSpace(setup.Title))
        {
            Title = setup.Title;
            Subtitle = setup.Message;
        }
        else
        {
            Title = HomeBanner.Title(session.CatalogWaiting, failed, active, activeQueue);
            Subtitle = HomeBanner.Subtitle(session.CatalogWaiting, failed, active, activeQueue);
        }
        var cards = installed.Select(queue => Card(queue, DevicePresentation.Kind(queue, session.Observation(queue.QueueName)), session.Observation(queue.QueueName)))
            .OrderBy(card => card.SortOrder).ThenBy(card => card.QueueName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Replace(Physical, cards.Where(card => !card.IsVirtual && !card.IsSoftware));
        Replace(Software, cards.Where(card => card.IsSoftware));
        Replace(Virtual, cards.Where(card => card.IsVirtual));
    }

    public static IReadOnlyList<InstalledQueue> ReadInstalled(DesktopSession session) => session.Installed;

    public static DeviceCardModel Card(InstalledQueue queue, DeviceKind kind, QueueObservation? observation = null)
    {
        var art = DevicePresentation.Art(queue, kind);
        return new DeviceCardModel
        {
            QueueName = queue.QueueName,
            KindLabel = DevicePresentation.KindLabel(kind),
            Status = observation?.Describe(DateTimeOffset.UtcNow) ?? "Estado no confirmado",
            Tone = (observation?.Operation(DateTimeOffset.UtcNow) ?? QueueOperation.Unknown).ToString(),
            SortOrder = QueueObservation.SortOrder(observation?.Operation(DateTimeOffset.UtcNow) ?? QueueOperation.Unknown),
            Art = art.ToString(),
            IsSoftware = kind == DeviceKind.Software,
            IsVirtual = kind == DeviceKind.Virtual,
        };
    }

    public static void Replace(ObservableCollection<DeviceCardModel> target, IEnumerable<DeviceCardModel> items)
    {
        var snapshot = items.ToArray();
        if (target.SequenceEqual(snapshot)) return;
        target.Clear();
        foreach (var item in snapshot)
        {
            target.Add(item);
        }
    }
}
