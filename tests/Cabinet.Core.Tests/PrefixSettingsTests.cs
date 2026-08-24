using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixSettingsTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private PrefixSettings Subject => new(Layout);

    public PrefixSettingsTests() => Directory.CreateDirectory(Layout.PrefixPath("serum"));

    [Fact]
    public void APrefixWaitsTheWayTheSystemDoesUntilItIsToldOtherwise()
    {
        Assert.Equal(SyncMode.System, Subject.Sync("serum"));
        Assert.Empty(PrefixSettings.SyncVariables(SyncMode.System));
    }

    [Fact]
    public void SettingASyncModeRecordsItWhereTheShimLooks()
    {
        Subject.SetSync("serum", SyncMode.Fsync);

        Assert.Equal(SyncMode.Fsync, Subject.Sync("serum"));
        Assert.Equal("fsync", File.ReadAllText(Layout.PrefixSyncFile("serum")).Trim());
    }

    [Fact]
    public void GoingBackToSystemRemovesTheMarker()
    {
        Subject.SetSync("serum", SyncMode.Ntsync);
        Subject.SetSync("serum", SyncMode.System);

        Assert.False(File.Exists(Layout.PrefixSyncFile("serum")));
    }

    [Fact]
    public void AWordNobodyRecognisesReadsBackAsSystem()
    {
        File.WriteAllText(Layout.PrefixSyncFile("serum"), "gsync\n");

        Assert.Equal(SyncMode.System, Subject.Sync("serum"));
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
        Assert.Empty(Subject.Variables("serum"));
    }

    [Fact]
    public void VariablesSurviveARoundTripThroughTheFile()
    {
        Subject.SetVariable("serum", "WINEDEBUG", "warn+all");
        Subject.SetVariable("serum", "MESA_LOADER_DRIVER_OVERRIDE", "zink");

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["MESA_LOADER_DRIVER_OVERRIDE"] = "zink",
                ["WINEDEBUG"] = "warn+all",
            },
            Subject.Variables("serum"));
    }

    [Fact]
    public void RemovingTheLastVariableRemovesTheFile()
    {
        Subject.SetVariable("serum", "WINEDEBUG", "warn+all");
        Subject.SetVariable("serum", "WINEDEBUG", null);

        Assert.False(File.Exists(Layout.PrefixEnvFile("serum")));
    }

    [Fact]
    public void AnEmptyValueIsKeptBecauseItMeansUnsetToTheRunner()
    {
        Subject.SetVariable("serum", "DISPLAY", "");

        Assert.Equal("", Subject.Variables("serum")["DISPLAY"]);
    }

    [Fact]
    public void BlankCommentAndKeylessLinesAreSkipped()
    {
        File.WriteAllText(
            Layout.PrefixEnvFile("serum"),
            "\n# a note\nnonsense\n=orphan\n  KEEP =1\nWITH=an=equals\n");

        Assert.Equal(
            new Dictionary<string, string> { ["KEEP"] = "1", ["WITH"] = "an=equals" },
            Subject.Variables("serum"));
    }

    [Fact]
    public void ANameThatWouldNotSurviveTheFileIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Subject.SetVariable("serum", "A=B", "1"));
        Assert.Throws<ArgumentException>(() => Subject.SetVariable("serum", "  ", "1"));
    }

    [Fact]
    public void AVariableCabinetPinsItselfIsRefusedRatherThanQuietlyIgnored()
    {
        foreach (var owned in PrefixSettings.Owned)
        {
            Assert.Throws<ArgumentException>(() => Subject.SetVariable("serum", owned, "1"));
        }

        Assert.False(File.Exists(Layout.PrefixEnvFile("serum")));
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
