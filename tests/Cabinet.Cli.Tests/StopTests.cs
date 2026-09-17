using Cabinet.Core.Tests;

namespace Cabinet.Cli.Tests;

public sealed class StopTests
{
    private const string Manager =
        "Name: Thing\nKind: windows\nSource: byo\nLaunch: C:\\Thing\\Thing.exe\n";

    [Fact]
    public void AnAppThatClosesReportsItAndSucceeds()
    {
        using var cli = new Cli();
        cli.Catalogue("thing", Manager);
        cli.Prefix("thing", "thing");

        var outcome = cli.Run("library", "stop", "thing");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("Thing is closed.", outcome.Out);
    }

    [Fact]
    public void AnAppInAPrefixADawIsBridgingCannotEvenBeOpened()
    {
        using var cli = new Cli();
        cli.Catalogue("thing", Manager);
        cli.Prefix("thing", "thing");
        using var plugin = SessionFiles.HeldByAPlugin(
            SessionFiles.Of(cli.Layout, "thing").Busy);

        var outcome = cli.Run("library", "launch", "thing");

        Assert.Equal(0, outcome.Exit);
        Assert.Empty(outcome.Error);
    }
}
