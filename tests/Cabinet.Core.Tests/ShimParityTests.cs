using System.Text.RegularExpressions;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class ShimParityTests
{
    private static readonly string Shim = Repo.Read("shim/src/main.rs");

    [Fact]
    public void TheShimLooksForTheMarkerFilesCabinetWrites()
    {
        Assert.Equal(Layout.RunnerMarker, Constant("RUNNER_MARKER"));
        Assert.Equal(Layout.SyncMarker, Constant("SYNC_MARKER"));
        Assert.Equal(Layout.EnvMarker, Constant("ENV_MARKER"));
        Assert.Equal(Layout.BundledRunner, Constant("BUNDLED_RUNNER"));
    }

    [Fact]
    public void BothSidesKeepWineOffTheSameSockets()
    {
        Assert.Equal(Prefixes.Blanked, List("BLANKED"));
    }

    [Fact]
    public void BothSidesRecogniseWinesDesktopWindow()
    {
        Assert.Equal(Layout.WineDesktopTitle, Constant("DESKTOP_TITLE"));
    }

    [Fact]
    public void BothSidesNameTheSessionModesTheSameWay()
    {
        Assert.Equal(Prefixes.JoinMode, Constant("JOIN_MODE"));
        Assert.Equal(Prefixes.SessionMode, Constant("SESSION_MODE"));
        Assert.Equal(Prefixes.SessionLiveWord, Constant("SESSION_LIVE"));
    }

    [Fact]
    public void NeitherSideLetsAPrefixTakeOverWhatCabinetPins()
    {
        Assert.Equal(PrefixSettings.Owned, List("CABINET_OWNED"));
    }

    [Fact]
    public void BothSidesSetTheSameVariablesForTheSameSyncMode()
    {
        var variables = List("SYNC_VARS");

        foreach (var (word, values) in Modes())
        {
            var shim = variables.Zip(values).ToDictionary(
                pair => pair.First, pair => pair.Second, StringComparer.Ordinal);

            Assert.Equal(
                PrefixSettings.SyncVariables(PrefixSettings.ParseSync(word)),
                shim);
        }
    }

    [Fact]
    public void NeitherSideKnowsAModeTheOtherDoesNot()
    {
        var mine = PrefixSettings.SyncModes
            .Where(mode => mode != SyncMode.System)
            .Select(PrefixSettings.Word);

        Assert.Equal(mine.Order(), Modes().Select(mode => mode.Word).Order());
    }

    private static string Constant(string name) =>
        Match($"""const {name}: &str = "([^"]*)";""");

    private static string[] List(string name) =>
        Regex.Matches(Match($@"const {name}:[^=]*= &?\[([^\]]*)\];"), @"""([^""]*)""")
            .Select(found => found.Groups[1].Value)
            .ToArray();

    private static IEnumerable<(string Word, string[] Values)> Modes() =>
        Regex.Matches(
                Match(@"const SYNC_MODES:[^=]*= &\[(.*?)\n\];"),
                @"\(""([^""]*)"", \[([^\]]*)\]\)",
                RegexOptions.Singleline)
            .Select(found => (
                found.Groups[1].Value,
                Regex.Matches(found.Groups[2].Value, @"""([^""]*)""")
                    .Select(value => value.Groups[1].Value)
                    .ToArray()));

    private static string Match(string pattern)
    {
        var found = Regex.Match(Shim, pattern, RegexOptions.Singleline);

        return found.Success
            ? found.Groups[1].Value
            : throw new InvalidOperationException($"shim/src/main.rs no longer has {pattern}");
    }
}
