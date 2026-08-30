using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixSettingsTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private PrefixSettings Subject => new(Layout);

    public PrefixSettingsTests() => Directory.CreateDirectory(Layout.PrefixPath("gadget"));

    [Fact]
    public void APrefixWaitsTheWayTheSystemDoesUntilItIsToldOtherwise()
    {
        Assert.Equal(SyncMode.System, Subject.Sync("gadget"));
        Assert.Empty(PrefixSettings.SyncVariables(SyncMode.System));
    }

    [Fact]
    public void SettingASyncModeRecordsItWhereTheShimLooks()
    {
        Subject.SetSync("gadget", SyncMode.Fsync);

        Assert.Equal(SyncMode.Fsync, Subject.Sync("gadget"));
        Assert.Equal("fsync", File.ReadAllText(Layout.PrefixSyncFile("gadget")).Trim());
    }

    [Fact]
    public void GoingBackToSystemRemovesTheMarker()
    {
        Subject.SetSync("gadget", SyncMode.Ntsync);
        Subject.SetSync("gadget", SyncMode.System);

        Assert.False(File.Exists(Layout.PrefixSyncFile("gadget")));
    }

    [Fact]
    public void AWordNobodyRecognisesReadsBackAsSystem()
    {
        File.WriteAllText(Layout.PrefixSyncFile("gadget"), "gsync\n");

        Assert.Equal(SyncMode.System, Subject.Sync("gadget"));
    }

    [Theory]
    [InlineData(SyncMode.Esync, "WINEESYNC")]
    [InlineData(SyncMode.Fsync, "WINEFSYNC")]
    [InlineData(SyncMode.Ntsync, "WINENTSYNC")]
    public void ChoosingOnePrimitiveTurnsTheOtherTwoOff(SyncMode mode, string chosen)
    {
        var variables = PrefixSettings.SyncVariables(mode);

        Assert.Equal(3, variables.Count);
        Assert.Equal("1", variables[chosen]);
        Assert.Equal(["0", "0"], variables.Where(v => v.Key != chosen).Select(v => v.Value));
    }

    [Fact]
    public void ASyncModeThatIsNotAModeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PrefixSettings.ParseSync("gsync"));
        Assert.Equal(SyncMode.Ntsync, PrefixSettings.ParseSync(" NtSync "));
    }

    [Fact]
    public void APrefixCarriesNoVariablesUntilItIsGivenOne()
    {
        Assert.Empty(Subject.Variables("gadget"));
    }

    [Fact]
    public void VariablesSurviveARoundTripThroughTheFile()
    {
        Subject.SetVariable("gadget", "WINEDEBUG", "warn+all");
        Subject.SetVariable("gadget", "MESA_LOADER_DRIVER_OVERRIDE", "zink");

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["MESA_LOADER_DRIVER_OVERRIDE"] = "zink",
                ["WINEDEBUG"] = "warn+all",
            },
            Subject.Variables("gadget"));
    }

    [Fact]
    public void RemovingTheLastVariableRemovesTheFile()
    {
        Subject.SetVariable("gadget", "WINEDEBUG", "warn+all");
        Subject.SetVariable("gadget", "WINEDEBUG", null);

        Assert.False(File.Exists(Layout.PrefixEnvFile("gadget")));
    }

    [Fact]
    public void AnEmptyValueIsKeptBecauseItMeansUnsetToTheRunner()
    {
        Subject.SetVariable("gadget", "DISPLAY", "");

        Assert.Equal("", Subject.Variables("gadget")["DISPLAY"]);
    }

    [Fact]
    public void BlankCommentAndKeylessLinesAreSkipped()
    {
        File.WriteAllText(
            Layout.PrefixEnvFile("gadget"),
            "\n# a note\nnonsense\n=orphan\n  KEEP =1\nWITH=an=equals\n");

        Assert.Equal(
            new Dictionary<string, string> { ["KEEP"] = "1", ["WITH"] = "an=equals" },
            Subject.Variables("gadget"));
    }

    [Fact]
    public void ANameThatWouldNotSurviveTheFileIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Subject.SetVariable("gadget", "A=B", "1"));
        Assert.Throws<ArgumentException>(() => Subject.SetVariable("gadget", "  ", "1"));
    }

    [Fact]
    public void AVariableCabinetPinsItselfIsRefusedRatherThanQuietlyIgnored()
    {
        foreach (var owned in PrefixSettings.Owned)
        {
            Assert.Throws<ArgumentException>(() => Subject.SetVariable("gadget", owned, "1"));
        }

        Assert.False(File.Exists(Layout.PrefixEnvFile("gadget")));
    }

    [Fact]
    public void SettingAnythingOnAPrefixThatIsNotThereSaysSo()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => Subject.SetSync("never-existed", SyncMode.Fsync));
        Assert.Throws<DirectoryNotFoundException>(
            () => Subject.SetVariable("never-existed", "WINEDEBUG", "warn"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
