using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class HomePage : Page, ISessionPage
{
    private readonly HomeViewModel _model = new();

    public HomePage()
    {
        InitializeComponent();
        AppServices.Window.Session.Changed += Refresh;
    }

    public void Activate() => Refresh();

    private void Refresh()
    {
        _model.Refresh(AppServices.Window.Session);
        TitleText.Text = _model.Title;
        SubtitleText.Text = _model.Subtitle;
        StatusText.Text = _model.Status;
        PhysicalList.ItemsSource = _model.Physical;
        SoftwareList.ItemsSource = _model.Software;
        VirtualList.ItemsSource = _model.Virtual;
        PhysicalEmpty.Visibility = _model.Physical.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VirtualEmpty.Visibility = _model.Virtual.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
