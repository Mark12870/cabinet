using System.Diagnostics;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

public abstract class InstalledEntry(string id) : IAsyncLifetime
{
    private protected const string Lavapipe =
        "VK_DRIVER_FILES=/usr/lib/x86_64-linux-gnu/GL/vulkan/icd.d/lvp_icd.x86_64.json";

    private static readonly SemaphoreSlim AtOnce = new(3);
    private RuntimeTestLock? runtimeLock;
    private Display? display;

    public LibraryEntry Entry { get; } = Find(id);

    internal ScenarioHarness Harness { get; } = new(Find(id));

    internal Display Display => display ?? throw new InvalidOperationException($"{id} is not installed");

    public static TheoryData<string> Formats(string id) => [.. Find(id).Formats.Where(format => format != "CLAP")];

    public async ValueTask InitializeAsync()
    {
        await AtOnce.WaitAsync();
        var clock = Stopwatch.StartNew();
        runtimeLock = RuntimeTestLock.AcquireShared();
        Harness.Prepare();
        Record("prepare", clock);

        await Timed("display", () =>
        {
            display = Display.Start();
            return Task.CompletedTask;
        });
        await Timed("install", () => Install(Display));
        await Timed("settle", () =>
        {
            Settle(Harness.Home);
            return Task.CompletedTask;
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Timed("dispose", () =>
        {
            Harness.Dispose();
            display?.Dispose();
            return Task.CompletedTask;
        });
        runtimeLock?.Dispose();
        AtOnce.Release();
    }

    private protected virtual Task Install(Display display) => Harness.Install(Entry.DemoUrl ?? Entry.Url!, display);

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

    private void Record(string step, Stopwatch clock) => Harness.Time(step, clock.Elapsed);

    private static LibraryEntry Find(string id) =>
        new Library(
                new Layout("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent", Repo.Path("data/library")),
                new UnusedRunner())
            .Find(id);
}
