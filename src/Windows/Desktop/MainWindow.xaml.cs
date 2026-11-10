using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DesktopHost _host;
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);
    private bool _activatedLogged;
    private readonly WindowsQueueObserver _observer = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DesktopNavigation _navigation = new();
    public string ReturnSection => _navigation.ReturnSection;

    public MainWindow(DesktopHost host, IPreferenceStore preferences)
    {
        _host = host;
        Preferences = preferences;
        Ui = new WindowUiQueue(DispatcherQueue);
        Session = new DesktopSession(host, Ui, preferences);
        InitializeComponent();
        AppServices.Window = this;
        Closed += OnClosed;
        _statusTimer.Tick += async (_, _) => await RefreshOperationsAsync();
        _statusTimer.Start();
        Session.Changed += OnSessionChanged;
        Activated += OnActivated;
        SizeChanged += OnSizeChanged;
        // Keep the native title bar: Windows reserves caption buttons and drag space.
        Nav.SelectedItem = Nav.MenuItems[0];
        ShowCurrent();
    }

    public DesktopSession Session { get; }

    public IPreferenceStore Preferences { get; }

    public WindowUiQueue Ui { get; }

    private bool _firstPoll;
    private void OnSessionChanged()
    {
        if (!_firstPoll && Session.NamesReady)
        {
            _firstPoll = true;
            _ = RefreshOperationsAsync();
        }
    }

    private async Task RefreshOperationsAsync()
    {
        // Expire displayed evidence even if an earlier native query is still blocked.
        Session.ApplyObservations(Session.Observations);
        var names = Session.Installed.Select(q => q.QueueName).ToArray();
        var result = await _observer.RefreshAsync(names);
        if (result is not null) Session.ApplyObservations(result);
    }

    public void BeginStartup() => Session.BeginStartup();

    public void OpenPrinter(string queueName)
    {
        _navigation.OpenPrinter(queueName);
        ShowCurrent();
    }

    public void OpenSection(string tag)
    {
        _navigation.OpenSection(tag);
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.Tag as string == tag);
        ShowCurrent();
    }

    public void GoBack()
    {
        _navigation.GoBack();
        ShowCurrent();
    }

    private void OnBack(NavigationView sender, NavigationViewBackRequestedEventArgs args) => GoBack();

    private void OnNavigate(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag) OpenSection(tag);
    }

    private void ShowCurrent()
    {
        Nav.IsBackEnabled = _navigation.CanGoBack;
        Show(_navigation.Current.Section);
    }

    private void Show(string tag)
    {
        if (!_pages.TryGetValue(tag, out var page))
        {
            page = tag switch
            {
                "printers" => new PrintersPage(),
                "detail" => new PrinterDetailPage(),
                "print" => new ManualPrintPage(),
                "jobs" => new JobsPage(),
                "connections" => new ConnectionsPage(),
                "access" => new AccessPage(),
                "settings" => new SettingsPage(),
                _ => new HomePage(),
            };
            _pages[tag] = page;
        }

        if (ContentFrame.Content is ManualPrintPage previous && previous != page) previous.Deactivate();
        ContentFrame.Content = page;
        if (page is ISessionPage sessionPage)
        {
            sessionPage.Activate();
        }

        if (tag == "detail" && page is PrinterDetailPage detail && _navigation.Current.QueueName is not null)
        {
            detail.ShowQueue(_navigation.Current.QueueName);
        }
    }

    private void OnSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        ContentFrame.Margin = new Thickness(0, args.Size.Width < 880 ? 48 : 0, 0, 0);
        BrandSubtitle.Visibility = args.Size.Width < 550 ? Visibility.Collapsed : Visibility.Visible;
        Nav.PaneDisplayMode = args.Size.Width switch
        {
            < 880 => NavigationViewPaneDisplayMode.LeftMinimal,
            < 1100 => NavigationViewPaneDisplayMode.LeftCompact,
            _ => NavigationViewPaneDisplayMode.Left,
        };
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _statusTimer.Stop();
        _host.Log.Write("Ventana cerrada. El proceso termina.");
        Application.Current.Exit();
    }


}

public interface ISessionPage
{
    void Activate();
}

public sealed class WindowUiQueue(Microsoft.UI.Dispatching.DispatcherQueue queue) : IUiQueue
{
    public bool TryEnqueue(Action action) => queue.TryEnqueue(() => action());
}

public static class AppServices
{
    public static MainWindow Window { get; set; } = null!;
}
