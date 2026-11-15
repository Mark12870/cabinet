using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PluginMonitorTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void AChangeWakesTheMonitorAfterItsStabilityWindow()
    {
        var waits = new Queue<int>([0, WaitHandle.WaitTimeout]);
        var timeouts = new List<TimeSpan>();
        using var monitor = Monitor(waits, timeouts);

        Assert.True(monitor.Wait(CancellationToken.None, Timeout.InfiniteTimeSpan));
        Assert.Equal([Timeout.InfiniteTimeSpan, Quiet], timeouts);
    }

    [Fact]
    public void AFiniteWaitUsesItsTimeoutAsTheStabilityWindow()
    {
        var waits = new Queue<int>([0, WaitHandle.WaitTimeout]);
        var timeouts = new List<TimeSpan>();
        using var monitor = Monitor(waits, timeouts);
        var timeout = TimeSpan.FromSeconds(1);

        Assert.True(monitor.Wait(CancellationToken.None, timeout));
        Assert.Equal([timeout, timeout], timeouts);
    }

    [Fact]
    public void CancellationStopsWaiting()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var observed = false;
        using var monitor = new PluginMonitor(Quiet, (handles, _) =>
        {
            observed = handles[1].WaitOne(0);
            return 1;
        });

        Assert.False(monitor.Wait(cancelled.Token, Timeout.InfiniteTimeSpan));
        Assert.True(observed);
    }

    [Fact]
    public void AMonitorOverTheInotifyLimitRescansOnATimer()
    {
        var timeouts = new List<TimeSpan>();
        using var monitor = new PluginMonitor(
            ["/plugins"],
            Quiet,
            _ => throw new IOException("The configured user limit (128) on the number of inotify instances has been reached"),
            (_, timeout) =>
            {
                timeouts.Add(timeout);
                return WaitHandle.WaitTimeout;
            });

        Assert.True(monitor.Wait(CancellationToken.None, Timeout.InfiniteTimeSpan));
        Assert.Equal([PluginMonitor.Rescan], timeouts);
    }

    [Fact]
    public void AChangeSeenWhileRescanningSettlesAfterTheQuietInterval()
    {
        var waits = new Queue<int>([0, WaitHandle.WaitTimeout]);
        var timeouts = new List<TimeSpan>();
        using var monitor = new PluginMonitor(
            ["/plugins"],
            Quiet,
            _ => throw new IOException("The configured user limit (128) on the number of inotify instances has been reached"),
            (_, timeout) =>
            {
                timeouts.Add(timeout);
                return waits.Dequeue();
            });

        Assert.True(monitor.Wait(CancellationToken.None, Timeout.InfiniteTimeSpan));
        Assert.Equal([PluginMonitor.Rescan, Quiet], timeouts);
    }

    private static PluginMonitor Monitor(Queue<int> waits, List<TimeSpan> timeouts) =>
        new(Quiet, (_, timeout) =>
        {
            timeouts.Add(timeout);
            return waits.Dequeue();
        });
}
