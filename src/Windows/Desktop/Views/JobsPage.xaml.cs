using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class JobsPage : Page, ISessionPage
{
    private List<JobRecord> _visible = [];

    public JobsPage()
    {
        InitializeComponent();
        PhaseBox.SelectedIndex = 0;
        AppServices.Window.Session.Changed += Refresh;
    }

    public void Activate() => Refresh();

    private void Refresh()
    {
        var selected = DestinationBox.SelectedItem as string;
        DestinationBox.SelectionChanged -= OnPhase;
        DestinationBox.Items.Clear();
        DestinationBox.Items.Add("Todos los destinos");
        foreach (var destination in AppServices.Window.Session.HostDestinations())
        {
            DestinationBox.Items.Add(destination.DisplayName);
        }

        DestinationBox.SelectedItem = selected ?? "Todos los destinos";
        if (DestinationBox.SelectedItem is null)
        {
            DestinationBox.SelectedIndex = 0;
        }

        DestinationBox.SelectionChanged += OnPhase;
        Apply();
    }

    private void Apply()
    {
        JobPhase? phase = ((PhaseBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "SubmittedToSpooler" => JobPhase.SubmittedToSpooler,
            "Uncertain" => JobPhase.Uncertain,
            "Failed" => JobPhase.Failed,
            "Received" => JobPhase.Received,
            "Prepared" => JobPhase.Prepared,
            _ => null,
        };
        string? destinationId = null;
        if (DestinationBox.SelectedItem is string name && name != "Todos los destinos")
        {
            destinationId = AppServices.Window.Session.HostDestinations().FirstOrDefault(item => item.DisplayName == name)?.Id;
        }

        _visible = JobHistoryQuery.Apply(AppServices.Window.Session.History, phase, destinationId, SearchBox.Text).ToList();
        History.Items.Clear();
        foreach (var record in _visible)
        {
            var warning = record.Phase == JobPhase.Uncertain ? " · resultado incierto, sin reimpresión" : "";
            History.Items.Add($"{record.ReceivedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {record.Id} · {JobPhaseText.Describe(record.Phase)} · {record.QueueName}{warning}");
        }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Apply();

    private void OnPhase(object sender, SelectionChangedEventArgs e) => Apply();

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        var record = History.SelectedIndex >= 0 && History.SelectedIndex < _visible.Count ? _visible[History.SelectedIndex] : null;
        if (record is null)
        {
            Detail.Text = "Elige un trabajo.";
            Confirm.IsEnabled = false;
            return;
        }

        Detail.Text = string.Join(Environment.NewLine, [
            record.Id,
            "Opciones: " + (record.Options?.Describe() ?? record.PaperName ?? "sin opciones"),
            "NPB: " + JobPhaseText.Describe(record.Phase) + ". " + record.Detail,
            "Spooler: " + (record.Phase == JobPhase.SubmittedToSpooler ? "aceptó el trabajo. Eso no confirma el papel." : "no hay una aceptación registrada."),
            "Confirmación física: " + (string.IsNullOrWhiteSpace(record.PhysicalConfirmation) ? "pendiente" : record.PhysicalConfirmation),
        ]);
        Confirm.IsEnabled = record.Phase == JobPhase.SubmittedToSpooler;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (History.SelectedIndex < 0 || History.SelectedIndex >= _visible.Count)
        {
            return;
        }

        var note = Note.Text?.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            Detail.Text = "Escribe una nota de quien vio el papel.";
            return;
        }

        var record = _visible[History.SelectedIndex];
        AppServices.Window.Session.ConfirmPaper(record.Id, note);
        Note.Text = "";
    }
}
