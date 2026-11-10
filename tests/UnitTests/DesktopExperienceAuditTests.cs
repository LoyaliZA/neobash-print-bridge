using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Desktop;

namespace Neobash.PrintBridge.Tests;

public sealed class DesktopExperienceAuditTests
{
    [Theory]
    [InlineData("home")]
    [InlineData("printers")]
    public void Repeated_detail_navigation_keeps_origin(string origin)
    {
        var navigation = new DesktopNavigation();
        for (var i = 0; i < 20; i++)
        {
            navigation.OpenSection(origin);
            navigation.OpenPrinter("A");
            navigation.OpenPrinter("B");
            Assert.Equal("B", navigation.Current.QueueName);
            Assert.Equal(origin, navigation.ReturnSection);
            navigation.GoBack();
            Assert.Equal(origin, navigation.Current.Section);
            Assert.False(navigation.CanGoBack);
        }
    }

    [Fact]
    public void Invoking_selected_home_always_leaves_detail_and_clears_history()
    {
        var navigation = new DesktopNavigation();
        navigation.OpenPrinter("A");
        navigation.OpenSection("home");
        navigation.GoBack();
        Assert.Equal(new DesktopRoute("home"), navigation.Current);
        Assert.False(navigation.CanGoBack);
    }

    [Theory]
    [InlineData(0u, 0u, QueueOperation.Unknown)]
    [InlineData(0u, 2u, QueueOperation.Pending)]
    [InlineData(0x200u, 0u, QueueOperation.Busy)]
    [InlineData(0x4000u, 0u, QueueOperation.Busy)]
    [InlineData(0x20000u, 0u, QueueOperation.Attention)]
    [InlineData(0x400u, 2u, QueueOperation.Printing)]
    [InlineData(0x401u, 2u, QueueOperation.Paused)]
    [InlineData(0x411u, 2u, QueueOperation.Attention)]
    [InlineData(0x491u, 2u, QueueOperation.Offline)]
    [InlineData(0x800u, 0u, QueueOperation.Attention)]
    [InlineData(0x80000u, 0u, QueueOperation.Attention)]
    [InlineData(0x100000u, 0u, QueueOperation.Attention)]
    [InlineData(0x400000u, 0u, QueueOperation.Attention)]
    [InlineData(0x800400u, 0u, QueueOperation.Unknown)]
    public void Windows_flags_have_explicit_precedence(uint flags, uint jobs, QueueOperation expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, new QueueObservation(flags, jobs, now).Operation(now));
    }

    [Fact]
    public void Physical_evidence_is_independent_and_stale_reports_are_neutral()
    {
        var now = DateTimeOffset.UtcNow;
        var report = new QueueObservation(0, 0, now, Physical: PhysicalEvidence.Available);
        Assert.Equal(QueueOperation.Available, report.Operation(now));
        Assert.Equal(QueueOperation.Unknown, report.Operation(now.AddSeconds(46)));
        Assert.Equal(QueueOperation.Unknown, (report with { Error = "denied" }).Operation(now));
    }

    [Theory]
    [InlineData("OneNote (Desktop)", "", "", DeviceKind.Software)]
    [InlineData("PDF", "Microsoft Print To PDF", "PORTPROMPT:", DeviceKind.Software)]
    [InlineData("NPB - Prueba Virtual", "", "", DeviceKind.Virtual)]
    [InlineData("Star", "Star BSC10", "USB001", DeviceKind.Physical)]
    [InlineData("Custom queue", "Custom", "CustomPort", DeviceKind.Unknown)]
    public void Software_and_unidentified_ports_are_not_assumed_physical(string name, string driver, string port, DeviceKind expected)
    {
        var queue = new InstalledQueue(name, driver, []);
        Assert.Equal(expected, DevicePresentation.Kind(queue, new(0, 0, DateTimeOffset.UtcNow, driver, port)));
    }

    [Fact]
    public async Task Status_cache_refreshes_errors_and_removes_disappeared_queues()
    {
        var now = DateTimeOffset.UtcNow;
        var calls = 0;
        var observer = new WindowsQueueObserver(name =>
        {
            calls++;
            return new(calls == 1 ? 0u : 0x80u, 0, now);
        }, () => now);
        Assert.Equal(QueueOperation.Unknown, (await observer.RefreshAsync(["A"]))!["A"].Operation(now));
        await observer.RefreshAsync(["A"]);
        Assert.Equal(1, calls);
        now = now.AddSeconds(16);
        Assert.Equal(QueueOperation.Offline, (await observer.RefreshAsync(["A"]))!["A"].Operation(now));
        Assert.Empty((await observer.RefreshAsync([]))!);
        var failing = new WindowsQueueObserver(_ => throw new IOException("read failed"));
        Assert.Equal(QueueOperation.Unknown, (await failing.RefreshAsync(["A"]))!["A"].Operation(now));
    }

    [Fact]
    public async Task Slow_status_query_does_not_enqueue_more_polls()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var observer = new WindowsQueueObserver(_ =>
        {
            entered.SetResult();
            release.Wait(TimeSpan.FromSeconds(5));
            return new(0, 0, DateTimeOffset.UtcNow);
        });
        var pending = observer.RefreshAsync(["A"]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(await observer.RefreshAsync(["A"]));
        }
        finally { release.Set(); }
        Assert.NotNull(await pending);
    }
}
