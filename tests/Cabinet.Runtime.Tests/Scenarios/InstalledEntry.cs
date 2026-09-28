using System.Diagnostics;
using System.Globalization;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

public abstract class InstalledEntry(string id) : IAsyncLifetime
{
    private RuntimeTestLock? runtimeLock;
    private Display? display;

    public LibraryEntry Entry { get; } = Find(id);

    internal ScenarioHarness Harness { get; } = new(id, Find(id).Kind);

    internal Display Display => display ?? throw new InvalidOperationException($"{id} is not installed");

    public static TheoryData<string> Formats(string id) => [.. Find(id).Formats.Where(format => format != "CLAP")];

    public async Task InitializeAsync()
    {
        var clock = Stopwatch.StartNew();
        runtimeLock = RuntimeTestLock.Acquire();
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

    public async Task DisposeAsync()
    {
        await Timed("dispose", () =>
        {
            Harness.Dispose();
            display?.Dispose();
            return Task.CompletedTask;
        });
        runtimeLock?.Dispose();
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

    private void Record(string step, Stopwatch clock) =>
        File.AppendAllText(
            Path.Combine(Harness.Artefacts, "timing.txt"),
            string.Create(CultureInfo.InvariantCulture, $"{step} {clock.Elapsed.TotalSeconds:F1}\n"));

    private static LibraryEntry Find(string id) =>
        new Library(
                new Layout("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent", Repo.Path("data/library")),
                new UnusedRunner())
            .Find(id);
}
