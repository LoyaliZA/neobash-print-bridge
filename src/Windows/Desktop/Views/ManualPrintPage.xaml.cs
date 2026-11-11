using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Neobash.PrintBridge.Core.Dispatch;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class ManualPrintPage : Page, ISessionPage
{
    private readonly PdfPagePreview _preview = new();
    private CancellationTokenSource? _render;
    private string? _path;

    private readonly PreviewPaperReader _paperReader = new();
    private readonly SemaphoreSlim _layoutGate = new(1, 1);
    private CancellationTokenSource? _layoutCancellation;
    private uint _pageIndex;
    private uint _pageCount;
    private double _widthDips, _heightDips, _zoom = 1;
    private bool _initialized, _updatingDestinations;
    private string? _destinationId;
    private bool _destinationInitialized;
    private bool _active;
    private PrintDestination? SelectedDestination => DestinationBox.SelectedItem as PrintDestination;

    public ManualPrintPage()
    {
        InitializeComponent();
        _initialized = true;
        AppServices.Window.Session.Changed += RefreshDestinations;
    }

    public void Activate()
    {
        _active = true;
        RefreshDestinations();
        if (_path is not null && Preview.Source is null) _ = RenderAsync();
        else _ = UpdateLayoutAsync();
    }

    public void Deactivate()
    {
        _active = false;
        _render?.Cancel();
        _layoutCancellation?.Cancel();
    }

    private void RefreshDestinations()
    {
        if (!_active) return;
        var destinations = AppServices.Window.Session.HostDestinations();
        var desired = _destinationInitialized ? _destinationId : AppServices.Window.Session.ActiveDestination?.Id;
        _destinationInitialized = true;
        var previous = SelectedDestination;
        _updatingDestinations = true;
        DestinationBox.ItemsSource = destinations;
        DestinationBox.SelectedItem = destinations.FirstOrDefault(d => d.Id == desired);
        _updatingDestinations = false;
        var selected = SelectedDestination;
        _destinationId = desired;
        var setup = AppServices.Window.Session.Setup;
        DestinationText.Text = setup.AsksForSelection
            ? setup.Message
            : selected is null ? "Selecciona un destino guardado en Mis impresoras." : $"{selected.QueueName} · {selected.PaperName}";
        if (_active && selected != previous) ApplyDestination();
    }

    private void OnDestination(object sender, SelectionChangedEventArgs args)
    {
        if (!_initialized || _updatingDestinations) return;
        _destinationId = SelectedDestination?.Id;
        ApplyDestination();
    }

    private void ApplyDestination()
    {
        var active = SelectedDestination;
        var setup = AppServices.Window.Session.Setup;
        DestinationText.Text = setup.AsksForSelection
            ? setup.Message
            : active is null ? "Selecciona un destino guardado en Mis impresoras." : $"{active.QueueName} · {active.PaperName}";
        if (active is not null)
        {
            var options = active.Defaults ?? PrintOptions.ForPaper(active.PaperName ?? "");
            CopiesBox.Value = options.Copies;
            OrientationBox.SelectedIndex = options.Orientation == PageOrientation.Landscape ? 1 : 0;
            ScaleBox.SelectedIndex = options.ScaleMode switch { ScaleMode.FitPrintableArea => 1, ScaleMode.Percentage => 2, _ => 0 };
            PercentBox.Text = (options.ScalePercent ?? 100).ToString();
            var queue = AppServices.Window.Session.Installed.FirstOrDefault(q => q.QueueName == active.QueueName);
            if (queue is { Detail: QueueDetail.NameOnly }) AppServices.Window.Session.SelectQueue(queue.QueueName);
        }
        _ = UpdateLayoutAsync();
    }

    private async void OnPick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(AppServices.Window));
        picker.FileTypeFilter.Add(".pdf");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        _render?.Cancel();
        _layoutCancellation?.Cancel();
        _path = file.Path;
        _pageIndex = 0;
        _pageCount = 0;
        Preview.Source = null;
        PaperCanvas.Visibility = Visibility.Collapsed;
        PathText.Text = file.Name;
        PickButton.Content = "Cambiar documento";
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        if (string.IsNullOrWhiteSpace(_path)) return;
        _render?.Cancel();
        var cancellation = new CancellationTokenSource();
        _render = cancellation;
        var token = cancellation.Token;
        _layoutCancellation?.Cancel();
        var path = _path;
        var page = _pageIndex;
        Preview.Source = null;
        PaperCanvas.Visibility = Visibility.Collapsed;
        PageText.Text = "Preparando página…";
        try
        {
            await Task.Delay(100, token);
            using var frame = await _preview.RenderAsync(path, page, token);
            if (frame is null) return;
            token.ThrowIfCancellationRequested();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(frame.Image).AsTask(token);
            token.ThrowIfCancellationRequested();
            Preview.Source = bitmap;
            _widthDips = frame.WidthDips;
            _heightDips = frame.HeightDips;
            _pageCount = frame.PageCount;
            PageText.Text = $"Página {page + 1} de {_pageCount}";
            PreviousButton.IsEnabled = page > 0;
            NextButton.IsEnabled = page + 1 < _pageCount;
            await UpdateLayoutAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested) PageText.Text = "No se pudo abrir la página. " + exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_render, cancellation)) _render = null;
            cancellation.Dispose();
        }
    }

    private async void OnPrevious(object sender, RoutedEventArgs args) { if (_pageIndex > 0) { _pageIndex--; await RenderAsync(); } }
    private async void OnNext(object sender, RoutedEventArgs args) { if (_pageIndex + 1 < _pageCount) { _pageIndex++; await RenderAsync(); } }
    private void OnZoomOut(object sender, RoutedEventArgs args) { _zoom = Math.Max(0.25, _zoom / 1.25); _ = UpdateLayoutAsync(); }
    private void OnZoomIn(object sender, RoutedEventArgs args) { _zoom = Math.Min(4, _zoom * 1.25); _ = UpdateLayoutAsync(); }
    private void OnLayoutChanged(object sender, SelectionChangedEventArgs args) { if (_initialized) _ = UpdateLayoutAsync(); }
    private void OnPercentChanged(object sender, TextChangedEventArgs args) { if (_initialized) _ = UpdateLayoutAsync(); }

    private async Task UpdateLayoutAsync()
    {
        if (!_initialized || !_active) return;
        _layoutCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _layoutCancellation = cancellation;
        var token = cancellation.Token;
        PaperCanvas.Visibility = Visibility.Collapsed;
        try
        {
            var destination = SelectedDestination;
            if (Preview.Source is null) return;
            if (destination?.PaperName is null) { ShowDocumentOnly(); PreviewNote.Text = "Página del PDF. Selecciona un destino para representar su papel y área imprimible."; return; }
            var orientation = OrientationBox.SelectedIndex == 1 ? PageOrientation.Landscape : PageOrientation.Portrait;
            var mode = ScaleBox.SelectedIndex switch { 1 => ScaleMode.FitPrintableArea, 2 => ScaleMode.Percentage, _ => ScaleMode.ActualSize };
            int? percent = null;
            if (mode == ScaleMode.Percentage)
            {
                if (!int.TryParse(PercentBox.Text, out var value) || value < PrintOptionRules.MinimumScalePercent || value > PrintOptionRules.MaximumScalePercent) { PreviewNote.Text = "Escribe un porcentaje entre 1 y 400."; return; }
                percent = value;
            }
            await Task.Delay(100, token);
            await _layoutGate.WaitAsync(token);
            PreviewPaper paper;
            try { paper = await _paperReader.ReadAsync(destination.QueueName, destination.PaperName, orientation, token); }
            finally { _layoutGate.Release(); }
            token.ThrowIfCancellationRequested();
            if (!paper.HasConsistentBounds)
            {
                ShowDocumentOnly();
                PreviewNote.Text = "Solo documento: el área imprimible reportada no coincide con el papel orientado. Revisa las preferencias del controlador. El envío conserva su validación de recorte.";
                return;
            }
            var size = PageLayoutCalculator.PhysicalSizeFromDips(_widthDips, _heightDips);
            var layout = PageLayoutCalculator.Place(size.WidthInches, size.HeightInches, paper.Printable, paper.DpiX, paper.DpiY, mode, percent);
            PreviewNote.Text = layout.Error ?? "Contorno: área imprimible del controlador. Vista orientativa; el envío vuelve a validar todas las páginas.";
            if (layout.RequiresClippingConfirmation) layout = PageLayoutCalculator.Place(size.WidthInches, size.HeightInches, paper.Printable, paper.DpiX, paper.DpiY, mode, percent, true);
            if (layout.Placement is not { } placement) return;
            var factor = Math.Min(520 / paper.Width, 370 / paper.Height) * _zoom;
            PaperCanvas.Width = paper.Width * factor;
            PaperCanvas.Height = paper.Height * factor;
            PaperCanvas.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, PaperCanvas.Width, PaperCanvas.Height) };
            PrintableCanvas.Width = PaperCanvas.Width;
            PrintableCanvas.Height = PaperCanvas.Height;
            PrintableCanvas.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(paper.Printable.XInches * factor, paper.Printable.YInches * factor, paper.Printable.WidthInches * factor, paper.Printable.HeightInches * factor) };
            Preview.Width = placement.WidthInches * factor;
            Preview.Height = placement.HeightInches * factor;
            Canvas.SetLeft(Preview, placement.XInches * factor);
            Canvas.SetTop(Preview, placement.YInches * factor);
            PrintableOutline.Visibility = Visibility.Visible;
            PrintableOutline.Width = paper.Printable.WidthInches * factor;
            PrintableOutline.Height = paper.Printable.HeightInches * factor;
            Canvas.SetLeft(PrintableOutline, paper.Printable.XInches * factor);
            Canvas.SetTop(PrintableOutline, paper.Printable.YInches * factor);
            PaperCanvas.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!token.IsCancellationRequested) { ShowDocumentOnly(); PreviewNote.Text = "Solo documento; no se pudo consultar el papel. " + exception.Message; } }
        finally
        {
            if (ReferenceEquals(_layoutCancellation, cancellation)) _layoutCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ShowDocumentOnly()
    {
        if (Preview.Source is null || _widthDips <= 0 || _heightDips <= 0) return;
        var factor = Math.Min(520 / _widthDips, 370 / _heightDips) * _zoom;
        PaperCanvas.Width = PrintableCanvas.Width = Preview.Width = _widthDips * factor;
        PaperCanvas.Height = PrintableCanvas.Height = Preview.Height = _heightDips * factor;
        PaperCanvas.Clip = null;
        PrintableCanvas.Clip = null;
        Canvas.SetLeft(Preview, 0);
        Canvas.SetTop(Preview, 0);
        PrintableOutline.Visibility = Visibility.Collapsed;
        PaperCanvas.Visibility = Visibility.Visible;
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        var setup = AppServices.Window.Session.Setup;
        if (setup.AsksForSelection)
        {
            ResultText.Text = setup.Message;
            return;
        }

        var destination = SelectedDestination;
        if (destination is null)
        {
            ResultText.Text = "Guarda un destino y márcalo como activo antes de imprimir.";
            return;
        }

        var queue = AppServices.Window.Session.Installed.FirstOrDefault(item => string.Equals(item.QueueName, destination.QueueName, StringComparison.OrdinalIgnoreCase));
        if (queue is null || DevicePresentation.IsKnownVirtual(queue))
        {
            ResultText.Text = "La cola del destino no está disponible como impresora física. No se elige otra.";
            return;
        }

        if (queue.Detail != QueueDetail.Ready)
        {
            ResultText.Text = "Las capacidades de esa cola todavía no están leídas. No se envía el trabajo.";
            return;
        }

        var scale = ScaleBox.SelectedIndex switch
        {
            1 => ScaleMode.FitPrintableArea,
            2 => ScaleMode.Percentage,
            _ => ScaleMode.ActualSize,
        };
        if (!PrintRequestReader.TryRead(destination.PaperName, CopiesBox.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), AllPages.IsChecked == true, FirstPage.Text, LastPage.Text, OrientationBox.SelectedIndex == 1, scale, PercentBox.Text, out var options, out var error))
        {
            ResultText.Text = error;
            return;
        }

        var rule = PrintOptionRules.Check(options, queue);
        if (rule is not null)
        {
            ResultText.Text = rule;
            return;
        }

        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        {
            ResultText.Text = "Elige un PDF que ya exista.";
            return;
        }

        var host = AppServices.Window.Session.SessionHost();
        var job = new PrintJob("job-" + Guid.NewGuid().ToString("N"), PhysicalQueueRules.PdfMediaType, _path, destination.Id, options.PaperName, options);
        AppServices.Window.Session.SetPrinting(true);
        PrintButton.IsEnabled = false;
        try
        {
            var result = await host.Dispatcher.DispatchAsync(job);
            if (result.RequiresClippingConfirmation)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "La página puede quedar recortada",
                    Content = new TextBlock { Text = result.Detail, TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = "Imprimir con posible recorte",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    ResultText.Text = "No se envió el trabajo. Puedes elegir Ajustar al área imprimible o cambiar la escala.";
                    return;
                }

                var rejected = job.Id;
                job = job with { Id = "job-" + Guid.NewGuid().ToString("N"), Options = options with { AllowClippingConfirmed = true } };
                host.Log.Write($"Posible recorte confirmado para {job.Id}; intento previo sin envío {rejected}.");
                result = await host.Dispatcher.DispatchAsync(job);
            }

            ResultText.Text = $"Trabajo {job.Id}: {JobPhaseText.Describe(result.Phase)}. {result.Detail}";
            host.Log.Write($"{JobPhaseText.Describe(result.Phase)} {job.Id} {result.Detail}");
        }
        catch (Exception exception)
        {
            ResultText.Text = $"Trabajo {job.Id}: no se pudo completar el despacho. {exception.Message}";
            host.Log.Write($"Fallo de interfaz {job.Id} {exception.Message}");
        }
        finally
        {
            AppServices.Window.Session.SetPrinting(false);
            PrintButton.IsEnabled = true;
            AppServices.Window.Session.RefreshHistory();
        }
    }
}
