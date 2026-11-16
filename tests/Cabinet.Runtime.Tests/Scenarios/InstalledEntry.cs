using System.Diagnostics;
using Cabinet.Ci;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

public abstract class InstalledEntry(string id) : IAsyncLifetime
{
    private protected const string Lavapipe =
        "VK_DRIVER_FILES=/usr/lib/x86_64-linux-gnu/GL/vulkan/icd.d/lvp_icd.x86_64.json";

    private static readonly SemaphoreSlim AtOnce = new(Math.Max(3, Environment.ProcessorCount / 2));
    private readonly ScenarioHarness harness = new(Find(id));
    private RuntimeTestLock? runtimeLock;
    private Display? display;
    private string? unavailable;

    public LibraryEntry Entry { get; } = Find(id);

    internal ScenarioHarness Harness
    {
        get
        {
            if (unavailable is not null)
            {
                Assert.Skip($"{Entry.Name}'s vendor is unavailable: {unavailable}");
            }

            return harness;
        }
    }

    internal Display Display => display ?? throw new InvalidOperationException($"{id} is not installed");

    public static TheoryData<string> Formats(string id) => [.. ScenarioFormats.Tested(Find(id).Formats)];

    public async ValueTask InitializeAsync()
    {
        await AtOnce.WaitAsync();
        var clock = Stopwatch.StartNew();
        runtimeLock = RuntimeTestLock.AcquireShared();
        harness.Prepare();
        Record("prepare", clock);

        await Timed("display", () =>
        {
            display = Display.Start();
            return Task.CompletedTask;
        });
        try
        {
            await Timed("install", () => Install(Display));
        }
        catch (Exception exception) when (Outage(exception) is { } reason)
        {
            unavailable = reason;
            File.WriteAllText(Path.Combine(harness.Artefacts, VendorOutage.Record), reason);
            return;
        }

        await Timed("settle", () =>
        {
            Settle(harness.Home);
            return Task.CompletedTask;
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Timed("release", Release);
        }
        finally
        {
            await Timed("dispose", () =>
            {
                harness.Dispose();
                display?.Dispose();
                return Task.CompletedTask;
            });
            runtimeLock?.Dispose();
            AtOnce.Release();
        }
    }

    private protected virtual Task Install(Display display) => Harness.Install(Entry.DemoUrl ?? Entry.Url!, display);

    private protected virtual Task Release() => Task.CompletedTask;

    protected virtual void Settle(string home)
    {
    }

    private async Task Timed(string step, Func<Task> action)
    {
        var clock = Stopwatch.StartNew();

        try
        {
            await action();
        }
        finally
        {
            Record(step, clock);
        }
    }

    private static string? Outage(Exception exception) =>
        exception is VendorUnavailableException ? exception.Message : VendorOutage.Of(exception);

    private void Record(string step, Stopwatch clock) => harness.Time(step, clock.Elapsed);

    internal static LibraryEntry Find(string id) =>
        new Library(
                new Layout("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent", Repo.Path("data/library")),
                new UnusedRunner())
            .Find(id);
}
