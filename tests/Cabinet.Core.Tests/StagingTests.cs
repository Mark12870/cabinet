using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class StagingTests : IDisposable
{
    private readonly string root = TestRoot.Create("staging");

    private Layout Layout => new(
        root,
        Path.Combine(root, "runtime"),
        Path.Combine(root, "data"),
        Path.Combine(root, "files"),
        yabridgeDir: Path.Combine(root, "bundled"),
        tempDir: Path.Combine(root, "tmp"));

    [Fact]
    public void EveryOperationStagesInADirectoryOfItsOwnThatGoesWhenItEnds()
    {
        var first = Staging.Create(Layout.TempDir, "library");
        var second = Staging.Create(Layout.TempDir, "library");

        Assert.NotEqual(first.Path, second.Path);

        first.Dispose();

        Assert.False(Directory.Exists(first.Path));
        Assert.True(Directory.Exists(second.Path));
        Assert.Equal(
            [second.Path, second.Path + ".lock"],
            Directory.EnumerateFileSystemEntries(Layout.TempDir).Order(StringComparer.Ordinal));
        second.Dispose();
        Assert.Empty(Directory.EnumerateFileSystemEntries(Layout.TempDir));
    }

    [Fact]
    public void StartingCabinetClearsOnlyStagingWhoseOperationHasStopped()
    {
        var stopped = Path.Combine(Layout.RunnersDir, ".cabinet-staging-runner-0123456789ab");
        Directory.CreateDirectory(Path.Combine(stopped, "bin"));
        File.WriteAllText(stopped + ".lock", "");
        var foreign = Path.Combine(Layout.TempDir, "cabinet-library");
        Directory.CreateDirectory(foreign);
        using var running = Staging.Create(Layout.PrefixesDir, "prefix");

        Bootstrap.Ensure(Layout);

        Assert.False(Path.Exists(stopped));
        Assert.False(Path.Exists(stopped + ".lock"));
        Assert.True(Directory.Exists(running.Path));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public void AMarkerIsReadBackOnlyOnceTheOperationThatWroteItHasStopped()
    {
        var marker = Path.Combine(root, "marker");
        var underway = Underway.Begin(marker)!;
        underway.Note("first\tcreated");

        Assert.Null(Underway.Abandoned(marker));
        Assert.Null(Underway.Begin(marker));

        underway.Dispose();

        Assert.Equal("first\tcreated", Underway.Abandoned(marker));

        using var again = Underway.Begin(marker)!;
        Assert.Equal("first\tcreated", again.Left);
        again.Finish();

        Assert.False(Underway.Marked(marker));
        Assert.Null(Underway.Abandoned(marker));
        Assert.Null(Underway.Begin(marker)!.Left);
    }

    [UnprivilegedFact]
    public void StagingThatCannotBeClearedIsLeftForLaterRatherThanStoppingCabinet()
    {
        var stuck = Path.Combine(Layout.TempDir, ".cabinet-staging-library-0123456789ab");
        var sealedIn = Path.Combine(stuck, "sealed");
        Directory.CreateDirectory(sealedIn);
        File.WriteAllText(Path.Combine(sealedIn, "file"), "");
        File.WriteAllText(stuck + ".lock", "");
        File.SetUnixFileMode(sealedIn, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            Bootstrap.Ensure(Layout);

            Assert.True(File.Exists(stuck + ".lock"));
        }
        finally
        {
            File.SetUnixFileMode(sealedIn, UnixFileMode.UserRead | UnixFileMode.UserWrite
                                           | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void AMarkerNobodyLeftIsNoInterruption()
    {
        using var underway = Underway.Begin(Path.Combine(root, "marker"))!;

        Assert.Null(underway.Left);
        Assert.Null(Underway.Abandoned(Path.Combine(root, "absent")));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
