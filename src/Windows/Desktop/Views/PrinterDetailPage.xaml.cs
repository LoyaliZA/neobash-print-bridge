using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class PrinterDetailPage : Page, ISessionPage
{
    private string? _queueName;

    public PrinterDetailPage()
    {
        InitializeComponent();
        AppServices.Window.Session.Changed += Refresh;
    }

    private void OnBack(object sender, RoutedEventArgs e) => AppServices.Window.GoBack();

    public void Activate() => Refresh();

    public void ShowQueue(string queueName)
    {
        _queueName = queueName;
        BackButton.Content = AppServices.Window.ReturnSection == "printers" ? "← Mis impresoras" : "← Inicio";
        AppServices.Window.Session.SelectQueue(queueName);
        Refresh();

    }

    private void Refresh()
    {
        if (string.IsNullOrWhiteSpace(_queueName))
        {
            return;
        }

        var queue = AppServices.Window.Session.Installed.FirstOrDefault(item => string.Equals(item.QueueName, _queueName, StringComparison.OrdinalIgnoreCase));
        var virtualQueue = queue is not null && DevicePresentation.IsKnownVirtual(queue);
        NameText.Text = _queueName;
        var observation = AppServices.Window.Session.Observation(_queueName);
        var kind = queue is null ? DeviceKind.Unknown : DevicePresentation.Kind(queue, observation);
        KindText.Text = DevicePresentation.KindLabel(kind) + " · " + (observation?.DriverName ?? queue?.DriverName ?? "controlador no leído");
        StatusText.Text = (observation?.Describe(DateTimeOffset.UtcNow) ?? "Estado no confirmado")
            + " · " + DevicePresentation.Status(queue, kind, queue is not null);
        var operation = observation?.Operation(DateTimeOffset.UtcNow) ?? QueueOperation.Unknown;
        var colorKey = operation switch
        {
            QueueOperation.Available => "NpbReadyBrush", QueueOperation.Printing or QueueOperation.Busy => "NpbIndigoBrush",
            QueueOperation.Attention => "NpbWarnBrush", QueueOperation.Paused => "NpbPausedBrush", _ => "NpbMutedBrush",
        };
        StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[colorKey];
        Art.Category = DevicePresentation.Art(queue, kind).ToString();
        DestinationSection.Visibility = virtualQueue ? Visibility.Collapsed : Visibility.Visible;
        VirtualNote.Visibility = virtualQueue ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.Visibility = virtualQueue ? Visibility.Collapsed : Visibility.Visible;
        DestinationList.Children.Clear();
        var active = AppServices.Window.Session.ActiveDestination;
        var matches = AppServices.Window.Session.HostDestinations().Where(item => string.Equals(item.QueueName, _queueName, StringComparison.OrdinalIgnoreCase)).ToList();
        DestinationEmpty.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var destination in matches)
        {
            DestinationList.Children.Add(BuildRow(destination, active?.Id == destination.Id));
        }

        JobList.Children.Clear();
        foreach (var record in AppServices.Window.Session.History.Where(record => string.Equals(record.QueueName, _queueName, StringComparison.OrdinalIgnoreCase)).Take(3))
        {
            JobList.Children.Add(new TextBlock
            {
                Text = $"{record.ReceivedAt.ToLocalTime():dd MMM HH:mm} · {Path.GetFileName(record.LocalPath)} · {JobPhaseText.Describe(record.Phase)}. El spooler no confirma el papel.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private UIElement BuildRow(PrintDestination destination, bool active)
    {
        var panel = new StackPanel { Spacing = 12 };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = destination.DisplayName, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = destination.Defaults?.Describe() ?? destination.PaperName ?? "Sin papel guardado",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["NpbMutedBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        if (active)
        {
            text.Children.Add(new TextBlock { Text = "Activo", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["NpbReadyBrush"] });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("Usar", () => Use(destination)));
        actions.Children.Add(Action("Editar", () => _ = EditAsync(destination)));
        actions.Children.Add(Action("Eliminar", () => _ = DeleteAsync(destination)));
        panel.Children.Add(text);
        panel.Children.Add(actions);
        return new Border
        {
            Style = (Style)Application.Current.Resources["NpbCard"],
            Child = panel,
        };
    }

    private static Button Action(string label, Action click)
    {
        var button = new Button { Content = label, Style = (Style)Application.Current.Resources["NpbQuietButton"] };
        button.Click += (_, _) => click();
        return button;
    }

    private void Use(PrintDestination destination)
    {
        var preferences = AppServices.Window.Preferences.Load();
        AppServices.Window.Preferences.Save(preferences with { ActiveDestinationId = destination.Id });
        Message.Text = $"«{destination.DisplayName}» queda como destino activo. No se cambia la impresora predeterminada de Windows.";
        Refresh();
    }

    private async Task EditAsync(PrintDestination destination)
    {
        var name = new TextBox { Text = destination.DisplayName, Header = "Nombre" };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Editar destino",
            Content = name,
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text))
        {
            return;
        }

        AppServices.Window.Session.SaveDestination(destination with { DisplayName = name.Text.Trim() });
        Refresh();
    }

    private async Task DeleteAsync(PrintDestination destination)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Eliminar destino",
            Content = $"Se quita «{destination.DisplayName}». El historial de trabajos se conserva.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        AppServices.Window.Session.RemoveDestination(destination.Id);
        var preferences = AppServices.Window.Preferences.Load();
        if (string.Equals(preferences.ActiveDestinationId, destination.Id, StringComparison.Ordinal))
        {
            AppServices.Window.Preferences.Save(preferences with { ActiveDestinationId = null });
        }

        Message.Text = "Destino eliminado. Los trabajos anteriores siguen en el historial.";
        Refresh();
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_queueName))
        {
            return;
        }

        var queue = AppServices.Window.Session.Installed.FirstOrDefault(item => string.Equals(item.QueueName, _queueName, StringComparison.OrdinalIgnoreCase));
        if (queue is not { Detail: QueueDetail.Ready })
        {
            Message.Text = "Hay que leer las capacidades de la cola antes de crear un destino. No se elige otra impresora.";
            if (queue is not null)
            {
                await AppServices.Window.Session.LoadDetailsAsync(queue.QueueName);
            }

            return;
        }

        var name = new TextBox { Header = "Nombre del destino", PlaceholderText = "Por ejemplo, Mostrador" };
        var paper = new ComboBox { Header = "Papel", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in queue.PaperNames)
        {
            paper.Items.Add(item);
        }

        var copies = new TextBox { Header = "Copias", Text = "1" };
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(name);
        form.Children.Add(paper);
        form.Children.Add(copies);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Nuevo destino",
            Content = form,
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!PrintRequestReader.TryRead(paper.SelectedItem as string, copies.Text, true, null, null, false, ScaleMode.ActualSize, null, out var options, out var error))
        {
            Message.Text = error;
            return;
        }

        var rule = PrintOptionRules.Check(options, queue);
        if (rule is not null)
        {
            Message.Text = rule;
            return;
        }

        var driver = string.IsNullOrWhiteSpace(queue.DriverName) ? null : queue.DriverName;
        var destination = new PrintDestination("dest-" + Guid.NewGuid().ToString("N"), name.Text.Trim(), queue.QueueName, options.PaperName, options, driver);
        if (string.IsNullOrWhiteSpace(destination.DisplayName))
        {
            Message.Text = "El destino necesita un nombre.";
            return;
        }

        AppServices.Window.Session.SaveDestination(destination);
        Use(destination);
    }

    private void OnPrint(object sender, RoutedEventArgs e) => AppServices.Window.OpenSection("print");

    private void OnJobs(object sender, RoutedEventArgs e) => AppServices.Window.OpenSection("jobs");

    private void OnAccess(object sender, RoutedEventArgs e) => AppServices.Window.OpenSection("access");


}
