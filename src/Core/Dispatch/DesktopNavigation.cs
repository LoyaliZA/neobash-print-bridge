namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record DesktopRoute(string Section, string? QueueName = null);

// A top-level invocation always leaves a secondary route, even when already selected.
public sealed class DesktopNavigation
{
    private readonly Stack<DesktopRoute> _history = new();
    public DesktopRoute Current { get; private set; } = new("home");
    public bool CanGoBack => _history.Count > 0;
    public string ReturnSection => _history.TryPeek(out var route) ? route.Section : "home";

    public void OpenSection(string section)
    {
        if (section is not ("home" or "printers" or "print" or "jobs" or "connections" or "access" or "settings"))
            throw new ArgumentOutOfRangeException(nameof(section));
        _history.Clear();
        Current = new(section);
    }

    public void OpenPrinter(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        if (Current.Section != "detail") _history.Push(Current);
        Current = new("detail", queueName);
    }

    public void GoBack()
    {
        if (_history.TryPop(out var route)) Current = route;
    }
}
