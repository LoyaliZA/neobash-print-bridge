using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neobash.PrintBridge.Core.Dispatch;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DesktopHost _host;
    private readonly List<InstalledQueue> _queues = [];
    private readonly List<JobRecord> _history = [];
    private string? _destinationId;
    private string? _pdfPath;
    private bool _loading;
    private bool _printing;
    private bool _refreshingHistory;
    private bool _snapshotReady;
    private int _historyToken;
    private bool _namesReady;
    private bool _activatedLogged;
    private int _namesToken;
    private int _detailToken;

    public MainWindow(DesktopHost host)
    {
        _host = host;
        InitializeComponent();
        Closed += OnClosed;
        Activated += OnActivated;
        QueueBox.SelectionChanged += OnQueueChanged;
        PaperBox.SelectionChanged += OnPaperChanged;
        DestinationStatus.Text = "Cargando colas…";
        UpdateCommandStates();
    }

    public void BeginStartup()
    {
        _ = RecoverAsync();
        _ = LoadNamesAsync();
        RefreshHistory();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_activatedLogged)
        {
            return;
        }

        _activatedLogged = true;
        _host.Log.Timing("window-activated-event", StartupClock.SinceManaged(), ("baseline", "managed-entry"));
    }

    private async Task RecoverAsync()
    {
        var started = Stopwatch.GetTimestamp();
        IReadOnlyList<string> ids = [];
        try
        {
            ids = await Task.Run(() => _host.Jobs.ListIds());
            _snapshotReady = true;
            UpdateCommandStates();
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
            UpdateCommandStates();
            _host.Log.Write("No se pudo recuperar trabajos interrumpidos. " + exception.Message);
        }
        finally
        {
            // A pre-recovery snapshot must not be the final visible history.
            RefreshHistory();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _host.Log.Write("Ventana cerrada. El proceso termina.");
        Application.Current.Exit();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = LoadNamesAsync();

    private async Task LoadNamesAsync()
    {
        var token = ++_namesToken;
        ++_detailToken;
        _namesReady = false;
        UpdateCommandStates();
        DestinationStatus.Text = "Cargando colas…";
        var started = Stopwatch.GetTimestamp();
        var timer = StartWait(() =>
        {
            if (token != _namesToken)
            {
                return;
            }

            DestinationStatus.Text = CatalogWaitText.NamesWaiting;
            _host.Log.Timing(
                "queue-name-enumeration",
                Stopwatch.GetElapsedTime(started),
                ("baseline", "ui-wait"),
                ("result", "waiting"));
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

            DestinationStatus.Text = "No se pudo leer el catálogo de colas. " + exception.Message;
            _host.Log.Write("Catálogo no leído. " + exception.Message);
            UpdateCommandStates();
        }
    }

    private void ApplyNames()
    {
        if (_host.Catalog.RefreshError is not null)
        {
            _namesReady = false;
            DestinationStatus.Text = "No se pudo leer el catálogo de colas. " + _host.Catalog.RefreshError;
            _host.Log.Timing(
                "catalog-names-applied",
                StartupClock.SinceManaged(),
                ("baseline", "managed-entry"),
                ("result", "unreadable"),
                ("count", "0"));
            UpdateCommandStates();
            return;
        }

        _loading = true;
        try
        {
            var installed = _host.Catalog.List();
            _queues.Clear();
            _queues.AddRange(SavedQueueSelection.PhysicalCandidates(installed));
            QueueBox.Items.Clear();
            PaperBox.Items.Clear();
            foreach (var queue in _queues)
            {
                QueueBox.Items.Add(queue.QueueName);
            }

            var saved = CurrentDestination();
            var choice = SavedQueueSelection.Match(saved?.QueueName, installed, saved?.PaperName);
            if (choice.Queue is null)
            {
                DestinationStatus.Text = AppendWarnings(choice.Message);
            }
            else
            {
                QueueBox.SelectedItem = choice.Queue.QueueName;
                if (choice.Queue.Detail == QueueDetail.Ready)
                {
                    FillPapers(choice.Queue);
                    if (choice.ApplySavedPaper)
                    {
                        SelectPaper(saved?.PaperName);
                        ApplyOptions(saved?.Defaults ?? PrintOptions.ForPaper(saved?.PaperName ?? ""));
                    }

                    DestinationStatus.Text = AppendWarnings(saved is null ? choice.Message : Describe(saved));
                }
                else if (choice.Queue.Detail == QueueDetail.Unreadable)
                {
                    DestinationStatus.Text = AppendWarnings($"No se pudo leer la cola «{choice.Queue.QueueName}». No se selecciona otra.");
                }
                else
                {
                    DestinationStatus.Text = "Leyendo los papeles que anuncia esta cola…";
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
        }
        finally
        {
            _loading = false;
            UpdateCommandStates();
        }
    }

    private void OnQueueChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ++_detailToken;
        var queue = SelectedQueue();
        if (queue is null)
        {
            PaperBox.Items.Clear();
            UpdateCommandStates();
            return;
        }

        if (queue.Detail == QueueDetail.Ready)
        {
            FillPapers(queue);
            DestinationStatus.Text = AppendWarnings($"Cola «{queue.QueueName}». Elige un papel anunciado por esa cola.");
            UpdateCommandStates();
            return;
        }

        _ = LoadDetailsAsync(queue.QueueName);
    }

    private void OnPaperChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateCommandStates();
        }
    }

    private async Task LoadDetailsAsync(string queueName)
    {
        var token = ++_detailToken;
        PaperBox.IsEnabled = false;
        SaveButton.IsEnabled = false;
        PrintButton.IsEnabled = false;
        DestinationStatus.Text = "Leyendo los papeles que anuncia esta cola…";
        var started = Stopwatch.GetTimestamp();
        var timer = StartWait(() =>
        {
            if (!IsCurrentDetail(token, queueName))
            {
                return;
            }

            DestinationStatus.Text = CatalogWaitText.CapabilitiesWaiting(queueName);
            _host.Log.Timing(
                "queue-capabilities",
                Stopwatch.GetElapsedTime(started),
                ("baseline", "ui-wait"),
                ("queue", queueName),
                ("result", "waiting"));
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
                PaperBox.Items.Clear();
                DestinationStatus.Text = AppendWarnings($"No se pudo leer la cola «{queueName}». No se selecciona otra.");
                UpdateCommandStates();
                return;
            }

            _loading = true;
            try
            {
                FillPapers(read);
                var saved = CurrentDestination();
                if (saved is not null
                    && string.Equals(saved.QueueName, queueName, StringComparison.OrdinalIgnoreCase)
                    && SavedQueueSelection.PaperStillAnnounced(read, saved.PaperName))
                {
                    SelectPaper(saved.PaperName);
                    ApplyOptions(saved.Defaults ?? PrintOptions.ForPaper(saved.PaperName ?? ""));
                    DestinationStatus.Text = AppendWarnings(Describe(saved));
                }
                else
                {
                    DestinationStatus.Text = AppendWarnings("Elige un papel anunciado por esa cola.");
                }
            }
            finally
            {
                _loading = false;
            }

            UpdateCommandStates();
        }
        catch (Exception exception)
        {
            timer.Stop();
            if (!IsCurrentDetail(token, queueName))
            {
                return;
            }

            DestinationStatus.Text = AppendWarnings($"No se pudo leer la cola «{queueName}». {exception.Message}");
            UpdateCommandStates();
        }
    }

    private bool IsCurrentDetail(int token, string queueName) =>
        token == _detailToken
        && string.Equals(QueueBox.SelectedItem as string, queueName, StringComparison.OrdinalIgnoreCase);

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

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!notice.Completed)
                {
                    onWait();
                }
            });
        });
        return notice;
    }

    private sealed class WaitNotice
    {
        public bool Completed { get; private set; }

        public void Stop() => Completed = true;
    }

    private string AppendWarnings(string message)
    {
        if (_host.Catalog.Warnings.Count == 0)
        {
            return message;
        }

        return message + " " + string.Join(" ", _host.Catalog.Warnings);
    }

    private void UpdateCommandStates()
    {
        var catalogFailed = _host.Catalog.RefreshError is not null;
        QueueBox.IsEnabled = _namesReady && !catalogFailed;
        var queue = SelectedQueue();
        var paperReady = queue is { Detail: QueueDetail.Ready } && PaperBox.SelectedItem is string;
        PaperBox.IsEnabled = _namesReady && !catalogFailed && queue is { Detail: QueueDetail.Ready };
        SaveButton.IsEnabled = _namesReady && paperReady && !catalogFailed;
        var saved = _destinationId is null ? null : _host.Destinations.Find(_destinationId);
        if (saved is null && _namesReady)
        {
            saved = CurrentDestination();
        }

        var destinationMatches = saved is not null
            && queue is not null
            && string.Equals(queue.QueueName, saved.QueueName, StringComparison.OrdinalIgnoreCase);
        PrintButton.IsEnabled = _namesReady && !_printing && _snapshotReady && paperReady && destinationMatches && !catalogFailed;
    }

    private void FillPapers(InstalledQueue? queue)
    {
        _loading = true;
        try
        {
            PaperBox.Items.Clear();
            if (queue is null)
            {
                return;
            }

            foreach (var paper in queue.PaperNames)
            {
                PaperBox.Items.Add(paper);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void SelectPaper(string? paperName)
    {
        if (string.IsNullOrWhiteSpace(paperName))
        {
            PaperBox.SelectedItem = null;
            return;
        }

        foreach (var item in PaperBox.Items)
        {
            if (string.Equals(item as string, paperName, StringComparison.OrdinalIgnoreCase))
            {
                PaperBox.SelectedItem = item;
                return;
            }
        }

        PaperBox.SelectedItem = null;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var queue = SelectedQueue();
        var paper = PaperBox.SelectedItem as string;
        if (queue is null || string.IsNullOrWhiteSpace(paper))
        {
            DestinationStatus.Text = "Elige una cola y un papel anunciado antes de guardar.";
            return;
        }

        if (!TryReadOptions(paper, out var options, out var readError))
        {
            DestinationStatus.Text = readError;
            return;
        }

        var optionError = PrintOptionRules.Check(options, queue);
        if (optionError is not null)
        {
            DestinationStatus.Text = optionError;
            return;
        }

        _destinationId ??= "dest-" + Guid.NewGuid().ToString("N");
        var destination = new PrintDestination(_destinationId, queue.QueueName, queue.QueueName, paper, options);
        _host.Destinations.Save(destination);
        _host.Log.Write($"Destino guardado {_destinationId} {options.Describe()}.");
        DestinationStatus.Text = Describe(destination);
        UpdateCommandStates();
    }

    private void OnRangeModeChanged(object sender, RoutedEventArgs e) => UpdateRangeBoxes();

    private void OnScaleChanged(object sender, SelectionChangedEventArgs e) => UpdatePercentBox();

    private async void OnPickPdf(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".pdf");
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _pdfPath = file.Path;
        PdfPath.Text = file.Path;
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        if (_printing)
        {
            return;
        }

        var destination = CurrentDestination();
        if (destination is null)
        {
            PrintStatus.Text = "Guarda el destino antes de imprimir.";
            return;
        }

        var queue = SelectedQueue();
        var paper = PaperBox.SelectedItem as string;
        if (queue is null || !string.Equals(queue.QueueName, destination.QueueName, StringComparison.OrdinalIgnoreCase))
        {
            PrintStatus.Text = "La cola en pantalla no coincide con el destino guardado. No se elige otra.";
            return;
        }

        if (!TryReadOptions(paper, out var options, out var readError))
        {
            PrintStatus.Text = readError;
            return;
        }

        if (string.IsNullOrWhiteSpace(_pdfPath) || !File.Exists(_pdfPath))
        {
            PrintStatus.Text = "Elige un PDF que ya exista.";
            return;
        }

        var job = new PrintJob(
            "job-" + Guid.NewGuid().ToString("N"),
            PhysicalQueueRules.PdfMediaType,
            _pdfPath,
            destination.Id,
            options.PaperName,
            options);
        _printing = true;
        PrintButton.IsEnabled = false;
        try
        {
            var result = await _host.Dispatcher.DispatchAsync(job);
            if (result.RequiresClippingConfirmation)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "La página puede quedar recortada",
                    Content = new TextBlock { Text = result.Detail, TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = "Imprimir con posible recorte",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Close,
                };
                var choice = await dialog.ShowAsync();
                if (choice != ContentDialogResult.Primary)
                {
                    PrintStatus.Text = "No se envió el trabajo a la impresora. Puedes elegir Ajustar al área imprimible o cambiar la escala.";
                    return;
                }

                // The rejected attempt never started printing. Explicit consent creates
                // a new, auditable job; it does not bypass identifier deduplication.
                var rejectedId = job.Id;
                job = job with
                {
                    Id = "job-" + Guid.NewGuid().ToString("N"),
                    Options = options with { AllowClippingConfirmed = true },
                };
                _host.Log.Write($"Posible recorte confirmado para {job.Id}; intento previo sin envío {rejectedId}.");
                result = await _host.Dispatcher.DispatchAsync(job);
            }
            var phase = JobPhaseText.Describe(result.Phase);
            PrintStatus.Text = $"Trabajo {job.Id}: {phase}. {result.Detail}";
            _host.Log.Write($"{phase} {job.Id} {result.Detail}");
        }
        catch (Exception exception)
        {
            PrintStatus.Text = $"Trabajo {job.Id}: no se pudo completar el despacho. {exception.Message}";
            _host.Log.Write($"Fallo de interfaz {job.Id} {exception.Message}");
        }
        finally
        {
            _printing = false;
            UpdateCommandStates();
            RefreshHistory();
        }
    }

    private void OnHistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingHistory)
        {
            return;
        }

        UpdateConfirmButton();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var row = SelectedHistory();
        if (row is null)
        {
            return;
        }

        var note = ConfirmationNote.Text?.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            PrintStatus.Text = "Escribe una nota de quien vio el papel.";
            return;
        }

        try
        {
            _host.Jobs.RecordPhysicalConfirmation(row.Id, note);
            _host.Log.Write($"Confirmación física anotada {row.Id}. No se reimprime.");
            ConfirmationNote.Text = "";
            RefreshHistory();
        }
        catch (Exception exception)
        {
            PrintStatus.Text = exception.Message;
        }
    }

    private void RefreshHistory()
    {
        var selected = SelectedHistory()?.Id;
        _ = LoadHistoryAsync(selected);
    }

    private async Task LoadHistoryAsync(string? selected)
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
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token != _historyToken)
                {
                    return;
                }

                ApplyHistory(records, selected, token);
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
            });
        }
        catch (Exception exception)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token != _historyToken)
                {
                    return;
                }

                PrintStatus.Text = "No se pudo leer el historial. " + exception.Message;
                _host.Log.Write("Historial no leído. " + exception.Message);
            });
        }
    }

    private void ApplyHistory(IReadOnlyList<JobRecord> records, string? selected, int token)
    {
        _refreshingHistory = true;
        _history.Clear();
        HistoryList.Items.Clear();
        _history.AddRange(records);
        var rendered = 0;

        void RenderBatch()
        {
            if (token != _historyToken)
            {
                return;
            }

            var take = Math.Min(40, records.Count - rendered);
            for (var index = 0; index < take; index++)
            {
                HistoryList.Items.Add(HistoryLabel(records[rendered + index]));
            }

            rendered += take;
            if (rendered < records.Count)
            {
                DispatcherQueue.TryEnqueue(RenderBatch);
                return;
            }

            var selectedIndex = _history.FindIndex(record => string.Equals(record.Id, selected, StringComparison.Ordinal));
            if (selectedIndex >= 0)
            {
                HistoryList.SelectedIndex = selectedIndex;
            }

            _refreshingHistory = false;
            UpdateConfirmButton();
        }

        RenderBatch();
    }

    private static string HistoryLabel(JobRecord record)
    {
        var confirmation = string.IsNullOrWhiteSpace(record.PhysicalConfirmation)
            ? "confirmación física pendiente"
            : "confirmación física: " + record.PhysicalConfirmation;
        var optionsText = record.Options?.Describe() ?? record.PaperName;
        return $"{record.ReceivedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {record.Id} · {JobPhaseText.Describe(record.Phase)} · {record.QueueName} · {optionsText} · {record.Detail} · {confirmation}";
    }

    private JobRecord? SelectedHistory()
    {
        var index = HistoryList.SelectedIndex;
        return index >= 0 && index < _history.Count ? _history[index] : null;
    }

    private void UpdateConfirmButton()
    {
        ConfirmButton.IsEnabled = SelectedHistory()?.Phase == JobPhase.SubmittedToSpooler;
    }

    private PrintDestination? CurrentDestination()
    {
        var saved = _host.Destinations.List();
        if (_destinationId is null)
        {
            _destinationId = saved.Count == 1 ? saved[0].Id : saved.FirstOrDefault()?.Id;
        }

        return _destinationId is null ? null : _host.Destinations.Find(_destinationId);
    }

    private InstalledQueue? SelectedQueue()
    {
        var name = QueueBox.SelectedItem as string;
        return _queues.FirstOrDefault(queue => string.Equals(queue.QueueName, name, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyOptions(PrintOptions options)
    {
        CopiesBox.Text = options.Copies.ToString();
        AllPagesBox.IsChecked = options.Range is null;
        FirstPageBox.Text = options.Range?.FirstPage.ToString() ?? "";
        LastPageBox.Text = options.Range?.LastPage.ToString() ?? "";
        OrientationBox.SelectedIndex = options.Orientation == PageOrientation.Landscape ? 1 : 0;
        ScaleBox.SelectedIndex = options.ScaleMode switch
        {
            ScaleMode.FitPrintableArea => 1,
            ScaleMode.Percentage => 2,
            _ => 0,
        };
        if (options.ScalePercent is int percent)
        {
            PercentBox.Text = percent.ToString();
        }

        UpdateRangeBoxes();
        UpdatePercentBox();
    }

    private bool TryReadOptions(string? paper, out PrintOptions options, out string error)
    {
        options = PrintOptions.ForPaper(paper ?? "");
        error = "";
        if (string.IsNullOrWhiteSpace(paper))
        {
            error = "Elige un papel anunciado por esa cola.";
            return false;
        }

        if (!int.TryParse(CopiesBox.Text, out var copies))
        {
            error = "Las copias tienen que ser un número entero.";
            return false;
        }

        PageRange? range = null;
        if (AllPagesBox.IsChecked != true)
        {
            if (!int.TryParse(FirstPageBox.Text, out var first) || !int.TryParse(LastPageBox.Text, out var last))
            {
                error = "El rango de páginas tiene que usar números enteros.";
                return false;
            }

            range = new PageRange(first, last);
        }

        var orientation = OrientationBox.SelectedIndex == 1 ? PageOrientation.Landscape : PageOrientation.Portrait;
        var scaleMode = ScaleBox.SelectedIndex switch
        {
            1 => ScaleMode.FitPrintableArea,
            2 => ScaleMode.Percentage,
            _ => ScaleMode.ActualSize,
        };
        int? percent = null;
        if (scaleMode == ScaleMode.Percentage)
        {
            if (!int.TryParse(PercentBox.Text, out var parsed))
            {
                error = "El porcentaje tiene que ser un número entero del tamaño físico del PDF.";
                return false;
            }

            percent = parsed;
        }

        options = new PrintOptions
        {
            PaperName = paper,
            Copies = copies,
            Range = range,
            Orientation = orientation,
            ScaleMode = scaleMode,
            ScalePercent = percent,
            PagesPerSheet = 1,
        };
        return true;
    }

    private void UpdateRangeBoxes()
    {
        if (AllPagesBox is null || FirstPageBox is null || LastPageBox is null)
        {
            return;
        }

        var ranged = AllPagesBox.IsChecked != true;
        FirstPageBox.IsEnabled = ranged;
        LastPageBox.IsEnabled = ranged;
    }

    private void UpdatePercentBox()
    {
        if (PercentBox is null || ScaleBox is null)
        {
            return;
        }

        PercentBox.IsEnabled = ScaleBox.SelectedIndex == 2;
    }

    private static string Describe(PrintDestination destination)
    {
        var options = destination.Defaults?.Describe() ?? destination.PaperName;
        return $"Destino {destination.Id} guardado para la cola «{destination.QueueName}». Valores predeterminados: {options}.";
    }
}
