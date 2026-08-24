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

        var bridged = Assert.Throws<InvalidOperationException>(() => Subject.Delete("serum"));

        Assert.Contains("yabridgectl", bridged.Message);
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
    public void AProfileLinkOutOfThePrefixBecomesADirectoryInsideIt()
    {
        var documents = Profile("serum", "testuser", "Documents");
        var host = Path.Combine(root, "Documents");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, "keep-me"), "");
        Directory.CreateSymbolicLink(documents, host);

        Subject.ContainProfile("serum");

        Assert.Null(new DirectoryInfo(documents).LinkTarget);
        Assert.True(Directory.Exists(documents));
        Assert.True(File.Exists(Path.Combine(host, "keep-me")));
    }

    [Fact]
    public void EveryProfileInThePrefixIsContained()
    {
        var mine = Profile("serum", "testuser", "Music");
        var everyone = Profile("serum", "Public", "Music");
        Directory.CreateDirectory(Path.Combine(root, "Music"));
        Directory.CreateSymbolicLink(mine, Path.Combine(root, "Music"));
        Directory.CreateSymbolicLink(everyone, Path.Combine(root, "Music"));

        Subject.ContainProfile("serum");

        Assert.Null(new DirectoryInfo(mine).LinkTarget);
        Assert.Null(new DirectoryInfo(everyone).LinkTarget);
    }

    [Fact]
    public void ALinkThatStaysInsideThePrefixIsLeftAlone()
    {
        var appData = Profile("serum", "testuser", "AppData");
        var target = Path.Combine(Layout.PrefixPath("serum"), "drive_c", "shared");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(appData, target);

        Subject.ContainProfile("serum");

        Assert.Equal(target, new DirectoryInfo(appData).LinkTarget);
    }

    [Fact]
    public void ContainingAProfileTwiceChangesNothingTheSecondTime()
    {
        var documents = Profile("serum", "testuser", "Documents");
        Directory.CreateDirectory(Path.Combine(root, "Documents"));
        Directory.CreateSymbolicLink(documents, Path.Combine(root, "Documents"));

        Subject.ContainProfile("serum");
        File.WriteAllText(Path.Combine(documents, "written-after"), "");
        Subject.ContainProfile("serum");

        Assert.True(File.Exists(Path.Combine(documents, "written-after")));
    }

    [Fact]
    public void APrefixWithNoProfileYetIsNotAnError()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Subject.ContainProfile("serum");
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

    [Fact]
    public void APrefixVariableReachesTheWineItStarts()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        new PrefixSettings(Layout).SetVariable("serum", "MESA_LOADER_DRIVER_OVERRIDE", "zink");
        new PrefixSettings(Layout).SetSync("serum", SyncMode.Fsync);

        var recorder = new RecordingRunner();
        new Prefixes(Layout, recorder).Run("serum", "winecfg", []);

        Assert.Equal("zink", recorder.Environment["MESA_LOADER_DRIVER_OVERRIDE"]);
        Assert.Equal("1", recorder.Environment["WINEFSYNC"]);
        Assert.Equal("0", recorder.Environment["WINEESYNC"]);
    }

    [Fact]
    public void APrefixVariableCannotDisplaceWhatCabinetPins()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        File.WriteAllLines(
            Layout.PrefixEnvFile("serum"),
            ["WINEPREFIX=/elsewhere", "WINELOADER=/bin/false", "WAYLAND_DISPLAY=wayland-0"]);

        var recorder = new RecordingRunner();
        new Prefixes(Layout, recorder).Run("serum", "winecfg", []);

        Assert.Equal(Layout.PrefixPath("serum"), recorder.Environment["WINEPREFIX"]);
        Assert.Equal(Layout.Wine, recorder.Environment["WINELOADER"]);
        Assert.Equal("", recorder.Environment["WAYLAND_DISPLAY"]);
    }

    [Fact]
    public void SystemSyncLeavesTheSurroundingEnvironmentAlone()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        var recorder = new RecordingRunner();
        new Prefixes(Layout, recorder).Run("serum", "winecfg", []);

        Assert.DoesNotContain("WINEFSYNC", recorder.Environment.Keys);
        Assert.DoesNotContain("WINENTSYNC", recorder.Environment.Keys);
    }

    private string Profile(string prefix, string user, string folder)
    {
        var path = Path.Combine(
            Layout.PrefixPath(prefix), "drive_c", "users", user, folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
