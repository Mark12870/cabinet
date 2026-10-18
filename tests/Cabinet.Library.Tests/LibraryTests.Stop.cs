using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Fact]
    public void StoppingAnAppEndsItsHelperToo()
    {
        var entry = LibraryEntry.Parse("thing", Linked + "LaunchHelper: ThingHelper.exe\n");
        var layout = Layout();
        var recorder = new RecordingRunner(outputs: _ => Gone);
        Recorded(layout, entry);

        new Library(layout, recorder).Stop(entry);

        Assert.Single(recorder.Calls, call => call.Arguments.SequenceEqual(
            [Prefixes.JoinMode, "taskkill", "/f", "/im", "ThingHelper.exe"]));
        Assert.Contains(
            "Closing ThingHelper.exe.", File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
    }

    [Fact]
    public void AnAppStopLeftAloneKeepsItsHelper()
    {
        var entry = LibraryEntry.Parse("thing", Linked + "LaunchHelper: ThingHelper.exe\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"Thing.exe\",\"42\",\"Console\",\"1\",\"90,112 K\"",
            dawSession: true);
        Recorded(layout, entry);

        new Library(layout, recorder).Stop(entry, grace: TimeSpan.Zero);

        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains("ThingHelper.exe"));
    }

    [Fact]
    public void StoppingAnAppKillsItByNameAndLetsTheRestOfThePrefixBe()
    {
        var entry = Manager();
        var layout = Layout();
        var recorder = new RecordingRunner(outputs: _ => Gone);
        Recorded(layout, entry);

        new Library(layout, recorder).Stop(entry);

        var killed = Assert.Single(recorder.Calls, call => call.Arguments.Contains("taskkill"));

        Assert.Equal(layout.ShimPath, killed.File);
        Assert.Equal([Prefixes.JoinMode, "taskkill", "/f", "/im", "Thing.exe"], killed.Arguments);
        Assert.Equal(layout.PrefixLaunchLog(entry.Prefix), killed.LogTo);
        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains("-k"));
        Assert.Contains(
            "Thing is closed.", File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
    }

    [Fact]
    public void AnAppThatOutlivesTheGraceIsEndedWithTheRestOfThePrefixesWine()
    {
        var entry = Manager();
        var layout = Layout();
        var listing = "\"Thing.exe\",\"316\",\"Console\",\"1\",\"64 K\"";
        var ended = false;
        var recorder = new RecordingRunner(
            acts: args => ended = ended || args.Contains("wineboot"),
            outputs: _ => ended ? "" : listing);
        Recorded(layout, entry);

        var outcome = new Library(layout, recorder).Stop(entry, grace: TimeSpan.Zero);

        Assert.Equal(StopResult.Forced, outcome.Result);
        Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual([Prefixes.JoinMode, "wineboot", "-k"]));
        Assert.Contains(
            "Ending every Wine process in thing, Cabinet's own included",
            File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
        Assert.Contains("Thing would not close", outcome.Told);
    }

    [Fact]
    public void AnAppInAPrefixADawIsBridgingIsLeftRunning()
    {
        var entry = Manager();
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"Thing.exe\",\"316\",\"Console\",\"1\",\"64 K\"");
        Recorded(layout, entry);
        using var plugin = SessionFiles.HeldByAPlugin(
            SessionFiles.Of(layout, entry.Prefix).Busy);

        var outcome = new Library(layout, recorder).Stop(entry, grace: TimeSpan.Zero);

        Assert.Equal(StopResult.LeftRunning, outcome.Result);
        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains("wineboot"));
        Assert.Contains("A DAW is using plugins from thing", outcome.Told);
        Assert.Contains(
            "A DAW is using plugins from thing",
            File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
    }

    [Fact]
    public void AnAppWhoseNameHasASpaceIsStillSeenRunning()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing Manager.exe" + "\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"Thing Manager.exe\",\"316\",\"Console\",\"1\",\"64 K\"");
        Recorded(layout, entry);

        new Library(layout, recorder).Stop(entry, grace: TimeSpan.Zero);

        Assert.Contains(
            "Thing was still running",
            File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
    }

    [Fact]
    public void StoppingAnAppStopsTheServiceItStarted()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchService: ThingService\n");

        var layout = Layout();
        var recorder = new RecordingRunner(outputs: _ => Gone);
        Recorded(layout, entry);

        new Library(layout, recorder).Stop(entry);

        var killed = recorder.Calls.ToList().FindIndex(call =>
            call.Arguments.Contains("taskkill"));

        var stopped = recorder.Calls.ToList().FindIndex(call =>
            call.Arguments.SequenceEqual([Prefixes.JoinMode, "sc", "stop", "ThingService"]));

        Assert.Equal(killed + 1, stopped);
    }

    [Fact]
    public void StoppingKeepsWhatTheLaunchWroteToTheLog()
    {
        var entry = Manager();
        var layout = Layout();
        var log = layout.PrefixLaunchLog(entry.Prefix);

        Recorded(layout, entry);
        File.WriteAllText(log, "Opening Thing.\n");

        new Library(layout, new RecordingRunner(outputs: _ => Gone)).Stop(entry);

        Assert.Equal(
            "Opening Thing.\nClosing Thing.\nThing is closed.\n", File.ReadAllText(log));
    }

    [Fact]
    public void APluginCannotBeStopped()
    {
        var entry = LibraryEntry.Parse("surge-xt", SurgeXt);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new Library(Layout(), new UnusedRunner()).Stop(entry));

        Assert.Contains("not an application Cabinet can open", thrown.Message);
    }
}
