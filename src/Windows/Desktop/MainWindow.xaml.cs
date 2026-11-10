using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Neobash.PrintBridge.Core.Dispatch;
using Microsoft.UI;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DesktopHost _host;
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);
    private bool _activatedLogged;
    private string? _pendingQueue;

    public MainWindow(DesktopHost host, IPreferenceStore preferences)
    {
        _host = host;
        Preferences = preferences;
        Ui = new WindowUiQueue(DispatcherQueue);
        Session = new DesktopSession(host, Ui, preferences);
        InitializeComponent();
        AppServices.Window = this;
        Closed += OnClosed;
        Activated += OnActivated;
        SizeChanged += OnSizeChanged;
        TryIntegrateTitleBar();
        Nav.SelectedItem = Nav.MenuItems[0];
        Show("home");
    }

    public DesktopSession Session { get; }

    public IPreferenceStore Preferences { get; }

    public WindowUiQueue Ui { get; }

    public void BeginStartup() => Session.BeginStartup();

    public void OpenPrinter(string queueName)
    {
        _pendingQueue = queueName;
        Show("detail");
        if (_pages.TryGetValue("detail", out var page) && page is PrinterDetailPage detail)
        {
            detail.ShowQueue(queueName);
        }
    }

    public void OpenSection(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            {
                Nav.SelectedItem = item;
                return;
            }
        }

        Show(tag);
    }

    private void OnNavigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Show(tag);
        }
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

        ContentFrame.Content = page;
        if (page is ISessionPage sessionPage)
        {
            sessionPage.Activate();
        }

        if (tag == "detail" && page is PrinterDetailPage detail && _pendingQueue is not null)
        {
            detail.ShowQueue(_pendingQueue);
        }
    }

    private void OnSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        Nav.PaneDisplayMode = args.Size.Width switch
        {
            < 880 => NavigationViewPaneDisplayMode.Top,
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
        _host.Log.Write("Ventana cerrada. El proceso termina.");
        Application.Current.Exit();
    }

    private void TryIntegrateTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 28, 27, 41);
        }
        catch (Exception exception)
        {
            _host.Log.Write("La barra de título integrada no está disponible. " + exception.Message);
        }
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
