using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class RunnersTests : IDisposable
{
    private readonly string root = TestRoot.Create("runners");

    [Fact]
    public void AHiddenDirectoryBesideTheRunnersIsNoRunner()
    {
        Directory.CreateDirectory(Path.Combine(Layout.RunnersDir, ".probe"));

        Assert.DoesNotContain(Subject.List(), r => r.Name == ".probe");
    }

    private Layout Layout =>
        new(root, "/run/user/1000", Path.Combine(root, "data"), null,
            Path.Combine(root, "library"));

    private IReadOnlyList<Check> Checks() => new Doctor(Layout, new UnusedRunner()).Run();

    private Runners Subject => new(Layout, new UnusedRunner());

    private void GiveRunner(string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.RunnerWine(name))!);
        File.WriteAllText(Layout.RunnerWine(name), "");
        Directory.CreateDirectory(Path.Combine(Layout.RunnerPath(name), "lib32"));
    }

    private void GiveRunnerWithoutMultilib(string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.RunnerWine(name))!);
        File.WriteAllText(Layout.RunnerWine(name), "");
    }

    private void GivePrefix(string name)
    {
        Directory.CreateDirectory(Layout.PrefixPath(name));
    }

    private void GivePrefix(string name, string runner)
    {
        Directory.CreateDirectory(Layout.PrefixPath(name));
        File.WriteAllText(Layout.PrefixRunnerFile(name), runner);
    }

    [Fact]
    public void TheBundledWineIsAlwaysListed()
    {
        Assert.Contains(Subject.List(), r => r.Name == Layout.BundledRunner && r.Bundled);
    }

    [Fact]
    public void AnUnpackedRunnerIsListedBesideIt()
    {
        GiveRunner("wine-9.21-staging");

        var found = Subject.List().Single(r => r.Name == "wine-9.21-staging");

        Assert.True(found.Usable);
        Assert.True(found.Multilib);
    }

    [Fact]
    public void ARunnerWithNo32BitTreeIsNotMultilib()
    {
        GiveRunnerWithoutMultilib("wow64-build");

        Assert.False(Subject.List().Single(r => r.Name == "wow64-build").Multilib);
    }

    [Fact]
    public void AWow64OnlyBuildIsAcceptedButSaidToBe64BitOnly()
    {
        var staging = Path.Combine(root, "staging", "wine-d2d1");
        Directory.CreateDirectory(Path.Combine(staging, "bin"));
        File.WriteAllText(Path.Combine(staging, "bin", "wine"), "");

        var archive = Path.Combine(root, "wine-d2d1-11.0-x86_64.tar.zst");
        var real = new ProcessRunner();
        Assert.True(real
            .Run(
                "tar", ["-cf", archive, "-C", Path.Combine(root, "staging"), "wine-d2d1"],
                cancellationToken: TestContext.Current.CancellationToken).Ok);

        var said = new List<string>();
        var found = new Runners(Layout, real).Add(
            archive, "wine-d2d1-11.0", said.Add,
            TestContext.Current.CancellationToken);

        Assert.False(found.Multilib);
        Assert.Contains(said, line => line.Contains("carries no 32-bit tree"));
    }

    [Fact]
    public void ARunnerAPrefixStillUsesIsNotRemoved()
    {
        GiveRunner("wine-9.21-staging");
        GivePrefix("aalto", "wine-9.21-staging");

        var refused = Assert.Throws<InvalidOperationException>(
            () => Subject.Remove("wine-9.21-staging"));

        Assert.Contains("aalto", refused.Message);
        Assert.True(Directory.Exists(Layout.RunnerPath("wine-9.21-staging")));
    }

    [Fact]
    public void ARunnerNothingUsesIsRemoved()
    {
        GiveRunner("wine-9.21-staging");
        GivePrefix("aalto");

        Subject.Remove("wine-9.21-staging");

        Assert.False(Directory.Exists(Layout.RunnerPath("wine-9.21-staging")));
    }

    [Fact]
    public void ARunnerWithoutItsWineIsListedAndWarnedAboutWithoutBeingRun()
    {
        Directory.CreateDirectory(Layout.RunnerPath("half-unpacked"));

        var broken = Subject.List().Single(r => r.Name == "half-unpacked");

        Assert.False(broken.Usable);
        Assert.Equal("unknown", Subject.Version(broken));
        Assert.Contains("half-unpacked", Checks().Single(c => c.Name == "broken runners").Detail);
    }

    [Fact]
    public void ARunnerAppearsUnderItsNameOnlyOnceItIsUnpackedWhole()
    {
        var tarball = Path.Combine(root, "wine-10.0-amd64.tar.xz");
        File.WriteAllText(tarball, "");
        var unpacked = new List<string>();
        var runners = new Runners(Layout, new RecordingRunner(args =>
        {
            var into = args[^1];
            unpacked.Add(into);
            Directory.CreateDirectory(Path.Combine(into, "bin"));
            File.WriteAllText(Path.Combine(into, "bin", "wine"), "");
        }));

        var added = runners.Add(tarball, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(Layout.RunnerPath("wine-10.0"), Assert.Single(unpacked));
        Assert.Equal(Layout.RunnerWine("wine-10.0"), added.Wine);
        Assert.True(File.Exists(added.Wine));
        Assert.Equal(
            [Layout.RunnerPath("wine-10.0")],
            Directory.EnumerateFileSystemEntries(Layout.RunnersDir));
    }

    [Fact]
    public void AnArchiveThatIsNoWineLeavesNothingInTheRunners()
    {
        var tarball = Path.Combine(root, "not-wine.tar.xz");
        File.WriteAllText(tarball, "");

        Assert.Throws<InvalidOperationException>(
            () => new Runners(Layout, new RecordingRunner()).Add(
                tarball, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFileSystemEntries(Layout.RunnersDir));
    }

    [Fact]
    public void TheBundledWineCannotBeRemoved()
    {
        Assert.Throws<ArgumentException>(() => Subject.Remove(Layout.BundledRunner));
    }

    [Fact]
    public void ANameThatWalksOutOfTheRunnersDirectoryIsRefused()
    {
        var outsider = Path.Combine(root, "data", "keep-me");
        Directory.CreateDirectory(outsider);

        Assert.Throws<ArgumentException>(() => Subject.Remove("../keep-me"));
        Assert.True(Directory.Exists(outsider));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
