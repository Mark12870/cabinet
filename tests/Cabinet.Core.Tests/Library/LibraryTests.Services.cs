using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    private const string Serviced =
        "Name: Thing\nKind: windows\nSource: byo\n"
        + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
        + "LaunchService: ThingService\n";

    [Fact]
    public void AServiceAPrefixDeclaresEndsWithTheWineThatStartedIt()
    {
        Catalogue(("thing", Serviced));
        var recorder = new RecordingRunner();

        new Library(Layout(), recorder).StopServices("thing");

        Assert.Single(
            recorder.Calls,
            call => call.File == "wineserver" && call.Arguments.SequenceEqual(["-k"]));
    }

    [Fact]
    public void APrefixThatDeclaresNoServiceIsLeftAlone()
    {
        Catalogue(("thing", Linked));
        var recorder = new RecordingRunner();

        new Library(Layout(), recorder).StopServices("thing");

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void AServiceKeepsRunningWhileADawUsesThePrefix()
    {
        Catalogue(("thing", Serviced));
        var layout = Layout();
        var recorder = new RecordingRunner();
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(layout, "thing").Busy);

        new Library(layout, recorder).StopServices("thing");

        Assert.DoesNotContain(recorder.Calls, call => call.File == "wineserver");
    }
}
