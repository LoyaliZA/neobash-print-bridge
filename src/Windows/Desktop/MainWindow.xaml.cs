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

    public MainWindow(DesktopHost host)
    {
        _host = host;
        InitializeComponent();
        Closed += OnClosed;
        QueueBox.SelectionChanged += OnQueueChanged;
        LoadQueues();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _host.Log.Write("Ventana cerrada. El proceso termina.");
        Application.Current.Exit();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => LoadQueues();

    private void LoadQueues()
    {
        _loading = true;
        try
        {
            var installed = _host.Catalog.List();
            _queues.Clear();
            _queues.AddRange(installed.Where(queue => !PhysicalQueueRules.IsVirtual(queue)));
            QueueBox.Items.Clear();
            PaperBox.Items.Clear();
            foreach (var queue in _queues)
            {
                QueueBox.Items.Add(queue.QueueName);
            }

            var saved = CurrentDestination();
            if (saved is null)
            {
                DestinationStatus.Text = _queues.Count == 0
                    ? "No hay colas físicas en el catálogo."
                    : "Elige una cola y un papel, y guarda el destino.";
            }
            else
            {
                var match = installed.FirstOrDefault(queue => string.Equals(queue.QueueName, saved.QueueName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    DestinationStatus.Text = $"La cola guardada «{saved.QueueName}» no está en el catálogo. No se selecciona otra.";
                }
                else if (PhysicalQueueRules.IsVirtual(match))
                {
                    DestinationStatus.Text = $"La cola guardada «{saved.QueueName}» es virtual y no puede ser destino físico. No se selecciona otra.";
                }
                else
                {
                    QueueBox.SelectedItem = match.QueueName;
                    FillPapers(SelectedQueue());
                    SelectPaper(saved.PaperName);
                    ApplyOptions(saved.Defaults ?? PrintOptions.ForPaper(saved.PaperName ?? ""));
                    DestinationStatus.Text = Describe(saved);
                }
            }

            if (_host.Catalog.Warnings.Count > 0)
            {
                DestinationStatus.Text += " " + string.Join(" ", _host.Catalog.Warnings);
            }
        }
        catch (Exception exception)
        {
            DestinationStatus.Text = "No se pudo leer el catálogo de colas. " + exception.Message;
            _host.Log.Write("Catálogo no leído. " + exception.Message);
        }
        finally
        {
            _loading = false;
        }

        RefreshHistory();
    }

    private void OnQueueChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        FillPapers(SelectedQueue());
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
            PrintButton.IsEnabled = true;
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
        _refreshingHistory = true;
        try
        {
            _history.Clear();
            HistoryList.Items.Clear();
            foreach (var record in _host.Jobs.List().OrderByDescending(record => record.ReceivedAt))
            {
                var confirmation = string.IsNullOrWhiteSpace(record.PhysicalConfirmation)
                    ? "confirmación física pendiente"
                    : "confirmación física: " + record.PhysicalConfirmation;
                var optionsText = record.Options?.Describe() ?? record.PaperName;
                var label = $"{record.ReceivedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {record.Id} · {JobPhaseText.Describe(record.Phase)} · {record.QueueName} · {optionsText} · {record.Detail} · {confirmation}";
                _history.Add(record);
                HistoryList.Items.Add(label);
            }

            var index = _history.FindIndex(record => string.Equals(record.Id, selected, StringComparison.Ordinal));
            if (index >= 0)
            {
                HistoryList.SelectedIndex = index;
            }
        }
        finally
        {
            _refreshingHistory = false;
        }

        UpdateConfirmButton();
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
