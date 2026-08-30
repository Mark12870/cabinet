using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class RunnersTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    [Fact]
    public void APrefixOnTheRunnerItsPluginAsksForIsNotComplainedAbout()
    {
        GiveRunner("soda-11.0-5");
        GivePrefix("fabfilter", "soda-11.0-5");
        GiveEntry("fabfilter", "fabfilter-total-bundle", "FabFilter Total Bundle", "soda-11.0-5");
        GiveRecord("fabfilter", "fabfilter-total-bundle");

        Assert.DoesNotContain(Checks(), c => c.Name == "plugin runners");
    }

    [Fact]
    public void AVersionSpecMatchesTheDirectoryTheRunnerWasUnpackedUnder()
    {
        GiveRunner("wine-9.21-staging-tkg");
        GivePrefix("gadget", "wine-9.21-staging-tkg");
        GiveEntry("acme", "gadget", "Gadget 2", "9.21");
        GiveRecord("gadget", "gadget");

        Assert.DoesNotContain(Checks(), c => c.Name == "plugin runners");
    }

    [Fact]
    public void APrefixMovedOffTheRunnerItsPluginAsksForIsWarnedAbout()
    {
        GiveRunner("wine-10.8-staging-tkg");
        GivePrefix("gadget", "wine-10.8-staging-tkg");
        GiveEntry("acme", "gadget", "Gadget 2", "9.21");
        GiveRecord("gadget", "gadget");

        var check = Checks().Single(c => c.Name == "plugin runners");

        Assert.Equal(Status.Warn, check.Status);
        Assert.Contains("gadget keeps wine-10.8-staging-tkg", check.Detail);
        Assert.Contains("Gadget 2 asks for Wine 9.21", check.Detail);
    }

    [Fact]
    public void APrefixHoldingNoRecordedPluginIsNobodysBusiness()
    {
        GivePrefix("aalto");

        Assert.DoesNotContain(Checks(), c => c.Name == "plugin runners");
    }

    private Layout Layout =>
        new(root, "/run/user/1000", Path.Combine(root, "data"), null,
            Path.Combine(root, "library"));

    private IReadOnlyList<Check> Checks() => new Doctor(Layout, new UnusedRunner()).Run();

    private void GiveEntry(string vendor, string id, string name, string runner,
        string? sync = null)
    {
        var dir = Path.Combine(root, "library", vendor);
        Directory.CreateDirectory(dir);

        File.WriteAllText(
            Path.Combine(dir, id + ".yml"),
            $"Name: {name}\nKind: windows\nSource: byo\nRunner: {runner}\n"
            + (sync is null ? "" : $"Sync: {sync}\n"));
    }

    [Fact]
    public void APrefixOnTheSyncModeItsPluginAsksForIsNotComplainedAbout()
    {
        GiveRunner("wine-9.21-staging-tkg");
        GivePrefix("gadget", "wine-9.21-staging-tkg");
        GiveEntry("acme", "gadget", "Gadget 2", "9.21", "fsync");
        GiveRecord("gadget", "gadget");
        File.WriteAllText(Layout.PrefixSyncFile("gadget"), "fsync");

        Assert.DoesNotContain(Checks(), c => c.Name == "plugin sync");
    }

    [Fact]
    public void APrefixThatNeverTookTheSyncModeItsPluginAsksForIsWarnedAbout()
    {
        GiveRunner("wine-9.21-staging-tkg");
        GivePrefix("gadget", "wine-9.21-staging-tkg");
        GiveEntry("acme", "gadget", "Gadget 2", "9.21", "fsync");
        GiveRecord("gadget", "gadget");

        var check = Checks().Single(c => c.Name == "plugin sync");

        Assert.Equal(Status.Warn, check.Status);
        Assert.Contains("gadget runs on system", check.Detail);
        Assert.Contains("Gadget 2 asks for fsync", check.Detail);
    }

    private void GiveRecord(string prefix, string id) =>
        File.WriteAllText(Layout.PrefixPluginsFile(prefix), id + Environment.NewLine);

    private Runners Subject => new(Layout, new UnusedRunner());

    private void GiveRunner(string name, bool multilib = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.RunnerWine(name))!);
        File.WriteAllText(Layout.RunnerWine(name), "");

        if (multilib)
        {
            Directory.CreateDirectory(Path.Combine(Layout.RunnerPath(name), "lib32"));
        }
    }

    private void GivePrefix(string name, string? runner = null)
    {
        Directory.CreateDirectory(Layout.PrefixPath(name));

        if (runner is not null)
        {
            File.WriteAllText(Layout.PrefixRunnerFile(name), runner);
        }
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
        GiveRunner("wow64-build", multilib: false);

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
            .Run("tar", ["-cf", archive, "-C", Path.Combine(root, "staging"), "wine-d2d1"]).Ok);

        var said = new List<string>();
        var found = new Runners(Layout, real).Add(archive, "wine-d2d1-11.0", said.Add);

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

    [Fact]
    public void DoctorFailsWhenAPrefixNamesARunnerThatIsGone()
    {
        GivePrefix("aalto", "wine-9.21-staging");

        var check = Checks().Single(c => c.Name == "prefix runners");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("aalto -> wine-9.21-staging", check.Detail);
    }

    [Fact]
    public void DoctorPassesOnceThatRunnerIsThere()
    {
        GivePrefix("aalto", "wine-9.21-staging");
        GiveRunner("wine-9.21-staging");

        Assert.Equal(
            Status.Ok, Checks().Single(c => c.Name == "prefix runners").Status);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
