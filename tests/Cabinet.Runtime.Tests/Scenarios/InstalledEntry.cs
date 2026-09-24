using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests;

public abstract class InstalledEntry(string id) : IAsyncLifetime
{
    private RuntimeTestLock? runtimeLock;
    private Display? display;

    public LibraryEntry Entry { get; } = Find(id);

    internal ScenarioHarness Harness { get; } = new(id);

    internal Display Display => display ?? throw new InvalidOperationException($"{id} is not installed");

    public static TheoryData<string> Formats(string id) => [.. Find(id).Formats];

    public async Task InitializeAsync()
    {
        runtimeLock = RuntimeTestLock.Acquire();
        Harness.Prepare();
        display = Display.Start();
        await Harness.Install(Entry.Url!, display);
    }

    public Task DisposeAsync()
    {
        Harness.Dispose();
        display?.Dispose();
        runtimeLock?.Dispose();
        return Task.CompletedTask;
    }

    private static LibraryEntry Find(string id) =>
        new Library(
                new Layout("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent", Repo.Path("data/library")),
                new UnusedRunner())
            .Find(id);
}
