using System.Collections.Concurrent;
using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Desktop;

namespace Neobash.PrintBridge.Tests;

public sealed class LocalSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "npb-c1-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void First_setup_asks_for_a_queue_and_does_not_pick_one()
    {
        var queues = new[]
        {
            new InstalledQueue("Cola de mostrador", "Controlador", ["Hoja A4"]),
            new InstalledQueue("Otra cola", "Otro", ["Sobre"]),
        };

        var setup = LocalSetup.Evaluate(DesktopPreferences.Default, [], queues, catalogWaiting: false, catalogFailed: false);

        Assert.Equal(LocalSetupKind.NeedsFirstSetup, setup.Kind);
        Assert.True(setup.AsksForSelection);
        Assert.Null(setup.KeptQueueName);
        Assert.Equal(2, LocalSetup.CandidateQueues(queues).Count);
    }

    [Fact]
    public void Saved_preference_reloads_when_the_announced_paper_remains()
    {
        var preferences = new FilePreferenceStore(_root);
        var destinations = new FileDestinationStore(_root);
        var queue = Queue("Cola de mostrador", "Controlador de mostrador", "Hoja A4", "Sobre");
        var built = LocalSetup.TryBuild(queue, "hoja a4", null);

        Assert.Null(built.Error);
        Assert.Equal("Hoja A4", built.Destination!.PaperName);
        Assert.Equal("Controlador de mostrador", built.Destination.DriverName);
        destinations.Save(built.Destination);
        preferences.Save(new DesktopPreferences(built.Destination.Id, AppearancePreference.Light));

        var loaded = LocalSetup.Evaluate(
            new FilePreferenceStore(_root).Load(),
            new FileDestinationStore(_root).List(),
            [queue],
            catalogWaiting: false,
            catalogFailed: false);

        Assert.Equal(LocalSetupKind.Ready, loaded.Kind);
        Assert.False(loaded.AsksForSelection);
        Assert.Equal("Cola de mostrador", loaded.KeptQueueName);
        Assert.Contains("Hoja A4", loaded.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("disponible", loaded.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unannounced_paper_is_not_invented()
    {
        var queue = Queue("Cola de mostrador", "Controlador", "Hoja A4");
        var before = LocalSetup.AnnouncedPapers(queue).ToArray();

        var rejected = LocalSetup.TryBuild(queue, "80 mm", null);
        var unread = LocalSetup.TryBuild(queue with { Detail = QueueDetail.NameOnly, PaperNames = [] }, "Hoja A4", null);
        var virtualQueue = new InstalledQueue(PhysicalQueueRules.VirtualQueueName, "Microsoft Virtual Print Class Driver", ["Hoja A4"]);

        Assert.Equal("El papel elegido no está entre los que anuncia esa cola.", rejected.Error);
        Assert.Null(rejected.Destination);
        Assert.Equal(before, LocalSetup.AnnouncedPapers(queue));
        Assert.DoesNotContain(LocalSetup.AnnouncedPapers(queue), paper => paper.Contains("80", StringComparison.OrdinalIgnoreCase));
        Assert.Null(unread.Destination);
        Assert.Contains("No se inventa un tamaño", unread.Error, StringComparison.Ordinal);
        Assert.Empty(LocalSetup.AnnouncedPapers(queue with { Detail = QueueDetail.NameOnly }));
        Assert.Null(LocalSetup.TryBuild(virtualQueue, "Hoja A4", null).Destination);
        Assert.DoesNotContain(LocalSetup.CandidateQueues([queue, virtualQueue]), item => item.QueueName == virtualQueue.QueueName);
    }

    [Fact]
    public void Missing_changed_or_unsupported_printer_keeps_the_saved_destination()
    {
        var saved = new PrintDestination("pref", "Mostrador", "Cola de mostrador", "Hoja A4", PrintOptions.ForPaper("Hoja A4"), "Controlador viejo");
        var preferences = new DesktopPreferences("pref", AppearancePreference.Light);
        var other = Queue("Otra cola", "Otro", "Hoja A4");

        var missing = LocalSetup.Evaluate(preferences, [saved], [other], false, false);
        Assert.Equal(LocalSetupKind.PrinterMissing, missing.Kind);
        Assert.True(missing.AsksForSelection);
        Assert.Null(missing.KeptQueueName);
        Assert.Contains("Cola de mostrador", missing.Message, StringComparison.Ordinal);
        Assert.Contains("silencio", missing.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Otra cola", missing.Message, StringComparison.Ordinal);

        var changed = LocalSetup.Evaluate(preferences, [saved], [Queue("Cola de mostrador", "Controlador nuevo", "Hoja A4")], false, false);
        Assert.Equal(LocalSetupKind.PrinterChanged, changed.Kind);
        Assert.Null(changed.KeptQueueName);
        Assert.Contains("silencio", changed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Otra cola", changed.Message, StringComparison.Ordinal);

        var paperGone = LocalSetup.Evaluate(preferences, [saved], [Queue("Cola de mostrador", "Controlador viejo", "Sobre")], false, false);
        Assert.Equal(LocalSetupKind.PaperNotAnnounced, paperGone.Kind);
        Assert.Equal("Cola de mostrador", paperGone.KeptQueueName);
        Assert.Contains("silencio", paperGone.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Sobre", paperGone.Message, StringComparison.Ordinal);
        Assert.Equal("Hoja A4", saved.PaperName);

        var pending = LocalSetup.Evaluate(preferences, [saved], [new InstalledQueue("Cola de mostrador", "", [], Detail: QueueDetail.NameOnly)], false, false);
        Assert.Equal(LocalSetupKind.CapabilitiesPending, pending.Kind);
        Assert.False(pending.AsksForSelection);

        var waiting = LocalSetup.Evaluate(preferences, [saved], [], catalogWaiting: true, catalogFailed: false);
        Assert.Equal(LocalSetupKind.CatalogPending, waiting.Kind);
        Assert.False(waiting.AsksForSelection);

        var failed = LocalSetup.Evaluate(preferences, [saved], [], catalogWaiting: false, catalogFailed: true);
        Assert.Equal(LocalSetupKind.CatalogFailed, failed.Kind);
        Assert.False(failed.AsksForSelection);
        Assert.Contains("se conserva", failed.Message, StringComparison.Ordinal);

        Assert.Equal(QueueOperation.Unknown, new QueueObservation(0, 0, DateTimeOffset.UtcNow).Operation(DateTimeOffset.UtcNow));
        Assert.Equal(HomeBanner.NeutralTitle, HomeBanner.Title(false, false, saved, Queue("Cola de mostrador", "Controlador nuevo", "Hoja A4")));
        Assert.Equal(HomeBanner.NeutralTitle, HomeBanner.Title(false, false, saved, Queue("Cola de mostrador", "Controlador viejo", "Sobre")));
    }

    [Fact]
    public async Task Startup_stays_pending_and_a_missing_queue_is_not_replaced()
    {
        var probe = new Probe { BlockNames = true, Names = ["Otra cola"] };
        probe.Details["Otra cola"] = Queue("Otra cola", "Otro", "Hoja A4");
        var preferences = new FilePreferenceStore(_root);
        var destinations = new FileDestinationStore(_root);
        destinations.Save(new PrintDestination("pref", "Mostrador", "Cola de mostrador", "Hoja A4", PrintOptions.ForPaper("Hoja A4"), "Controlador"));
        preferences.Save(new DesktopPreferences("pref", AppearancePreference.Light));
        var session = Open(probe, preferences, destinations);

        session.BeginStartup();
        try
        {
            Assert.True(session.CatalogWaiting);
            Assert.False(session.NamesReady);
            Assert.Equal(LocalSetupKind.CatalogPending, session.Setup.Kind);
            Assert.False(session.Setup.AsksForSelection);
        }
        finally
        {
            probe.ReleaseNames.TrySetResult();
        }

        await session.LoadNamesAsync();

        Assert.Null(session.SelectedQueueName);
        Assert.Equal(LocalSetupKind.PrinterMissing, session.Setup.Kind);
        Assert.Equal("pref", new FilePreferenceStore(_root).Load().ActiveDestinationId);
        Assert.Equal("Cola de mostrador", new FileDestinationStore(_root).Find("pref")!.QueueName);
        Assert.DoesNotContain("Otra cola", session.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_rejects_an_unannounced_paper_and_keeps_the_previous_preference()
    {
        var probe = new Probe { Names = ["Cola de mostrador"] };
        probe.Details["Cola de mostrador"] = Queue("Cola de mostrador", "Controlador", "Hoja A4");
        var preferences = new FilePreferenceStore(_root);
        var destinations = new FileDestinationStore(_root);
        var session = Open(probe, preferences, destinations);
        await session.LoadNamesAsync();
        await session.LoadDetailsAsync("Cola de mostrador");

        var error = session.SaveLocalPrinter("Cola de mostrador", "80 mm");

        Assert.Contains("no está entre los que anuncia", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(new FilePreferenceStore(_root).Load().ActiveDestinationId);
        Assert.Empty(new FileDestinationStore(_root).List());
        Assert.Equal(["Hoja A4"], LocalSetup.AnnouncedPapers(session.Installed.Single()));

        Assert.Null(session.SaveLocalPrinter("Cola de mostrador", "Hoja A4"));
        var savedId = new FilePreferenceStore(_root).Load().ActiveDestinationId;
        Assert.False(string.IsNullOrWhiteSpace(savedId));
        Assert.Equal("Hoja A4", new FileDestinationStore(_root).Find(savedId!)!.PaperName);

        probe.Details["Cola de mostrador"] = Queue("Cola de mostrador", "Controlador", "Sobre");
        await session.LoadNamesAsync();
        await session.LoadDetailsAsync("Cola de mostrador");

        Assert.Equal(LocalSetupKind.PaperNotAnnounced, session.Setup.Kind);
        Assert.Equal("Cola de mostrador", session.SelectedQueueName);
        Assert.Null(session.SelectedPaper);
        Assert.Equal("Hoja A4", new FileDestinationStore(_root).Find(savedId)!.PaperName);
        Assert.Equal(savedId, new FilePreferenceStore(_root).Load().ActiveDestinationId);
        Assert.Contains("silencio", session.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_changed_driver_is_not_replaced_until_the_selection_is_saved()
    {
        var probe = new Probe { Names = ["Cola de mostrador", "Otra cola"] };
        probe.Details["Cola de mostrador"] = Queue("Cola de mostrador", "Controlador nuevo", "Hoja A4");
        probe.Details["Otra cola"] = Queue("Otra cola", "Otro", "Sobre");
        var preferences = new FilePreferenceStore(_root);
        var destinations = new FileDestinationStore(_root);
        destinations.Save(new PrintDestination("pref", "Mostrador", "Cola de mostrador", "Hoja A4", PrintOptions.ForPaper("Hoja A4"), "Controlador viejo"));
        preferences.Save(new DesktopPreferences("pref", AppearancePreference.Light));
        var session = Open(probe, preferences, destinations);

        await session.LoadNamesAsync();
        await session.LoadDetailsAsync("Cola de mostrador");

        Assert.Equal(LocalSetupKind.PrinterChanged, session.Setup.Kind);
        Assert.Null(session.SelectedQueueName);
        Assert.Equal("Controlador viejo", new FileDestinationStore(_root).Find("pref")!.DriverName);
        Assert.DoesNotContain("Otra cola", session.Status, StringComparison.Ordinal);

        session.SelectQueue("Cola de mostrador");
        await session.LoadDetailsAsync("Cola de mostrador");

        Assert.Equal("Cola de mostrador", session.SelectedQueueName);
        Assert.Equal(["Hoja A4"], session.Papers);
        Assert.Null(session.SelectedPaper);
        Assert.Equal("Controlador viejo", new FileDestinationStore(_root).Find("pref")!.DriverName);
        Assert.False(session.CanPrint);

        Assert.Null(session.SaveLocalPrinter("Cola de mostrador", "Hoja A4"));
        Assert.Equal(LocalSetupKind.Ready, session.Setup.Kind);
        Assert.Equal("Controlador nuevo", new FileDestinationStore(_root).Find("pref")!.DriverName);
        Assert.Equal("pref", new FilePreferenceStore(_root).Load().ActiveDestinationId);
    }

    private DesktopSession Open(Probe probe, IPreferenceStore preferences, IDestinationStore destinations) =>
        new(new DesktopHost(
            destinations,
            new FileJobRecordStore(_root),
            new CachingPrinterCatalog(probe),
            null!,
            new SilentLog()), new UiQueue(), preferences);

    private static InstalledQueue Queue(string name, string driver, params string[] papers) =>
        new(name, driver, papers, MaximumCopies: 9, SupportsLandscape: true);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class UiQueue : IUiQueue
    {
        private readonly ConcurrentQueue<Action> _pending = new();

        public bool TryEnqueue(Action action)
        {
            _pending.Enqueue(action);
            return true;
        }
    }

    private sealed class Probe : IPrinterProbe
    {
        public List<string> Names { get; set; } = [];

        public Dictionary<string, InstalledQueue> Details { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool BlockNames { get; set; }

        public TaskCompletionSource ReleaseNames { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> ListNames()
        {
            if (BlockNames)
            {
                ReleaseNames.Task.GetAwaiter().GetResult();
            }

            return Names;
        }

        public InstalledQueue? ReadDetails(string name) =>
            Details.TryGetValue(name, out var queue) ? queue with { QueueName = name, Detail = QueueDetail.Ready } : null;
    }

    private sealed class SilentLog : IDiagnosticLog
    {
        public void Write(string message)
        {
        }

        public void Timing(string operation, TimeSpan elapsed, params (string Key, string Value)[] fields)
        {
        }
    }
}
