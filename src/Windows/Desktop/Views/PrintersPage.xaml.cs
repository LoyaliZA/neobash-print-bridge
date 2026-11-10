using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class PrintersPage : Page, ISessionPage
{
    private readonly ObservableCollection<DeviceCardModel> _cards = [];
    private string _filter = "all";

    public PrintersPage()
    {
        InitializeComponent();
        AppServices.Window.Session.Changed += Refresh;
    }

    public void Activate() => Refresh();

    private void Refresh()
    {
        var term = SearchBox.Text?.Trim();
        var cards = new List<DeviceCardModel>();
        foreach (var queue in AppServices.Window.Session.Installed.OrderBy(q => QueueObservation.SortOrder(AppServices.Window.Session.Observation(q.QueueName)?.Operation(DateTimeOffset.UtcNow) ?? QueueOperation.Unknown)).ThenBy(q => q.QueueName, StringComparer.CurrentCultureIgnoreCase))
        {
            var kind = DevicePresentation.Kind(queue, AppServices.Window.Session.Observation(queue.QueueName));
            if (_filter == "physical" && kind != DeviceKind.Physical)
            {
                continue;
            }

            if (_filter == "software" && kind != DeviceKind.Software) continue;

            if (_filter == "virtual" && kind != DeviceKind.Virtual)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(term) && !queue.QueueName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cards.Add(HomeViewModel.Card(queue, kind, AppServices.Window.Session.Observation(queue.QueueName)));
        }

        HomeViewModel.Replace(_cards, cards);
        List.ItemsSource = _cards;
        EmptyText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFilter(object sender, TextChangedEventArgs e) => Refresh();

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        _filter = (sender as Button)?.Tag as string ?? "all";
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
}
