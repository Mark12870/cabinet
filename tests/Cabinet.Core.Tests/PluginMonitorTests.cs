using System.Diagnostics;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PluginMonitorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;
    private readonly TimeSpan quiet = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void AFileChangeWakesTheMonitor()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "VST3")).FullName;
        using var monitor = new PluginMonitor([directory], quiet);

        File.WriteAllText(Path.Combine(directory, "Synth.vst3"), "");

        Assert.True(monitor.Wait(CancellationToken.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AChangeInsideABundleWakesTheMonitor()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "VST3")).FullName;
        using var monitor = new PluginMonitor([directory], quiet);
        var bundle = Path.Combine(directory, "Synth.vst3");

        Directory.CreateDirectory(Path.Combine(bundle, "Contents"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "module"), "");

        Assert.True(monitor.Wait(CancellationToken.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void APluginRenameWakesTheMonitor()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "VST3")).FullName;
        using var monitor = new PluginMonitor([directory], quiet);
        var before = Path.Combine(directory, "Before.vst3");
        var after = Path.Combine(directory, "After.vst3");

        File.WriteAllText(before, "");
        Assert.True(monitor.Wait(CancellationToken.None, TimeSpan.FromSeconds(1)));
        File.Move(before, after);

        Assert.True(monitor.Wait(CancellationToken.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AFollowUpChangeGetsItsOwnStabilityWindow()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "VST3")).FullName;
        using var monitor = new PluginMonitor([directory], quiet);
        var plugin = Path.Combine(directory, "Synth.vst3");

        File.WriteAllText(plugin, "");
        Assert.True(monitor.Wait(CancellationToken.None, Timeout.InfiniteTimeSpan));

        File.AppendAllText(plugin, "changed");
        var started = Stopwatch.GetTimestamp();

        Assert.True(monitor.Wait(CancellationToken.None, TimeSpan.FromMilliseconds(100)));
        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(80));
    }

    [Fact]
    public async Task CancellationStopsWaiting()
    {
        using var monitor = new PluginMonitor([], quiet);
        using var cancelled = new CancellationTokenSource();
        var waiting = Task.Run(() => monitor.Wait(cancelled.Token, Timeout.InfiniteTimeSpan));

        cancelled.Cancel();

        Assert.False(await waiting);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
