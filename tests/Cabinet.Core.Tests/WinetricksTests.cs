using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class WinetricksTests : IDisposable
{
    private readonly string root = TestRoot.Create("winetricks");

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    public WinetricksTests() =>
        Directory.CreateDirectory(Path.Combine(Layout.PrefixPath("gadget"), "dosdevices"));

    [Fact]
    public void ApplyingDependenciesUsesThePrefixAndItsWineRunner()
    {
        var recorder = new RecordingRunner();
        var result = new Winetricks(Layout, recorder).Apply("gadget", ["corefonts", "vcrun2022"]);

        Assert.True(result.Ok);
        Assert.Equal(Layout.Winetricks, recorder.LastFile);
        Assert.Equal(["--unattended", "corefonts", "vcrun2022"], recorder.LastArguments);
        Assert.Equal(Layout.PrefixPath("gadget"), recorder.Environment["WINEPREFIX"]);
        Assert.Equal(Layout.Wine, recorder.Environment["WINE"]);
        var winetricks = Assert.Single(recorder.Calls, call => call.File == Layout.Winetricks);
        Assert.Equal(Layout.PrefixPath("gadget"), winetricks.WorkingDirectory);
    }

    [Fact]
    public void OpeningWinetricksShowsItsMenuWithWarningsThatCloseThemselves()
    {
        var recorder = new RecordingRunner();

        new Winetricks(Layout, recorder).Open("gadget");

        Assert.Equal(Layout.Winetricks, recorder.LastFile);
        Assert.Equal(["--unattended"], recorder.LastArguments);
    }

    [Fact]
    public void AnActiveWineSessionIsRefused()
    {
        var recorder = new RecordingRunner(
            outputs: args => args.SequenceEqual([Prefixes.SessionMode])
                ? Prefixes.SessionLiveWord
                : "");

        var refused = Assert.Throws<InvalidOperationException>(
            () => new Winetricks(Layout, recorder).Open("gadget"));

        Assert.Contains("Close every DAW", refused.Message);
        Assert.Equal(Layout.ShimPath, recorder.Calls[0].File);
        Assert.Equal([Prefixes.SessionMode], recorder.Calls[0].Arguments);
        Assert.DoesNotContain(recorder.Calls, call => call.File == Layout.Winetricks);
    }

    [Fact]
    public void AnUninitialisedPrefixIsRefused()
    {
        var recorder = new RecordingRunner();

        Assert.Throws<DirectoryNotFoundException>(
            () => new Winetricks(Layout, recorder).Open("missing"));

        Assert.Empty(recorder.Calls);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
