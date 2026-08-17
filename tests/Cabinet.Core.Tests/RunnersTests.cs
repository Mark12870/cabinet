using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class RunnersTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

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

        var check = new Doctor(Layout).Run().Single(c => c.Name == "prefix runners");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("aalto -> wine-9.21-staging", check.Detail);
    }

    [Fact]
    public void DoctorPassesOnceThatRunnerIsThere()
    {
        GivePrefix("aalto", "wine-9.21-staging");
        GiveRunner("wine-9.21-staging");

        Assert.Equal(
            Status.Ok, new Doctor(Layout).Run().Single(c => c.Name == "prefix runners").Status);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
