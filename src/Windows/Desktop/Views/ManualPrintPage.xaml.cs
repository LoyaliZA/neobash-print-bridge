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

    public ManualPrintPage()
    {
        InitializeComponent();
    }

    public void Activate()
    {
        var active = AppServices.Window.Session.ActiveDestination;
        DestinationText.Text = active is null
            ? "Elige un destino activo dentro de una impresora física."
            : $"Destino activo: {active.DisplayName} · {active.QueueName}";
        if (active?.Defaults is PrintOptions options)
        {
            CopiesBox.Text = options.Copies.ToString();
            OrientationBox.SelectedIndex = options.Orientation == PageOrientation.Landscape ? 1 : 0;
            ScaleBox.SelectedIndex = options.ScaleMode switch
            {
                ScaleMode.FitPrintableArea => 1,
                ScaleMode.Percentage => 2,
                _ => 0,
            };
            PercentBox.Text = options.ScalePercent?.ToString() ?? "";
        }
    }

    private async void OnPick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(AppServices.Window));
        picker.FileTypeFilter.Add(".pdf");
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _path = file.Path;
        PathText.Text = file.Path;
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        _render?.Cancel();
        _render = new CancellationTokenSource();
        var token = _render.Token;
        try
        {
            var frame = await _preview.RenderAsync(_path, 0, token);
            if (frame is null || token.IsCancellationRequested)
            {
                frame?.Dispose();
                return;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(frame.Image);
            Preview.Source = bitmap;
            var size = PageLayoutCalculator.PhysicalSizeFromDips(frame.WidthDips, frame.HeightDips);
            PageText.Text = $"{frame.PageCount} páginas. La primera mide {size.WidthInches:0.00} × {size.HeightInches:0.00} pulgadas.";
            frame.Dispose();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PageText.Text = "No se pudo preparar la vista previa. " + exception.Message;
        }
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        var destination = AppServices.Window.Session.ActiveDestination;
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
        if (!PrintRequestReader.TryRead(destination.PaperName, CopiesBox.Text, AllPages.IsChecked == true, FirstPage.Text, LastPage.Text, OrientationBox.SelectedIndex == 1, scale, PercentBox.Text, out var options, out var error))
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
