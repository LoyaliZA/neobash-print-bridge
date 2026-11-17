using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class HomePage : Page, ISessionPage
{
    private readonly HomeViewModel _model = new();
    private bool _filling;
    private bool _choosing;

    public HomePage()
    {
        InitializeComponent();
        AppServices.Window.Session.Changed += Refresh;
    }

    public void Activate() => Refresh();

    private void Refresh()
    {
        var session = AppServices.Window.Session;
        _model.Refresh(session);
        TitleText.Text = _model.Title;
        SubtitleText.Text = _model.Subtitle;
        StatusText.Text = _model.Status;
        PhysicalList.ItemsSource = _model.Physical;
        SoftwareList.ItemsSource = _model.Software;
        VirtualList.ItemsSource = _model.Virtual;
        PhysicalEmpty.Visibility = _model.Physical.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VirtualEmpty.Visibility = _model.Virtual.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BindSetup(session);
    }

    private void BindSetup(DesktopSession session)
    {
        var setup = session.Setup;
        var showSelectors = setup.AsksForSelection || _choosing;
        var showCard = showSelectors || setup.Kind is LocalSetupKind.Ready or LocalSetupKind.CapabilitiesUnreadable;
        SetupCard.Visibility = showCard ? Visibility.Visible : Visibility.Collapsed;
        if (!showCard)
        {
            return;
        }

        SetupTitle.Text = setup.Kind == LocalSetupKind.Ready && !_choosing ? "Impresora de NPB" : setup.Title;
        SetupMessage.Text = setup.Message;
        var warn = setup.AsksForSelection;
        SetupCard.Background = (Brush)Application.Current.Resources[warn ? "NpbWarnSoftBrush" : "NpbSurfaceBrush"];
        SetupCard.BorderBrush = (Brush)Application.Current.Resources[warn ? "NpbWarnBrush" : "NpbStrokeBrush"];
        var selectors = showSelectors ? Visibility.Visible : Visibility.Collapsed;
        SetupQueue.Visibility = selectors;
        SetupPaper.Visibility = selectors;
        SetupHint.Visibility = selectors;
        SetupSave.Visibility = selectors;
        SetupChange.Visibility = setup.Kind == LocalSetupKind.Ready && !_choosing ? Visibility.Visible : Visibility.Collapsed;
        SetupCancel.Visibility = _choosing && !setup.AsksForSelection ? Visibility.Visible : Visibility.Collapsed;
        if (!showSelectors)
        {
            return;
        }

        var names = LocalSetup.CandidateQueues(session.Installed).Select(queue => queue.QueueName).ToArray();
        var selectedQueue = session.SelectedQueueName ?? setup.KeptQueueName;
        ReplaceItems(SetupQueue, names, selectedQueue);
        var papers = PapersFor(session, SetupQueue.SelectedItem as string);
        var requestedPaper = session.SelectedPaper;
        ReplaceItems(SetupPaper, papers, requestedPaper);
        SetupSave.IsEnabled = session.CanSaveLocalPrinter;
    }

    private static string[] PapersFor(DesktopSession session, string? queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName))
        {
            return [];
        }

        if (string.Equals(session.SelectedQueueName, queueName, StringComparison.OrdinalIgnoreCase))
        {
            return session.Papers.ToArray();
        }

        var queue = session.Installed.FirstOrDefault(item => string.Equals(item.QueueName, queueName, StringComparison.OrdinalIgnoreCase));
        return LocalSetup.AnnouncedPapers(queue).ToArray();
    }

    private void ReplaceItems(ComboBox box, IReadOnlyList<string> items, string? selected)
    {
        _filling = true;
        var current = box.Items.OfType<string>().ToArray();
        if (!current.SequenceEqual(items, StringComparer.Ordinal))
        {
            box.Items.Clear();
            foreach (var item in items)
            {
                box.Items.Add(item);
            }
        }

        var match = items.FirstOrDefault(item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(box.SelectedItem as string, match, StringComparison.Ordinal))
        {
            box.SelectedItem = match;
        }

        _filling = false;
    }

    private void OnSetupQueue(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        _choosing = true;
        SetupFeedback.Text = "";
        if (SetupQueue.SelectedItem is string queue)
        {
            AppServices.Window.Session.SelectQueue(queue);
        }
    }

    private void OnSetupPaper(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        SetupFeedback.Text = "";
        AppServices.Window.Session.SelectPaper(SetupPaper.SelectedItem as string);
    }

    private void OnSaveSetup(object sender, RoutedEventArgs e)
    {
        var error = AppServices.Window.Session.SaveLocalPrinter(SetupQueue.SelectedItem as string, SetupPaper.SelectedItem as string);
        SetupFeedback.Text = error ?? "Impresora de NPB guardada. Eso no confirma que haya salido papel.";
        if (error is null)
        {
            _choosing = false;
        }

        Refresh();
    }

    private void OnChangeSetup(object sender, RoutedEventArgs e)
    {
        _choosing = true;
        SetupFeedback.Text = "";
        Refresh();
    }

    private void OnCancelSetup(object sender, RoutedEventArgs e)
    {
        _choosing = false;
        SetupFeedback.Text = "";
        AppServices.Window.Session.CancelLocalSelection();
        Refresh();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = AppServices.Window.Session.LoadNamesAsync();

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string queue })
        {
            AppServices.Window.OpenPrinter(queue);
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e) => AppServices.Window.OpenSection("print");

    private void OnPrinters(object sender, RoutedEventArgs e) => AppServices.Window.OpenSection("printers");

    private void OnAccess(object sender, RoutedEventArgs e) => AppServices.Window.OpenSection("access");
}
