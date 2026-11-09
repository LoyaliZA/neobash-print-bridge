using System.Diagnostics;

namespace Neobash.PrintBridge.Desktop;

public static class StartupClock
{
    private static readonly long ManagedTimestamp = Stopwatch.GetTimestamp();

    public static DateTimeOffset ManagedEntryUtc { get; } = DateTimeOffset.UtcNow;

    public static DateTimeOffset ProcessStartUtc { get; } = new(Process.GetCurrentProcess().StartTime);

    public static TimeSpan ProcessToManaged => ManagedEntryUtc - ProcessStartUtc;

    public static TimeSpan SinceManaged() => Stopwatch.GetElapsedTime(ManagedTimestamp);
}
