using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Fact]
    public void OpeningAnAppPassesItsArgumentsAfterThePath()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchArgs:\n  --disable-gpu\n  --disable-gpu-compositing\n");
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual(
                [Prefixes.JoinMode, entry.Launch!, "--disable-gpu", "--disable-gpu-compositing"]));
    }

    [Fact]
    public void ClosingAnAppEndsTheHelperItLeftRunning()
    {
        var entry = LibraryEntry.Parse("thing", Linked + "LaunchHelper: ThingHelper.exe\n");
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        var calls = recorder.Calls.ToList();
        var app = calls.FindIndex(call => call.Arguments.Contains(entry.Launch));
        var helper = calls.FindIndex(call => call.Arguments.SequenceEqual(
            [Prefixes.JoinMode, "taskkill", "/f", "/im", "ThingHelper.exe"]));

        Assert.True(app < helper);
    }

    [Fact]
    public void ClosingAnAppLeavesTheHelperToACopyOfItStillOpen()
    {
        var entry = LibraryEntry.Parse("thing", Linked + "LaunchHelper: ThingHelper.exe\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            outputs: _ => "\"Thing.exe\",\"42\",\"Console\",\"1\",\"90,112 K\"");
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains("ThingHelper.exe"));
    }

    [Fact]
    public void OpeningAnAppSendsItsOutputToAFileSoElectronCanOpenItsOwnStdout()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");

        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        var opened = Assert.Single(recorder.Calls, call => call.Arguments.Contains(entry.Launch));

        Assert.Equal(layout.PrefixLaunchLog(entry.Prefix), opened.LogTo);
    }

    [Fact]
    public void OpeningAnAppOnAVirtualDesktopSaysItWillBeConfinedToIt()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");
        File.WriteAllText(
            layout.PrefixUserReg(entry.Prefix),
            "[Software\\\\Wine\\\\Explorer]\n\"Desktop\"=\"Default\"\n"
            + "[Software\\\\Wine\\\\Explorer\\\\Desktops]\n\"Default\"=\"1280x720\"\n");

        new Library(layout, recorder).Launch(entry);

        Assert.Contains(
            "confined to it", File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
        Assert.DoesNotContain(
            recorder.Calls,
            call => call.Arguments.Contains("reg") && !call.Arguments.Contains("query"));
    }

    [Fact]
    public void OpeningAnAppOnAPlainPrefixSaysNothingAboutADesktop()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.DoesNotContain(
            "confined to it", File.ReadAllText(layout.PrefixLaunchLog(entry.Prefix)));
        Assert.DoesNotContain(
            recorder.Calls,
            call => call.Arguments.Contains("reg") && !call.Arguments.Contains("query"));
    }

    [Fact]
    public void OpeningAnAppLeavesWineToTheSessionAlreadyBridgingThePrefix()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            dawSession: true);
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.Single(
            recorder.Calls,
            call => call.File == layout.ShimPath
                    && call.Arguments.SequenceEqual([Prefixes.JoinMode, entry.Launch!]));
        Assert.DoesNotContain(recorder.Calls, call => call.File == "wineserver");
    }

    [Fact]
    public void AnAppTheSessionEndedIsNotReportedAsWineFailingToFinish()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            exits: args => args.SequenceEqual([Prefixes.JoinMode, @"C:\Program Files\Thing\Thing.exe"])
                ? 137
                : 0,
            dawSession: true);
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new Library(layout, recorder).Launch(entry));

        Assert.DoesNotContain("did not finish", thrown.Message);
        Assert.Contains("137", thrown.Message);
    }

    [Fact]
    public void OpeningAnAppStartsItsServiceBeforeTheApp()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchService: ThingService\n");
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        var service = Assert.Single(
            recorder.Calls,
            call => call.Arguments.SequenceEqual([Prefixes.JoinMode, "sc", "start", "ThingService"]));
        var app = Assert.Single(recorder.Calls, call => call.Arguments.Contains(entry.Launch));

        var calls = recorder.Calls.ToList();
        Assert.True(calls.IndexOf(service) < calls.IndexOf(app));
    }

    [Fact]
    public void OpeningAnAppAllowsItsAlreadyRunningService()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchService: ThingService\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            exits: args => args.SequenceEqual([Prefixes.JoinMode, "sc", "start", "ThingService"]) ? 32 : 0);
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.Contains(recorder.Calls, call => call.Arguments.Contains(entry.Launch));
    }

    [Fact]
    public void AServiceThatCannotStartPreventsTheAppOpening()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchService: ThingService\n");
        var layout = Layout();
        var recorder = new RecordingRunner(
            exits: args => args.SequenceEqual([Prefixes.JoinMode, "sc", "start", "ThingService"]) ? 1 : 0);
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new Library(layout, recorder).Launch(entry));

        Assert.Contains("ThingService service could not start", thrown.Message);
        Assert.DoesNotContain(recorder.Calls, call => call.Arguments.Contains(entry.Launch));
    }

    [Fact]
    public async Task OpeningAnAppWaitsForPluginsThatAppearWhileTheAppRuns()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");
        var layout = Layout();
        var pluginDirectory = layout.PrefixVst3Dir(entry.Prefix);
        var plugin = Path.Combine(pluginDirectory, "Detached.vst3");
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var recorder = new RecordingRunner(args =>
        {
            if (!args.SequenceEqual([Prefixes.JoinMode, entry.Launch!]))
            {
                return;
            }

            File.WriteAllText(plugin, "");
            started.Set();
            release.Wait();
            File.Delete(plugin);
        });

        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var task = Task.Run(() => new Library(layout, recorder).Launch(entry));

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(task.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await task;
        Assert.False(File.Exists(plugin));
    }

    [Fact]
    public async Task APluginIsBridgedWhileAnAppIsStillOpen()
    {
        var entry = Manager();
        var layout = Layout();
        var plugin = Path.Combine(layout.PrefixVst3Dir(entry.Prefix), "Landed.vst3");
        var started = new ManualResetEventSlim();
        var bridged = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var recorder = new RecordingRunner(args =>
        {
            if (args.SequenceEqual([Prefixes.JoinMode, entry.Launch]))
            {
                File.WriteAllText(plugin, "");
                started.Set();
                release.Wait();
            }

            if (args.SequenceEqual(["sync", "--prune", "--no-verify"]))
            {
                bridged.Set();
            }
        });

        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath(entry.Prefix), "dosdevices"));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var task = Task.Run(() =>
            new Library(layout, recorder).Launch(entry));

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(bridged.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.Set();
            await task;
        }
        Assert.Contains(recorder.Calls, Synced);
    }

    [Fact]
    public async Task AFailedLiveBridgeIsRetriedWhileAnAppIsStillOpen()
    {
        var entry = Manager();
        var layout = Layout();
        var plugin = Path.Combine(layout.PrefixVst3Dir(entry.Prefix), "Landed.vst3");
        var started = new ManualResetEventSlim();
        var retried = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var attempts = 0;
        var recorder = new RecordingRunner(
            args =>
            {
                if (args.SequenceEqual([Prefixes.JoinMode, entry.Launch]))
                {
                    File.WriteAllText(plugin, "");
                    started.Set();
                    release.Wait();
                }

                if (args.SequenceEqual(["sync", "--prune", "--no-verify"]))
                {
                    if (Interlocked.Increment(ref attempts) >= 2)
                    {
                        retried.Set();
                    }
                }
            },
            args => args.SequenceEqual(["sync", "--prune", "--no-verify"]) && Volatile.Read(ref attempts) == 1 ? 1 : 0);

        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath(entry.Prefix), "dosdevices"));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var library = new Library(layout, recorder);
        var task = Task.Run(() => library.Launch(entry));

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(retried.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.Set();
            await task;
        }
        Assert.True(attempts >= 2);
        Assert.Contains("yabridgectl exited with 1", library.LaunchLog(entry));
    }

    [Fact]
    public void ClosingAnAppBridgesEvenWhenNothingInThePrefixChanged()
    {
        var entry = Manager();
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.Contains(recorder.Calls, Synced);
    }

    [Fact]
    public void AnAppThatFailsStillBridgesWhatItInstalled()
    {
        var entry = Manager();
        var layout = Layout();
        var plugin = Path.Combine(layout.PrefixVst3Dir(entry.Prefix), "Landed.vst3");
        var recorder = new RecordingRunner(
            args =>
            {
                if (args.SequenceEqual([Prefixes.JoinMode, entry.Launch!]))
                {
                    File.WriteAllText(plugin, "");
                }
            },
            args => args.SequenceEqual([Prefixes.JoinMode, entry.Launch!]) ? 1 : 0);

        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var library = new Library(layout, recorder);
        var thrown = Assert.Throws<InvalidOperationException>(() => library.Launch(entry));

        Assert.Contains("Thing exited with 1", thrown.Message);
        Assert.Contains(recorder.Calls, Synced);
        Assert.Contains("Landed.vst3 appeared", library.LaunchLog(entry));
    }

    [Fact]
    public void BridgeAndAppFailuresAreBothReported()
    {
        var entry = Manager();
        var layout = Layout();
        var recorder = new RecordingRunner(
            exits: args => args.SequenceEqual([Prefixes.JoinMode, entry.Launch!])
                           || args.SequenceEqual(["sync", "--prune", "--no-verify"])
                ? 1
                : 0);

        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var thrown = Assert.Throws<AggregateException>(
            () => new Library(layout, recorder).Launch(entry));

        Assert.Contains("Thing exited with 1", thrown.ToString());
        Assert.Contains("yabridgectl exited with 1", thrown.ToString());
    }

    [Fact]
    public void APluginTakenAwayWhileAnAppWasOpenIsBridgedAwayOnClose()
    {
        var entry = Manager();
        var layout = Layout();
        var plugin = Path.Combine(layout.PrefixVst3Dir(entry.Prefix), "Uninstalled.vst3");
        var recorder = new RecordingRunner(args =>
        {
            if (args.SequenceEqual([Prefixes.JoinMode, entry.Launch!]))
            {
                File.Delete(plugin);
            }
        });

        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        File.WriteAllText(plugin, "");
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        var library = new Library(layout, recorder);
        library.Launch(entry);

        Assert.Contains(recorder.Calls, Synced);
        Assert.Contains("removed Uninstalled.vst3", library.LaunchLog(entry));
    }

    [Fact]
    public void OpeningAnAppNarratesIntoTheLogSoOneFileHoldsTheWholeLaunch()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");

        var layout = Layout();
        var library = new Library(layout, new RecordingRunner());
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        Assert.Null(library.LaunchLog(entry));

        library.Launch(entry);

        var written = Assert.IsType<string>(library.LaunchLog(entry));

        Assert.Contains("Opening Thing.", written);
        Assert.Contains("Thing closed.", written);
    }

    [Fact]
    public void PluginLogsIncludeTheLaunchAndYabridgeRuntimeFiles()
    {
        var entry = Manager();
        var layout = Layout();
        Recorded(layout, entry);
        Directory.CreateDirectory(layout.SocketDir);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.InstallLogPath(entry.Id))!);
        File.WriteAllText(layout.InstallLogPath(entry.Id), "Installed Thing.\n");
        File.WriteAllText(layout.PrefixLaunchLog(entry.Prefix), "Opening Thing.\n");
        File.WriteAllText(layout.RuntimeLogPath, "Loaded Thing.vst3.\n");

        var written = Assert.IsType<string>(new Library(layout, new UnusedRunner()).LaunchLog(entry));

        Assert.Contains("Cabinet installation log\nInstalled Thing.", written);
        Assert.Contains("Cabinet launch log\nOpening Thing.", written);
        Assert.Contains("yabridge runtime log (shared)\nLoaded Thing.vst3.", written);
        Assert.True(
            written.IndexOf("Cabinet installation log", StringComparison.Ordinal)
            < written.IndexOf("Cabinet launch log", StringComparison.Ordinal));
        Assert.True(
            written.IndexOf("Cabinet launch log", StringComparison.Ordinal)
            < written.IndexOf("yabridge runtime log (shared)", StringComparison.Ordinal));
    }

    [Fact]
    public void NativePluginLogsCanBeReadWithoutAPrefixLog()
    {
        var entry = Native("thing");
        var layout = Layout();
        Directory.CreateDirectory(layout.SocketDir);
        File.WriteAllText(layout.RuntimeLogPath, "Loaded Thing.vst3.\n");

        var written = Assert.IsType<string>(new Library(layout, new UnusedRunner()).LaunchLog(entry));

        Assert.Contains("yabridge runtime log (shared)\nLoaded Thing.vst3.", written);
    }

    [Fact]
    public void OversizedLogsAreShownAsTheirRecentTailAndLeftWhole()
    {
        var entry = Native("thing");
        var layout = Layout();
        Directory.CreateDirectory(layout.SocketDir);
        var content = new string('x', 4 * 1024 * 1024) + "\nRecent.\n";
        File.WriteAllText(layout.RuntimeLogPath, content);

        var written = Assert.IsType<string>(new Library(layout, new UnusedRunner()).LaunchLog(entry));

        Assert.Contains("Recent.", written);
        Assert.DoesNotContain(new string('x', 100), written);
        Assert.Equal(content.Length, new FileInfo(layout.RuntimeLogPath).Length);
    }

    [Fact]
    public void KeptDownloadsAreLinkedAsideWhileTheAppIsOpen()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Launch: C:\\Thing\\Thing.exe
            Keep: drive_c/downloads
            Recover: fixture.sh
            """));
        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");
        var downloads = Path.Combine(layout.PrefixPath("thing"), "drive_c", "downloads");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "Kontakt_8_Installer.zip"), "payload");

        new Library(layout, recorder).Launch(new Library(layout, recorder).Find("thing"));

        var link = Assert.Single(recorder.Calls, call => call.File == "ln");
        Assert.Equal(
            [
                "-f",
                Path.Combine(downloads, "Kontakt_8_Installer.zip"),
                Path.Combine(layout.PrefixKeptDir("thing"), "Kontakt_8_Installer.zip"),
            ],
            link.Arguments);
    }

    [Fact]
    public void ARecoverScriptWaitsForADawAndSaysWhatBecomesOfTheDownloads()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Launch: C:\\Thing\\Thing.exe
            Keep: drive_c/downloads
            Recover: fixture.sh
            """));
        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(layout, "thing").Busy);

        var library = new Library(layout, recorder);
        library.Launch(library.Find("thing"));

        Assert.DoesNotContain(recorder.Calls, call => call.File == "sh");
        Assert.Contains("A DAW is using plugins from thing", library.LaunchLog(library.Find("thing")));
        Assert.Contains(
            "Cabinet finishes the install the next time you open Thing",
            library.LaunchLog(library.Find("thing")));
    }

    [Fact]
    public void ARecoverScriptRunsWhenTheAppClosesAndBeforeTheBridge()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Launch: C:\\Thing\\Thing.exe
            Keep: drive_c/downloads
            Recover: fixture.sh
            """));
        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");

        new Library(layout, recorder).Launch(new Library(layout, recorder).Find("thing"));

        var script = Assert.Single(recorder.Calls, call => call.File == "sh");
        Assert.Equal(["-e", layout.LibraryScript(Vendor, "fixture.sh")], script.Arguments);
        Assert.Equal(layout.PrefixKeptDir("thing"), script.Environment["CABINET_KEPT"]);

        var calls = recorder.Calls.ToList();
        Assert.True(calls.IndexOf(script) < calls.FindIndex(Synced));
    }

    [Fact]
    public void ClosingAnAppStopsItsServiceAndNeverWaitsForWineInTheSessionItKeepsLive()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Thing\Thing.exe" + "\n"
            + "LaunchService: ThingService\n");
        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");

        new Library(layout, recorder).Launch(entry);

        Assert.Single(recorder.Calls,
            call => call.Arguments.SequenceEqual([Prefixes.JoinMode, "sc", "stop", "ThingService"]));
        Assert.DoesNotContain(recorder.Calls, call => call.File == "wineserver");
    }

    [Fact]
    public void AnAppWhoseCloseCouldNotBeBridgedIsReportedAsLeftOpen()
    {
        var entry = Manager();
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");
        var library = new Library(layout, new RecordingRunner(
            exits: args => args.SequenceEqual(["sync", "--prune", "--no-verify"]) ? 1 : 0));

        Assert.Throws<InvalidOperationException>(() => library.Launch(entry));

        Assert.Equal([(entry.Id, entry.Prefix)], library.LeftOpen());
    }

    [Fact]
    public void AnAppThatClosedAndWasBridgedLeavesNothingOpen()
    {
        var entry = Manager();
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixVst3Dir(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");
        var library = new Library(layout, new RecordingRunner());

        library.Launch(entry);

        Assert.Empty(library.LeftOpen());
        Assert.False(Underway.Marked(layout.PrefixOpen(entry.Prefix, entry.Id)));
    }

    [Fact]
    public void AnAppAnotherCabinetHasOpenIsSeenAsOpen()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("first"));
        File.WriteAllText(layout.PrefixOpen("first", "closed"), "closed");
        using var elsewhere = Underway.Begin(layout.PrefixOpen("first", "manager"));

        Assert.Equal(["manager"], new Library(layout, new RecordingRunner()).Opened());
    }
}
