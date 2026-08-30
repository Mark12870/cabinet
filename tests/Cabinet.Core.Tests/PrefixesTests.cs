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
            Layout.PrefixPath("gadget"), "drive_c", "Program Files", "Gadget.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        File.WriteAllText(plugin, "");

        var bridged = Assert.Throws<InvalidOperationException>(() => Subject.Delete("gadget"));

        Assert.Contains("yabridgectl", bridged.Message);
        Assert.False(Directory.Exists(Layout.PrefixPath("gadget")));
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
        var documents = Profile("gadget", "testuser", "Documents");
        var host = Path.Combine(root, "Documents");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, "keep-me"), "");
        Directory.CreateSymbolicLink(documents, host);

        Subject.ContainProfile("gadget");

        Assert.Null(new DirectoryInfo(documents).LinkTarget);
        Assert.True(Directory.Exists(documents));
        Assert.True(File.Exists(Path.Combine(host, "keep-me")));
    }

    [Fact]
    public void EveryProfileInThePrefixIsContained()
    {
        var mine = Profile("gadget", "testuser", "Music");
        var everyone = Profile("gadget", "Public", "Music");
        Directory.CreateDirectory(Path.Combine(root, "Music"));
        Directory.CreateSymbolicLink(mine, Path.Combine(root, "Music"));
        Directory.CreateSymbolicLink(everyone, Path.Combine(root, "Music"));

        Subject.ContainProfile("gadget");

        Assert.Null(new DirectoryInfo(mine).LinkTarget);
        Assert.Null(new DirectoryInfo(everyone).LinkTarget);
    }

    [Fact]
    public void ALinkThatStaysInsideThePrefixIsLeftAlone()
    {
        var appData = Profile("gadget", "testuser", "AppData");
        var target = Path.Combine(Layout.PrefixPath("gadget"), "drive_c", "shared");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(appData, target);

        Subject.ContainProfile("gadget");

        Assert.Equal(target, new DirectoryInfo(appData).LinkTarget);
    }

    [Fact]
    public void ContainingAProfileTwiceChangesNothingTheSecondTime()
    {
        var documents = Profile("gadget", "testuser", "Documents");
        Directory.CreateDirectory(Path.Combine(root, "Documents"));
        Directory.CreateSymbolicLink(documents, Path.Combine(root, "Documents"));

        Subject.ContainProfile("gadget");
        File.WriteAllText(Path.Combine(documents, "written-after"), "");
        Subject.ContainProfile("gadget");

        Assert.True(File.Exists(Path.Combine(documents, "written-after")));
    }

    [Fact]
    public void APrefixWithNoProfileYetIsNotAnError()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));

        Subject.ContainProfile("gadget");
    }

    [Fact]
    public void APrefixRecordsNoRunnerUntilItIsGivenOne()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));

        Assert.Equal(Layout.BundledRunner, Subject.RunnerOf("gadget"));
    }

    [Fact]
    public void CreatingAPrefixReportsItsCompleteState()
    {
        var path = Layout.PrefixPath("gadget");
        Directory.CreateDirectory(Path.Combine(path, "dosdevices"));
        File.WriteAllText(Layout.PrefixDxvkFile("gadget"), "2.7.1");
        File.WriteAllText(Layout.PrefixSyncFile("gadget"), "fsync");
        File.WriteAllText(
            Layout.PrefixUserReg("gadget"),
            "[Software\\\\Wine\\\\Explorer]\n\"Desktop\"=\"Default\"\n"
            + "[Software\\\\Wine\\\\Explorer\\\\Desktops]\n\"Default\"=\"1280x720\"\n");

        var expected = new Prefix(
            "gadget", path, true, Layout.BundledRunner, "2.7.1", SyncMode.Fsync, "1280x720");

        Assert.Equal(expected, Subject.Create("gadget"));
        Assert.Equal(expected, Assert.Single(Subject.List()));
    }

    [Fact]
    public void SettingARunnerRecordsItWhereTheShimLooks()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.RunnerWine("wine-9.21"))!);
        File.WriteAllText(Layout.RunnerWine("wine-9.21"), "");

        Subject.SetRunner("gadget", "wine-9.21");

        Assert.Equal("wine-9.21", Subject.RunnerOf("gadget"));
        Assert.Equal(
            "wine-9.21", File.ReadAllText(Layout.PrefixRunnerFile("gadget")).Trim());
    }

    [Fact]
    public void GoingBackToBundledRemovesTheMarker()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));
        File.WriteAllText(Layout.PrefixRunnerFile("gadget"), "wine-9.21");

        Subject.SetRunner("gadget", Layout.BundledRunner);

        Assert.False(File.Exists(Layout.PrefixRunnerFile("gadget")));
    }

    [Fact]
    public void ARunnerWithoutAWineBinaryIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));
        Directory.CreateDirectory(Layout.RunnerPath("empty"));

        Assert.Throws<InvalidOperationException>(() => Subject.SetRunner("gadget", "empty"));
        Assert.False(File.Exists(Layout.PrefixRunnerFile("gadget")));
    }

    [Fact]
    public void APrefixVariableReachesTheWineItStarts()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));
        new PrefixSettings(Layout).SetVariable("gadget", "MESA_LOADER_DRIVER_OVERRIDE", "zink");
        new PrefixSettings(Layout).SetSync("gadget", SyncMode.Fsync);

        var recorder = new RecordingRunner();
        new Prefixes(Layout, recorder).Run("gadget", "winecfg", []);

        Assert.Equal("zink", recorder.Environment["MESA_LOADER_DRIVER_OVERRIDE"]);
        Assert.Equal("1", recorder.Environment["WINEFSYNC"]);
        Assert.Equal("0", recorder.Environment["WINEESYNC"]);
    }

    [Fact]
    public void APrefixVariableCannotDisplaceWhatCabinetPins()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));
        File.WriteAllLines(
            Layout.PrefixEnvFile("gadget"),
            ["WINEPREFIX=/elsewhere", "WINELOADER=/bin/false", "WAYLAND_DISPLAY=wayland-0"]);

        var recorder = new RecordingRunner();
        new Prefixes(Layout, recorder).Run("gadget", "winecfg", []);

        Assert.Equal(Layout.PrefixPath("gadget"), recorder.Environment["WINEPREFIX"]);
        Assert.Equal(Layout.Wine, recorder.Environment["WINELOADER"]);
        Assert.Equal("", recorder.Environment["WAYLAND_DISPLAY"]);
    }

    [Fact]
    public void SystemSyncLeavesTheSurroundingEnvironmentAlone()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));

        var recorder = new RecordingRunner();
        new Prefixes(Layout, recorder).Run("gadget", "winecfg", []);

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
