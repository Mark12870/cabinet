using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private Prefixes Subject => new(Layout, new UnusedRunner());

    [Fact]
    public void DeleteTakesThePrefixAndEverythingUnderIt()
    {
        var plugin = Path.Combine(
            Layout.PrefixPath("serum"), "drive_c", "Program Files", "Serum.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        File.WriteAllText(plugin, "");

        Subject.Delete("serum");

        Assert.False(Directory.Exists(Layout.PrefixPath("serum")));
    }

    [Fact]
    public void ANameThatWalksOutOfThePrefixesDirectoryIsRefused()
    {
        var outsider = Path.Combine(root, "data", "keep-me");
        Directory.CreateDirectory(outsider);

        Assert.Throws<ArgumentException>(() => Subject.Delete("../keep-me"));
        Assert.True(Directory.Exists(outsider));
    }

    [Fact]
    public void DeletingAPrefixThatIsNotThereSaysSo()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Subject.Delete("never-existed"));
    }

    [Fact]
    public void APrefixRecordsNoRunnerUntilItIsGivenOne()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Assert.Equal(Layout.BundledRunner, Subject.RunnerOf("serum"));
    }

    [Fact]
    public void SettingARunnerRecordsItWhereTheShimLooks()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.RunnerWine("wine-9.21"))!);
        File.WriteAllText(Layout.RunnerWine("wine-9.21"), "");

        Subject.SetRunner("serum", "wine-9.21");

        Assert.Equal("wine-9.21", Subject.RunnerOf("serum"));
        Assert.Equal(
            "wine-9.21", File.ReadAllText(Layout.PrefixRunnerFile("serum")).Trim());
    }

    [Fact]
    public void GoingBackToBundledRemovesTheMarker()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        File.WriteAllText(Layout.PrefixRunnerFile("serum"), "wine-9.21");

        Subject.SetRunner("serum", Layout.BundledRunner);

        Assert.False(File.Exists(Layout.PrefixRunnerFile("serum")));
    }

    [Fact]
    public void ARunnerWithoutAWineBinaryIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        Directory.CreateDirectory(Layout.RunnerPath("empty"));

        Assert.Throws<InvalidOperationException>(() => Subject.SetRunner("serum", "empty"));
        Assert.False(File.Exists(Layout.PrefixRunnerFile("serum")));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
