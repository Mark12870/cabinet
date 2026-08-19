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
        var settings = new PrefixSettings(Layout);
        settings.SetVariable("serum", "WINEPREFIX", "/elsewhere");
        settings.SetVariable("serum", "WINELOADER", "/bin/false");
        settings.SetVariable("serum", "WAYLAND_DISPLAY", "wayland-0");

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

    public void Dispose() => Directory.Delete(root, recursive: true);
}
