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
        _cards.Clear();
        foreach (var queue in AppServices.Window.Session.Installed)
        {
            var kind = DevicePresentation.IsKnownVirtual(queue) ? DeviceKind.Virtual : DeviceKind.Physical;
            if (_filter == "physical" && kind != DeviceKind.Physical)
            {
                continue;
            }

            if (_filter == "virtual" && kind != DeviceKind.Virtual)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(term) && !queue.QueueName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _cards.Add(HomeViewModel.Card(queue, kind));
        }

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
