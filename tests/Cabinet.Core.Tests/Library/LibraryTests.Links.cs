using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Fact]
    public void ALinkGoesToTheRunningManagerThroughItsSession()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"Thing.exe\",\"42\",\"Console\",\"1\",\"90,112 K\"",
            dawSession: true);
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        new Library(layout, recorder).Open("thingmanager://signed-in?code=1");

        Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual(
                [Prefixes.JoinMode, "start", "thingmanager://signed-in?code=1"]));
        Assert.DoesNotContain(
            recorder.Calls, call => call.Arguments.Contains(@"C:\Program Files\Thing\Thing.exe"));
    }

    [Fact]
    public void ALinkForAClosedManagerOpensItWithTheLink()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        new Library(layout, recorder).Open("thingmanager://signed-in?code=1");

        Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual(
                [Prefixes.JoinMode, @"C:\Program Files\Thing\Thing.exe", "thingmanager://signed-in?code=1"]));
        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains("start"));
    }

    [Fact]
    public void ALiveSessionWithoutTheManagerOpensItWithTheLink()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"yabridge-host.exe\",\"42\",\"Console\",\"1\",\"90,112 K\"",
            dawSession: true);
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        new Library(layout, recorder).Open("thingmanager://signed-in?code=1");

        Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual(
                [Prefixes.JoinMode, @"C:\Program Files\Thing\Thing.exe", "thingmanager://signed-in?code=1"]));
        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains("start"));
    }

    [Fact]
    public void ALinkFindsItsManagerWhateverTheCaseOfItsScheme()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        new Library(layout, recorder).Open("ThingManager://signed-in");

        Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual(
                [Prefixes.JoinMode, @"C:\Program Files\Thing\Thing.exe", "ThingManager://signed-in"]));
    }

    [Fact]
    public void ALinkGoesToThePrefixTheManagerIsInstalledIn()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("other"));
        File.WriteAllText(layout.PrefixPluginsFile("other"), "thing\n");

        new Library(layout, recorder).Open("thingmanager://signed-in");

        var opened = Assert.Single(
            recorder.Calls, call => call.Arguments.Contains("thingmanager://signed-in"));
        Assert.Equal(layout.PrefixPath("other"), opened.Environment["WINEPREFIX"]);
    }

    [Theory]
    [InlineData("thingmanager")]
    [InlineData(":signed-in")]
    public void SomethingThatIsNotALinkIsRefused(string link)
    {
        Catalogue(("thing", Linked));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new Library(Layout(), new RecordingRunner()).Open(link));

        Assert.Contains($"{link} is not a link", thrown.Message);
    }

    [Fact]
    public void HandingALinkOverIsWrittenToTheLaunchLog()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"Thing.exe\",\"42\",\"Console\",\"1\",\"90,112 K\"",
            dawSession: true);
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        new Library(layout, recorder).Open("thingmanager://signed-in");

        var handed = Assert.Single(recorder.Calls, call => call.Arguments.Contains("start"));
        Assert.Equal(layout.PrefixLaunchLog("thing"), handed.LogTo);
        Assert.Contains(
            "Handing the link to Thing.", File.ReadAllText(layout.PrefixLaunchLog("thing")));
    }

    [Fact]
    public void ALinkIsMatchedToItsManagerWithoutRunningAnything()
    {
        Catalogue(("thing", Linked));
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        var entry = new Library(layout, recorder).ForLink("ThingManager://signed-in");

        Assert.Equal("thing", entry.Id);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void ALinkNoAppInTheLibraryRegistersIsRefused()
    {
        Catalogue(("thing", Linked));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new Library(Layout(), new RecordingRunner()).Open("othermanager://signed-in"));

        Assert.Contains("no app in the library opens othermanager: links", thrown.Message);
    }

    [Fact]
    public void ALinkForAManagerThatIsNotInstalledSaysSo()
    {
        Catalogue(("thing", Linked));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new Library(Layout(), new RecordingRunner()).Open("thingmanager://signed-in"));

        Assert.Equal("Thing opens thingmanager: links but is not installed", thrown.Message);
    }
}
