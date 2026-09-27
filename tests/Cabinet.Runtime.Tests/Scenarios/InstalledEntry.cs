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
        runtimeLock = RuntimeTestLock.Acquire();
        Harness.Prepare();
        display = Display.Start();
        await Install(display);
        Settle(Harness.Home);
    }

    public Task DisposeAsync()
    {
        Harness.Dispose();
        display?.Dispose();
        runtimeLock?.Dispose();
        return Task.CompletedTask;
    }

    private protected virtual Task Install(Display display) => Harness.Install(Entry.DemoUrl ?? Entry.Url!, display);

    protected virtual void Settle(string home)
    {
    }

    private static LibraryEntry Find(string id) =>
        new Library(
                new Layout("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent", Repo.Path("data/library")),
                new UnusedRunner())
            .Find(id);
}
