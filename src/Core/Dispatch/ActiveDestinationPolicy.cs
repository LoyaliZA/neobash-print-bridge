namespace Neobash.PrintBridge.Core.Dispatch;

public static class ActiveDestinationPolicy
{
    public static PrintDestination? Resolve(DesktopPreferences preferences, IReadOnlyList<PrintDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(destinations);
        if (!string.IsNullOrWhiteSpace(preferences.ActiveDestinationId))
        {
            return destinations.FirstOrDefault(item => string.Equals(item.Id, preferences.ActiveDestinationId, StringComparison.Ordinal));
        }

        return destinations.Count == 1 ? destinations[0] : null;
    }
}
