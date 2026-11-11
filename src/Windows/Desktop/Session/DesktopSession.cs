using System.Diagnostics;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed class DesktopSession
{
    private readonly DesktopHost _host;
    private readonly IUiQueue _ui;
    private readonly IPreferenceStore? _preferences;
    private readonly List<InstalledQueue> _queues = [];
    private readonly List<string> _papers = [];
    private readonly List<JobRecord> _history = [];
    private readonly List<string> _historyLabels = [];
    private string? _selectedQueue;
    private string? _selectedPaper;
    private bool _namesReady;
    private bool _snapshotReady;
    private bool _printing;
    private bool _catalogWaiting;
    private bool _selectionExplicit;
    private int _namesToken;
    private int _detailToken;
    private int _historyToken;

    public DesktopSession(DesktopHost host, IUiQueue ui, IPreferenceStore? preferences = null)
    {
        _host = host;
        _ui = ui;
        _preferences = preferences;
        Status = "Cargando colas…";
    }

    public IReadOnlyDictionary<string, QueueObservation> Observations { get; private set; } = new Dictionary<string, QueueObservation>();
    public QueueObservation? Observation(string name) => Observations.GetValueOrDefault(name);
    public void ApplyObservations(IReadOnlyDictionary<string, QueueObservation> observations)
    {
        Observations = observations;
        Notify();
    }

    public event Action? Changed;

    public string Status { get; private set; }

    public string? PrintStatus { get; private set; }

    public bool NamesReady => _namesReady;

    public bool SnapshotReady => _snapshotReady;

    public bool CatalogWaiting => _catalogWaiting;

    public bool CanSave { get; private set; }

    public bool CanPrint { get; private set; }

    public string? SelectedQueueName => _selectedQueue;

    public string? SelectedPaper => _selectedPaper;

    public IReadOnlyList<string> Papers => _papers;

    public IReadOnlyList<InstalledQueue> PhysicalQueues => _queues;

    public bool CatalogFailed => _host.Catalog.RefreshError is not null;

    public IReadOnlyList<string> CatalogWarnings() => _host.Catalog.Warnings;

    public void ConfirmPaper(string id, string note)
    {
        _host.Jobs.RecordPhysicalConfirmation(id, note);
        _host.Log.Write($"Confirmación física anotada {id}. No se reimprime.");
        RefreshHistory();
    }

    public DesktopHost SessionHost() => _host;

    public IReadOnlyList<PrintDestination> HostDestinations() => _host.Destinations.List();

    public void SaveDestination(PrintDestination destination)
    {
        _host.Destinations.Save(destination);
        _host.Log.Write($"Destino guardado {destination.Id} {destination.Defaults?.Describe()}.");
        Notify();
    }

    public bool RemoveDestination(string id)
    {
        var removed = _host.Destinations.Remove(id);
        if (removed)
        {
            _host.Log.Write($"Destino eliminado {id}. El historial se conserva.");
        }

        Notify();
        return removed;
    }

    public IReadOnlyList<InstalledQueue> Installed => _host.Catalog.List();

    public IReadOnlyList<JobRecord> History => _history;

    public IReadOnlyList<string> HistoryLabels => _historyLabels;

    public PrintDestination? ActiveDestination => ResolveActive();

    public LocalSetupAssessment Setup =>
        Assess(_catalogWaiting || (!_namesReady && _host.Catalog.RefreshError is null));

    public string? SaveLocalPrinter(string? queueName, string? paperName)
    {
        if (_preferences is null)
        {
            return "No se puede guardar la preferencia de impresora.";
        }

        if (string.IsNullOrWhiteSpace(queueName) || string.IsNullOrWhiteSpace(paperName))
        {
            return "Elige una impresora y un papel anunciado por su controlador.";
        }

        var queue = _host.Catalog.List().FirstOrDefault(item => string.Equals(item.QueueName, queueName, StringComparison.OrdinalIgnoreCase));
        if (queue is null)
        {
            return "Esa cola no está en el catálogo. No se elige otra.";
        }

        var built = LocalSetup.TryBuild(queue, paperName, ResolveActive());
        if (built.Error is not null || built.Destination is null)
        {
            return built.Error ?? "No se guardó la impresora.";
        }

        _host.Destinations.Save(built.Destination);
        var preferences = _preferences.Load();
        _preferences.Save(preferences with { ActiveDestinationId = built.Destination.Id });
        _selectedQueue = built.Destination.QueueName;
        FillPapers(queue);
        _selectedPaper = built.Destination.PaperName;
        Status = AppendWarnings(Describe(built.Destination));
        UpdateCommands();
        Notify();
        _host.Log.Write($"Impresora de NPB guardada {built.Destination.Id} {built.Destination.QueueName} {built.Destination.PaperName}.");
        return null;
    }

    public void SetPrinting(bool printing)
    {
        _printing = printing;
        UpdateCommands();
        Notify();
    }

    public void BeginStartup()
    {
        _ = RecoverAsync();
        _ = LoadNamesAsync();
        RefreshHistory();
    }

    public async Task RecoverAsync()
    {
        var started = Stopwatch.GetTimestamp();
        IReadOnlyList<string> ids = [];
        try
        {
            ids = await Task.Run(() => _host.Jobs.ListIds());
            _snapshotReady = true;
            UpdateCommands();
            await Task.Run(() => JobRecovery.MarkInterruptedAsUncertain(_host.Jobs, ids));
            _host.Log.Timing(
                "job-recovery",
                Stopwatch.GetElapsedTime(started),
                ("baseline", "snapshot"),
                ("count", ids.Count.ToString()),
                ("result", "ready"));
        }
        catch (Exception exception)
        {
            _snapshotReady = true;
            UpdateCommands();
            _host.Log.Write("No se pudo recuperar trabajos interrumpidos. " + exception.Message);
        }
        finally
        {
            RefreshHistory();
        }
    }

    public async Task LoadNamesAsync()
    {
        var token = ++_namesToken;
        ++_detailToken;
        _namesReady = false;
        _catalogWaiting = true;
        UpdateCommands();
        Status = "Cargando colas…";
        Notify();
        var started = Stopwatch.GetTimestamp();
        var timer = StartWait(() =>
        {
            if (token != _namesToken)
            {
                return;
            }

            Status = CatalogWaitText.NamesWaiting;
            _host.Log.Timing(
                "queue-name-enumeration",
                Stopwatch.GetElapsedTime(started),
                ("baseline", "ui-wait"),
                ("result", "waiting"));
            Notify();
        });
        try
        {
            await _host.Catalog.RefreshNamesAsync();
            timer.Stop();
            if (token != _namesToken)
            {
                return;
            }

            ApplyNames();
        }
        catch (Exception exception)
        {
            timer.Stop();
            if (token != _namesToken)
            {
                return;
            }

            _catalogWaiting = false;
            Status = "No se pudo leer el catálogo de colas. " + exception.Message;
            _host.Log.Write("Catálogo no leído. " + exception.Message);
            UpdateCommands();
            Notify();
        }
    }

    public void SelectQueue(string? queueName)
    {
        _selectionExplicit = true;
        ++_detailToken;
        _selectedQueue = queueName;
        var queue = SelectedQueue();
        if (queue is null)
        {
            _papers.Clear();
            _selectedPaper = null;
            UpdateCommands();
            Notify();
            return;
        }

        if (queue.Detail == QueueDetail.Ready)
        {
            FillPapers(queue);
            Status = AppendWarnings($"Cola «{queue.QueueName}». Elige un papel anunciado por esa cola.");
            UpdateCommands();
            Notify();
            return;
        }

        _ = LoadDetailsAsync(queue.QueueName);
    }

    public void SelectPaper(string? paper)
    {
        _selectedPaper = paper;
        UpdateCommands();
        Notify();
    }

    public async Task LoadDetailsAsync(string queueName)
    {
        var token = ++_detailToken;
        CanSave = false;
        CanPrint = false;
        Status = "Leyendo los papeles que anuncia esta cola…";
        Notify();
        var started = Stopwatch.GetTimestamp();
        var timer = StartWait(() =>
        {
            if (!IsCurrentDetail(token, queueName))
            {
                return;
            }

            Status = CatalogWaitText.CapabilitiesWaiting(queueName);
            _host.Log.Timing(
                "queue-capabilities",
                Stopwatch.GetElapsedTime(started),
                ("baseline", "ui-wait"),
                ("queue", queueName),
                ("result", "waiting"));
            Notify();
        });
        try
        {
            var read = await _host.Catalog.ReadDetailsAsync(queueName);
            timer.Stop();
            if (!IsCurrentDetail(token, queueName))
            {
                return;
            }

            var updated = _host.Catalog.List().FirstOrDefault(queue => string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase));
            var index = _queues.FindIndex(queue => string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase));
            if (updated is not null && index >= 0)
            {
                _queues[index] = updated;
            }

            if (read is null || updated is not { Detail: QueueDetail.Ready })
            {
                _papers.Clear();
                _selectedPaper = null;
                Status = AppendWarnings($"No se pudo leer la cola «{queueName}». No se selecciona otra.");
                UpdateCommands();
                Notify();
                return;
            }

            FillPapers(read);
            var saved = ResolveActive();
            var setup = Assess(catalogWaiting: false);
            var sameSaved = saved is not null && string.Equals(saved.QueueName, queueName, StringComparison.OrdinalIgnoreCase);
            if (sameSaved && setup.Kind == LocalSetupKind.PrinterChanged && !_selectionExplicit)
            {
                _selectedQueue = null;
                _papers.Clear();
                _selectedPaper = null;
                Status = AppendWarnings(setup.Message);
            }
            else if (sameSaved && setup.Kind is LocalSetupKind.PrinterChanged or LocalSetupKind.PaperNotAnnounced or LocalSetupKind.OptionsNeedReview)
            {
                _selectedPaper = null;
                Status = AppendWarnings(setup.Message);
            }
            else if (sameSaved && SavedQueueSelection.PaperStillAnnounced(read, saved!.PaperName) && setup.Kind == LocalSetupKind.Ready)
            {
                _selectedPaper = _papers.FirstOrDefault(paper => string.Equals(paper, saved.PaperName, StringComparison.OrdinalIgnoreCase));
                Status = AppendWarnings(Describe(saved));
            }
            else
            {
                Status = AppendWarnings("Elige un papel anunciado por esa cola.");
            }

            UpdateCommands();
            Notify();
        }
        catch (Exception exception)
        {
            timer.Stop();
            if (!IsCurrentDetail(token, queueName))
            {
                return;
            }

            Status = AppendWarnings($"No se pudo leer la cola «{queueName}». {exception.Message}");
            UpdateCommands();
            Notify();
        }
    }

    public void RefreshHistory() => _ = LoadHistoryAsync(null);

    public Task LoadHistoryAsync(string? selected) => LoadHistoryCoreAsync(selected);

    private void ApplyNames()
    {
        _catalogWaiting = false;
        if (_host.Catalog.RefreshError is not null)
        {
            _namesReady = false;
            Status = "No se pudo leer el catálogo de colas. " + _host.Catalog.RefreshError;
            _host.Log.Timing(
                "catalog-names-applied",
                StartupClock.SinceManaged(),
                ("baseline", "managed-entry"),
                ("result", "unreadable"),
                ("count", "0"));
            UpdateCommands();
            Notify();
            return;
        }

        _selectionExplicit = false;
        var installed = _host.Catalog.List();
        _queues.Clear();
        _queues.AddRange(SavedQueueSelection.PhysicalCandidates(installed));
        var saved = ResolveActive();
        var choice = SavedQueueSelection.Match(saved?.QueueName, installed, saved?.PaperName);
        _papers.Clear();
        _selectedPaper = null;
        var setup = Assess(catalogWaiting: false);
        if (choice.Queue is null)
        {
            _selectedQueue = null;
            Status = AppendWarnings(setup.Kind is LocalSetupKind.NeedsFirstSetup or LocalSetupKind.PrinterMissing or LocalSetupKind.PrinterChanged
                ? setup.Message
                : choice.Message);
        }
        else if (setup.Kind == LocalSetupKind.PrinterChanged)
        {
            _selectedQueue = null;
            Status = AppendWarnings(setup.Message);
        }
        else
        {
            _selectedQueue = choice.Queue.QueueName;
            if (choice.Queue.Detail == QueueDetail.Ready)
            {
                FillPapers(choice.Queue);
                if (setup.Kind is LocalSetupKind.PaperNotAnnounced or LocalSetupKind.OptionsNeedReview)
                {
                    Status = AppendWarnings(setup.Message);
                }
                else if (choice.ApplySavedPaper)
                {
                    _selectedPaper = _papers.FirstOrDefault(paper => string.Equals(paper, saved?.PaperName, StringComparison.OrdinalIgnoreCase));
                    Status = AppendWarnings(saved is null ? choice.Message : Describe(saved));
                }
                else
                {
                    Status = AppendWarnings(string.IsNullOrWhiteSpace(choice.Message) ? "Elige un papel anunciado por esa cola." : choice.Message);
                }
            }
            else if (choice.Queue.Detail == QueueDetail.Unreadable)
            {
                Status = AppendWarnings(setup.Kind == LocalSetupKind.CapabilitiesUnreadable
                    ? setup.Message
                    : $"{DevicePresentation.CapabilitiesUnreadable}. La cola «{choice.Queue.QueueName}» se conserva. No se selecciona otra.");
            }
            else
            {
                Status = "Leyendo los papeles que anuncia esta cola…";
                _ = LoadDetailsAsync(choice.Queue.QueueName);
            }
        }

        _namesReady = true;
        _host.Log.Timing(
            "catalog-names-applied",
            StartupClock.SinceManaged(),
            ("baseline", "managed-entry"),
            ("result", "ready"),
            ("count", _queues.Count.ToString()));
        UpdateCommands();
        Notify();
    }

    private async Task LoadHistoryCoreAsync(string? selected)
    {
        var started = Stopwatch.GetTimestamp();
        var token = ++_historyToken;
        try
        {
            var records = await Task.Run(() =>
            {
                if (_host.Jobs is FileJobRecordStore store)
                {
                    store.RebuildFromDisk();
                }

                return _host.Jobs.List().OrderByDescending(record => record.ReceivedAt).ToList();
            });
            _ui.TryEnqueue(() =>
            {
                if (token != _historyToken)
                {
                    return;
                }

                _history.Clear();
                _history.AddRange(records);
                _historyLabels.Clear();
                foreach (var record in records)
                {
                    _historyLabels.Add(HistoryLabel(record));
                }

                if (_host.Jobs is FileJobRecordStore reloaded)
                {
                    foreach (var warning in reloaded.ReadWarnings)
                    {
                        _host.Log.Write(warning);
                    }
                }

                _host.Log.Timing(
                    "history-load",
                    Stopwatch.GetElapsedTime(started),
                    ("baseline", "read"),
                    ("count", records.Count.ToString()),
                    ("result", "ready"));
                Notify();
            });
        }
        catch (Exception exception)
        {
            _ui.TryEnqueue(() =>
            {
                if (token != _historyToken)
                {
                    return;
                }

                PrintStatus = "No se pudo leer el historial. " + exception.Message;
                _host.Log.Write("Historial no leído. " + exception.Message);
                Notify();
            });
        }
    }

    private void FillPapers(InstalledQueue queue)
    {
        _papers.Clear();
        foreach (var paper in queue.PaperNames)
        {
            _papers.Add(paper);
        }
    }

    private bool IsCurrentDetail(int token, string queueName) =>
        token == _detailToken
        && string.Equals(_selectedQueue, queueName, StringComparison.OrdinalIgnoreCase);

    private InstalledQueue? SelectedQueue() =>
        _queues.FirstOrDefault(queue => string.Equals(queue.QueueName, _selectedQueue, StringComparison.OrdinalIgnoreCase));

    private PrintDestination? ResolveActive()
    {
        var saved = _host.Destinations.List();
        var preferences = _preferences?.Load() ?? DesktopPreferences.Default;
        return ActiveDestinationPolicy.Resolve(preferences, saved);
    }

    private void UpdateCommands()
    {
        var catalogFailed = _host.Catalog.RefreshError is not null;
        var queue = SelectedQueue();
        var paperReady = queue is { Detail: QueueDetail.Ready } && _selectedPaper is not null;
        var saved = ResolveActive();
        var destinationMatches = saved is not null
            && queue is not null
            && string.Equals(queue.QueueName, saved.QueueName, StringComparison.OrdinalIgnoreCase);
        var asksAgain = Assess(_catalogWaiting || !_namesReady).AsksForSelection;
        CanSave = _namesReady && paperReady && !catalogFailed && !asksAgain;
        CanPrint = _namesReady && !_printing && _snapshotReady && paperReady && destinationMatches && !catalogFailed && !asksAgain;
    }

    private LocalSetupAssessment Assess(bool catalogWaiting) =>
        LocalSetup.Evaluate(
            _preferences?.Load() ?? DesktopPreferences.Default,
            _host.Destinations.List(),
            _host.Catalog.List(),
            catalogWaiting,
            _host.Catalog.RefreshError is not null);

    private string AppendWarnings(string message)
    {
        if (_host.Catalog.Warnings.Count == 0)
        {
            return message;
        }

        return message + " " + string.Join(" ", _host.Catalog.Warnings);
    }

    private static string Describe(PrintDestination destination)
    {
        var options = destination.Defaults?.Describe();
        return string.IsNullOrWhiteSpace(options)
            ? $"Destino «{destination.DisplayName}» en «{destination.QueueName}»."
            : $"Destino «{destination.DisplayName}». {options}.";
    }

    private static string HistoryLabel(JobRecord record)
    {
        var confirmation = string.IsNullOrWhiteSpace(record.PhysicalConfirmation)
            ? "confirmación física pendiente"
            : "confirmación física: " + record.PhysicalConfirmation;
        var optionsText = record.Options?.Describe() ?? record.PaperName;
        return $"{record.ReceivedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {record.Id} · {JobPhaseText.Describe(record.Phase)} · {record.QueueName} · {optionsText} · {record.Detail} · {confirmation}";
    }

    private WaitNotice StartWait(Action onWait)
    {
        var notice = new WaitNotice();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (notice.Completed)
            {
                return;
            }

            _ui.TryEnqueue(() =>
            {
                if (!notice.Completed)
                {
                    onWait();
                }
            });
        });
        return notice;
    }

    private void Notify() => Changed?.Invoke();

    private sealed class WaitNotice
    {
        public bool Completed { get; private set; }

        public void Stop() => Completed = true;
    }
}
