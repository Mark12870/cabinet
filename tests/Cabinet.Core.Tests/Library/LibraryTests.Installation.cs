using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Theory]
    [InlineData("windows", "https://x.invalid/a/setup-2.1.exe", "setup-2.1.exe")]
    [InlineData("windows", "https://x.invalid/library-manager/download/win/", "win.exe")]
    [InlineData("windows", "https://x.invalid/download/win", "win.exe")]
    [InlineData("native", "https://x.invalid/a/thing_linux.tar.xz", "thing_linux.tar.xz")]
    [InlineData(
        "native", "https://www.modartt.com/try?file=thing_trial_v1.tar.xz", "thing_trial_v1.tar.xz")]
    public void ADownloadWithNoFilenameIsNamedAfterTheLastThingInItsUrl(
        string kind, string url, string expected)
    {
        var entry = LibraryEntry.Parse(
            "thing", $"Name: Thing\nKind: {kind}\nSource: rolling\nUrl: {url}\n");

        Assert.Equal(expected, Library.ArchiveName(entry));
    }

    [Fact]
    public void AnInstallScriptIsFollowedByEndingTheWineItStarted()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Script: fixture.sh
            """));
        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recorder = new RecordingRunner();
        var installer = Path.Combine(root, "Thing.exe");
        File.WriteAllText(installer, "");

        var library = new Library(layout, recorder);
        library.Install(library.Find("thing"), installer: installer);

        Assert.Single(
            recorder.Calls,
            call => call.File == "wineserver" && call.Arguments.SequenceEqual(["-k"]));
    }

    [Fact]
    public void ADawsPluginsKeepACatalogueInstallOutOfTheirPrefix()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            """));

        var layout = Layout();
        var recorder = new RecordingRunner();
        var installer = Path.Combine(root, "Thing.exe");
        File.WriteAllText(installer, "");
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(layout, "thing").Busy);

        var library = new Library(layout, recorder);
        var refused = Assert.Throws<PrefixInUseException>(
            () => library.Install(library.Find("thing"), installer: installer));

        Assert.Contains("install Thing into thing", refused.Message);
        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void APluginYouHadToBuyNeedsItsInstaller()
    {
        Catalogue(("gadget", "Name: Gadget\nKind: windows\nSource: byo\n"));

        var refused = Assert.Throws<InvalidOperationException>(
            () => Subject().Install(Subject().Find("gadget")));

        Assert.Equal(
            "Gadget cannot be downloaded, so it needs the file you have", refused.Message);
    }

    [Fact]
    public void ALinuxPluginBehindALoginSaysWhereToGoAndGetIt()
    {
        Catalogue(("vital", """
            Name: Vital
            Kind: native
            Source: byo
            Account: https://account.vital.audio
            """));

        var refused = Assert.Throws<InvalidOperationException>(
            () => Subject().Install(Subject().Find("vital")));

        Assert.Equal(
            "Vital cannot be downloaded, so it needs the file you have from "
            + "https://account.vital.audio",
            refused.Message);
    }

    [Fact]
    public void AFileThatIsNotThereIsSaidSoRatherThanFailingInTheUnpack()
    {
        Catalogue(("vital", "Name: Vital\nKind: native\nSource: byo\n"));

        Assert.Throws<FileNotFoundException>(() => Subject()
            .Install(Subject().Find("vital"), installer: Path.Combine(root, "gone.zip")));
    }

    [Fact]
    public void ALinuxPluginInstallsFromTheFileYouSupplyAndLeavesItWhereItWas()
    {
        var staging = Path.Combine(root, "supplied");
        Directory.CreateDirectory(Path.Combine(staging, "Vital.vst3"));

        var archive = Path.Combine(root, "VitalInstaller.tar.gz");
        var real = new ProcessRunner();
        Assert.True(real.Run(
            "tar", ["-czf", archive, "-C", staging, "."],
            cancellationToken: TestContext.Current.CancellationToken).Ok);

        Catalogue(("vital", """
            Name: Vital
            Kind: native
            Source: byo
            Account: https://account.vital.audio
            """));

        var layout = Layout();
        var library = new Library(layout, real);
        var said = new List<string>();

        library.Install(library.Find("vital"), installer: archive, onOutput: said.Add);

        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".vst3"), "Vital.vst3")));
        Assert.True(File.Exists(archive));
        Assert.DoesNotContain(said, line => line.Contains("Checking sha256"));
        Assert.Contains("Unpacking VitalInstaller.tar.gz", File.ReadAllText(layout.InstallLogPath("vital")));
    }

    [Fact]
    public void ALinuxPluginWithADemoDownloadsAndChecksItWhenNoFileIsSupplied()
    {
        var staging = Path.Combine(root, "supplied");
        Directory.CreateDirectory(Path.Combine(staging, "Thing.vst3"));

        var demo = Path.Combine(root, "thing_demo.tar.gz");
        var real = new ProcessRunner();
        Assert.True(real.Run(
            "tar", ["-czf", demo, "-C", staging, "."],
            cancellationToken: TestContext.Current.CancellationToken).Ok);

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Source: byo
            DemoUrl: {new Uri(demo).AbsoluteUri}
            DemoSha256: {Checksum.Sha256(demo)}
            """));

        var layout = Layout();
        var library = new Library(layout, real);
        var said = new List<string>();

        library.Install(library.Find("thing"), onOutput: said.Add);

        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")));
        Assert.Contains(said, line => line.StartsWith("Checking sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void ARelinkRewritesEveryElfThePayloadHoldsAndStepsOverWhatIsNotOne()
    {
        var staging = Path.Combine(root, "supplied");
        var bundle = Path.Combine(staging, "Vital.vst3", "Contents", "x86_64-linux");
        Directory.CreateDirectory(bundle);
        File.WriteAllBytes(Path.Combine(staging, "Vital.so"), SharedObject.Bytes());
        File.WriteAllBytes(Path.Combine(bundle, "Vital.so"), SharedObject.Bytes());
        File.WriteAllText(Path.Combine(staging, "presets.ttl"), "not an elf");
        File.CreateSymbolicLink(Path.Combine(staging, "wavetables"), "/nowhere/wavetables");

        var archive = Path.Combine(root, "VitalInstaller.tar.gz");
        var real = new ProcessRunner();
        Assert.True(real.Run(
            "tar", ["-czf", archive, "-C", staging, "."],
            cancellationToken: TestContext.Current.CancellationToken).Ok);

        Catalogue(("vital", """
            Name: Vital
            Kind: native
            Source: byo
            Relink: libcurl-gnutls.so.4 = libcurl.so.4
            """));

        var layout = Layout();
        var library = new Library(layout, real);
        var said = new List<string>();

        library.Install(library.Find("vital"), installer: archive, onOutput: said.Add);

        var loose = File.ReadAllBytes(Path.Combine(layout.NativePath("vital"), "Vital.so"));
        var inside = File.ReadAllBytes(Path.Combine(
            layout.NativePath("vital"), "Vital.vst3", "Contents", "x86_64-linux", "Vital.so"));

        Assert.Equal("libcurl.so.4", SharedObject.Soname(loose, SharedObject.First));
        Assert.Equal("libcurl.so.4", SharedObject.Soname(inside, SharedObject.First));
        Assert.Equal("not an elf", File.ReadAllText(
            Path.Combine(layout.NativePath("vital"), "presets.ttl")));
        Assert.Equal(
            [
                "Vital.so: libcurl-gnutls.so.4 \u2192 libcurl.so.4",
                "Vital.vst3/Contents/x86_64-linux/Vital.so: "
                + "libcurl-gnutls.so.4 \u2192 libcurl.so.4",
            ],
            said.Where(line => line.Contains("libcurl")).Select(line => line.Trim()));
    }

    [Fact]
    public void ALinuxPluginIsRefusedAPrefixBecauseItLoadsWithoutOne()
    {
        Catalogue(("dexed", $"""
            Name: Dexed
            Kind: native
            Url: https://example.invalid/dexed.zip
            Sha256: {Zeros}
            """));

        var refused = Assert.Throws<ArgumentException>(
            () => Subject().Install(Subject().Find("dexed"), "somewhere"));

        Assert.Contains("needs no prefix", refused.Message);
    }

    [Fact]
    public void AWindowsPluginIsBootedInstalledAndOnlyThenBridged()
    {
        Catalogue(("dexed", "Name: Dexed\nKind: windows\nSource: byo\n"));

        var installer = Path.Combine(root, "Dexed.exe");
        File.WriteAllText(installer, "");

        var recording = new RecordingRunner();
        var library = new Library(Layout(), recording);

        library.Install(library.Find("dexed"), null, installer);

        Assert.Equal(
            ["wineboot", "wine", "wine"],
            recording.Ran
                .TakeWhile(call => Path.GetFileName(call.File) != "yabridgectl")
                .Select(call => Path.GetFileName(call.File)));
        Assert.Equal(
            ["reg", "add", @"HKCU\Software\Wine\WineDbg", "/v", "ShowCrashDialog", "/t", "REG_DWORD", "/d", "0", "/f"],
            recording.Ran[1].Arguments);
        Assert.Equal([installer], recording.Ran[2].Arguments);
        Assert.Contains(recording.Ran, Synced);
        Assert.Equal("dexed", library.Installed()["dexed"]);
    }

    [Fact]
    public void AByoDemoAndItsOwnInstallerUseTheSamePrefixSettingsAndScript()
    {
        var demo = Path.Combine(root, "thing-demo.exe");
        File.WriteAllText(demo, "demo");
        var own = Path.Combine(root, "thing-own.exe");
        File.WriteAllText(own, "own");

        Catalogue(("thing", $"""
            Name: Thing
            Kind: windows
            Source: byo
            DemoUrl: {new Uri(demo).AbsoluteUri}
            DemoSha256: {Checksum.Sha256(demo)}
            Env: TEST_SETTING=shared
            Sync: fsync
            Script: fixture.sh
            """));
        Script("fixture.sh", "exit 0");

        var recording = new RecordingRunner(args =>
        {
            if (args is ["-fL", "--progress-bar", "--retry", "6", "-o", var target, _])
            {
                File.Copy(demo, target, overwrite: true);
            }
        });
        var layout = Layout();
        var library = new Library(layout, recording);
        var entry = library.Find("thing");

        library.Install(entry, "demo");
        library.Install(entry, installer: own);

        var scripts = recording.Calls.Where(call => call.File == "sh").ToList();

        Assert.Equal(2, scripts.Count);
        Assert.Equal("TEST_SETTING=shared", File.ReadAllText(layout.PrefixEnvFile("demo")).Trim());
        Assert.Equal(SyncMode.Fsync, new PrefixSettings(layout).Sync("demo"));
        Assert.False(Directory.Exists(layout.PrefixPath("thing")));
        Assert.Equal(own, scripts[1].Environment["CABINET_ARCHIVE"]);
        Assert.Equal(scripts[0].Environment["WINE"], scripts[1].Environment["WINE"]);
        Assert.Equal("shared", scripts[0].Environment["TEST_SETTING"]);
        Assert.Equal("shared", scripts[1].Environment["TEST_SETTING"]);
        Assert.Single(recording.Calls, call => call.File == "curl");
    }

    [Fact]
    public void InstallingAgainGoesToThePrefixThePluginIsRecordedIn()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("elsewhere"), "dosdevices"));
        File.WriteAllText(layout.PrefixPluginsFile("elsewhere"), "thing\n");
        var library = new Library(layout, new RecordingRunner());

        library.Install(library.Find("thing"), installer: installer);

        Assert.False(Directory.Exists(layout.PrefixPath("thing")));
        Assert.Equal("elsewhere", library.Installed()["thing"]);
    }

    [Fact]
    public void APluginInstalledInOnePrefixIsNotInstalledIntoAnother()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("first"));
        File.WriteAllText(layout.PrefixPluginsFile("first"), "thing\n");
        var recording = new RecordingRunner();
        var library = new Library(layout, recording);

        var refused = Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing"), "second", installer));

        Assert.Equal(
            "Thing is installed in first already — install it again there, or remove it first",
            refused.Message);
        Assert.False(Directory.Exists(layout.PrefixPath("second")));
        Assert.Empty(recording.Ran);
    }

    [Fact]
    public void APluginIsNotInstalledTwiceAtTheSameTime()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.InstallLogPath("thing"))!);
        File.WriteAllText(layout.InstallLogPath("thing"), "first install\n");
        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        using var first = Underway.Begin(layout.InstallLockPath("thing"));

        var refused = Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing"), "second", installer));

        Assert.Equal("Cabinet is installing Thing right now — wait for that to finish", refused.Message);
        Assert.Equal("first install\n", File.ReadAllText(layout.InstallLogPath("thing")));
        Assert.False(Directory.Exists(layout.PrefixPath("second")));
        Assert.Empty(recording.Ran);
    }

    [Fact]
    public void AMissingOwnInstallerIsRefusedBeforeThePrefixIsCreated()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));
        var missing = Path.Combine(root, "missing.exe");
        var recording = new RecordingRunner();
        var library = new Library(Layout(), recording);

        var thrown = Assert.Throws<FileNotFoundException>(() =>
            library.Install(library.Find("thing"), installer: missing));

        Assert.Equal(missing, thrown.FileName);
        Assert.DoesNotContain(recording.Calls, call => call.File == "wineboot");
        Assert.False(Directory.Exists(Layout().PrefixPath("thing")));
    }

    [Fact]
    public void AnExistingPrefixKeepsItsRunnerAndFetchesNothing()
    {
        Catalogue(("dexed", "Name: Dexed\nKind: windows\nSource: byo\nRunner: 9.21\n"));

        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("dexed"), "dosdevices"));

        var installer = Path.Combine(root, "Dexed.exe");
        File.WriteAllText(installer, "");

        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        var said = new List<string>();

        library.Install(library.Find("dexed"), null, installer, said.Add);

        Assert.Contains(recording.Calls, Synced);
        Assert.DoesNotContain(recording.Calls, call => call.File == "curl");
        Assert.Contains(said, line => line.Contains("keeps bundled"));
    }

    [Fact]
    public void AnInstalledD2D1RunnerIsReusedForItsVersionSpec()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\nRunner: d2d1-11.0\n"));

        var layout = Layout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.RunnerWine("wine-d2d1-11.0"))!);
        File.WriteAllText(layout.RunnerWine("wine-d2d1-11.0"), "");

        var installer = Path.Combine(root, "thing-setup.exe");
        File.WriteAllText(installer, "");

        var recording = new RecordingRunner();
        new Library(layout, recording).Install(
            new Library(layout, recording).Find("thing"), installer: installer);

        Assert.DoesNotContain(recording.Calls, call => call.File == "curl");
        Assert.Contains(recording.Calls, call => Path.GetFileName(call.File) == "wineboot");
    }

    [Fact]
    public void EveryBundleIsLinkedByFormatAndNoBundleIsWalkedInto()
    {
        var staging = Path.Combine(root, "fixture");
        Directory.CreateDirectory(Path.Combine(staging, "Thing.lv2"));
        File.WriteAllText(Path.Combine(staging, "Thing.lv2", "libThing.so"), "");
        File.WriteAllText(Path.Combine(staging, "Thing.lv2", "manifest.ttl"), "");
        Directory.CreateDirectory(Path.Combine(staging, "Thing.vst3"));
        File.WriteAllText(Path.Combine(staging, "Thing.clap"), "");
        File.WriteAllText(Path.Combine(staging, "README"), "");

        var archive = Path.Combine(root, "thing.tar.gz");
        var real = new ProcessRunner();
        Assert.True(real.Run(
            "tar", ["-czf", archive, "-C", staging, "."],
            cancellationToken: TestContext.Current.CancellationToken).Ok);

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            """));

        var layout = Layout();
        var library = new Library(layout, real);
        library.Install(library.Find("thing"));

        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".lv2"), "Thing.lv2")));
        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")));
        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".clap"), "Thing.clap")));
        Assert.False(Directory.Exists(layout.NativeScanDir(".so")));

        library.Remove(library.RemovalOf(library.Find("thing")));

        Assert.False(Path.Exists(Path.Combine(layout.NativeScanDir(".lv2"), "Thing.lv2")));
        Assert.Empty(library.Installed());
    }

    [Fact]
    public void ARollingDownloadIsInstalledUncheckedAndSaidToBe()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Source: rolling
            Url: file://{archive}
            Script: fixture.sh
            """));

        Script("fixture.sh", "mkdir -p \"$CABINET_DEST/Thing.vst3\"");

        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());
        var said = new List<string>();

        library.Install(library.Find("thing"), onOutput: said.Add);

        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")));
        Assert.Contains(said, line => line.Contains("nothing here can verify what arrives"));
        Assert.DoesNotContain(said, line => line.Contains("Checking sha256"));
    }

    [Fact]
    public void ADownloadThatIsNotTheOneTheEntryPinnedStopsTheInstall()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Zeros}
            """));

        var library = new Library(Layout(), new ProcessRunner());

        Assert.Contains(
            "failed its checksum",
            Assert.Throws<InvalidOperationException>(() => library.Install(library.Find("thing")))
                .Message);
    }

    [Fact]
    public void AnInstallThatFailedIsFinishedWithTheSettingsItsEntryDeclares()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\nSync: fsync\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        var fails = true;
        var library = new Library(layout, new RecordingRunner(
            exits: args => fails && args.SequenceEqual([installer]) ? 1 : 0));

        Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing"), installer: installer));

        Assert.Empty(library.Installed());
        Assert.Equal([("thing", "thing")], library.Unfinished());

        fails = false;
        library.Install(library.Find("thing"), installer: installer);

        Assert.Equal(SyncMode.Fsync, new PrefixSettings(layout).Sync("thing"));
        Assert.Equal("thing", library.Installed()["thing"]);
        Assert.Empty(library.Unfinished());
    }

    [Fact]
    public void APluginWhoseBridgeFailedIsRecordedButLeftUnfinishedUntilARetryBridgesIt()
    {
        Catalogue(("gadget", "Name: Gadget\nKind: windows\nSource: byo\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        var key = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Gadget2";
        var bridges = false;
        var library = new Library(layout, new RecordingRunner(
            args =>
            {
                if (args.SequenceEqual([installer]))
                {
                    File.WriteAllText(layout.PrefixSystemReg("gadget"), """
                        WINE REGISTRY Version 2

                        [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Gadget2] 1787344290
                        "DisplayName"="Gadget 2"
                        "UninstallString"="C:\\Uninstall_Gadget2.exe"
                        """);
                }
            },
            args => !bridges && args.SequenceEqual(["sync", "--prune", "--no-verify"]) ? 1 : 0));

        Assert.Contains(
            "yabridgectl exited with 1",
            Assert.Throws<InvalidOperationException>(
                () => library.Install(library.Find("gadget"), installer: installer)).Message);
        Assert.Equal("gadget", library.Installed()["gadget"]);
        Assert.Equal([("gadget", "gadget")], library.Unfinished());

        bridges = true;
        library.Install(library.Find("gadget"), installer: installer);

        Assert.Equal($"gadget\t{key}\n", File.ReadAllText(layout.PrefixPluginsFile("gadget")));
        Assert.Empty(library.Unfinished());
    }

    [Fact]
    public void FinishingAnInstallElsewhereForgetsWhereItFirstStopped()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        var failing = new Library(layout, new RecordingRunner(
            exits: args => args.SequenceEqual([installer]) ? 1 : 0));

        Assert.Throws<InvalidOperationException>(
            () => failing.Install(failing.Find("thing"), "first", installer));

        var library = new Library(layout, new RecordingRunner());
        library.Install(library.Find("thing"), "second", installer);

        Assert.Equal("second", library.Installed()["thing"]);
        Assert.Empty(library.Unfinished());
    }

    [Fact]
    public void InstallingAgainWithoutAPrefixFinishesWhereTheFirstTryStopped()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var layout = Layout();
        var failing = new Library(layout, new RecordingRunner(
            exits: args => args.SequenceEqual([installer]) ? 1 : 0));

        Assert.Throws<InvalidOperationException>(
            () => failing.Install(failing.Find("thing"), "chosen", installer));

        var library = new Library(layout, new RecordingRunner());
        library.Install(library.Find("thing"), installer: installer);

        Assert.Equal("chosen", library.Installed()["thing"]);
        Assert.False(Directory.Exists(layout.PrefixPath("thing")));
    }

    [Fact]
    public void ANativeInstallNeverReplacesALinkSomethingElseMade()
    {
        var archive = Bundles("Thing.vst3");
        Catalogue(("thing", "Name: Thing\nKind: native\nSource: byo\n"));
        var layout = Layout();
        var theirs = Path.Combine(root, "elsewhere", "Thing.vst3");
        Directory.CreateDirectory(theirs);
        var link = Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3");
        Directory.CreateDirectory(layout.NativeScanDir(".vst3"));
        File.CreateSymbolicLink(link, theirs);
        var library = new Library(layout, new ProcessRunner());

        var refused = Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing"), installer: archive));

        Assert.Contains("is not one of Cabinet's plugins", refused.Message);
        Assert.Equal(theirs, new FileInfo(link).LinkTarget);
        Assert.False(Directory.Exists(layout.NativePath("thing")));
        Assert.False(Underway.Marked(layout.NativeInstalling("thing")));
    }

    [Fact]
    public void ANativeInstallNeverTakesTheLinkOfAnotherInstalledPlugin()
    {
        var archive = Bundles("Thing.vst3");
        Catalogue(("thing", "Name: Thing\nKind: native\nSource: byo\n"));
        var layout = Layout();
        var other = Path.Combine(layout.NativePath("other"), "Thing.vst3");
        Directory.CreateDirectory(other);
        var link = Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3");
        Directory.CreateDirectory(layout.NativeScanDir(".vst3"));
        File.CreateSymbolicLink(link, other);
        var library = new Library(layout, new ProcessRunner());

        Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing"), installer: archive));

        Assert.Equal(other, new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void ANativeInstallPutsItsPluginsInTheScanPathsAndKeepsTheRestInCabinet()
    {
        var archive = NestedArchive();
        Catalogue(("sampler", "Name: Sampler\nKind: native\nSource: byo\n"));
        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());

        library.Install(library.Find("sampler"), installer: archive);

        var folder = Path.Combine(layout.NativePath("sampler"), "Sampler-Linux");
        var vst3 = Path.Combine(layout.NativeScanDir(".vst3"), "Sampler.vst3");
        var vst2 = Path.Combine(layout.NativeScanDir(".so"), "Sampler.so");
        Assert.True(File.Exists(Path.Combine(vst3, "Contents", "x86_64-linux", "Sampler.so")));
        Assert.Null(new DirectoryInfo(vst3).LinkTarget);
        Assert.Equal("vst2", File.ReadAllText(vst2));
        Assert.Null(new FileInfo(vst2).LinkTarget);
        Assert.Equal(vst3, new DirectoryInfo(Path.Combine(folder, "Sampler.vst3")).LinkTarget);
        Assert.Equal(vst2, new FileInfo(Path.Combine(folder, "Sampler.so")).LinkTarget);
        Assert.Equal("standalone", File.ReadAllText(Path.Combine(folder, "Sampler")));
    }

    [Fact]
    public void AClapThatLinksIntoItsVst3BundleStillReachesItOnceBothArePublished()
    {
        var payload = Path.Combine(root, "uhe");
        var module = Path.Combine(payload, "Synth.vst3", "Contents", "x86_64-linux");
        Directory.CreateDirectory(module);
        File.WriteAllText(Path.Combine(module, "Synth.so"), "module");
        File.CreateSymbolicLink(
            Path.Combine(payload, "Synth.clap"), Path.Combine("Synth.vst3", "Contents", "x86_64-linux", "Synth.so"));
        var archive = Archive(payload);
        Catalogue(("synth", "Name: Synth\nKind: native\nSource: byo\n"));
        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());

        library.Install(library.Find("synth"), installer: archive);

        var clap = Path.Combine(layout.NativeScanDir(".clap"), "Synth.clap");
        Assert.Equal("module", File.ReadAllText(clap));
        Assert.Equal(
            Path.Combine(layout.NativeScanDir(".vst3"), "Synth.vst3", "Contents", "x86_64-linux", "Synth.so"),
            new FileInfo(clap).LinkTarget);
    }

    [Fact]
    public void AFailedNativeInstallTakesBackTheLinksItHadMade()
    {
        var archive = Bundles("A.vst3", "B.vst3");
        Catalogue(("thing", "Name: Thing\nKind: native\nSource: byo\n"));
        var layout = Layout();
        var scan = layout.NativeScanDir(".vst3");
        Directory.CreateDirectory(Path.Combine(scan, "B.vst3"));
        var library = new Library(layout, new ProcessRunner());

        Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing"), installer: archive));

        Assert.Equal([Path.Combine(scan, "B.vst3")], Directory.EnumerateFileSystemEntries(scan));
        Assert.Empty(library.Installed());
    }

    [Fact]
    public void AnInterruptedNativeInstallIsNotInstalledAndInstallingAgainFinishesIt()
    {
        var archive = Bundles("Thing.vst3");
        Catalogue(("thing", "Name: Thing\nKind: native\nSource: byo\nData: .thing/Thing\n"));
        var layout = Layout();
        var data = layout.DataPath(".thing/Thing");
        Directory.CreateDirectory(Path.Combine(layout.NativePath("thing"), "Stale.vst3"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "stale.preset"), "");
        Directory.CreateDirectory(layout.NativeScanDir(".vst3"));
        File.CreateSymbolicLink(
            Path.Combine(layout.NativeScanDir(".vst3"), "Stale.vst3"),
            Path.Combine(layout.NativePath("thing"), "Stale.vst3"));
        File.WriteAllText(layout.NativeInstalling("thing"), $"thing\n{data}");
        var library = new Library(layout, new ProcessRunner());

        Assert.Empty(library.Installed());
        Assert.Equal([("thing", (string?)null)], library.Unfinished());

        library.Install(library.Find("thing"), installer: archive);

        Assert.Null(library.Installed()["thing"]);
        Assert.Empty(library.Unfinished());
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
        Assert.Equal(
            [Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")],
            Directory.EnumerateFileSystemEntries(layout.NativeScanDir(".vst3")));
    }

    [Fact]
    public void AScriptReplacesTheUnpackAndFillsTheDirectoriesCabinetMade()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: fixture.sh
            Data: .thing/Thing
            """));

        Script("fixture.sh", """
            test "$(pwd)" = "$CABINET_DEST"
            tar -xf "$CABINET_ARCHIVE" -C "$CABINET_WORK"
            mkdir -p "$CABINET_DEST/Thing.vst3"
            cp "$CABINET_WORK/nested/deep/Thing.so" "$CABINET_DEST/Thing.vst3/Thing.so"
            cp "$CABINET_WORK/nested/deep/presets.txt" "$CABINET_DATA/presets.txt"
            """);

        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());
        library.Install(library.Find("thing"));

        var data = Path.Combine(root, ".thing", "Thing");

        Assert.True(Path.Exists(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")));
        Assert.True(File.Exists(Path.Combine(data, "presets.txt")));
        Assert.Equal("thing", Assert.Single(library.Installed()).Key);

        library.Remove(library.RemovalOf(library.Find("thing")));

        Assert.False(Path.Exists(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")));
        Assert.False(Directory.Exists(data));
        Assert.False(Directory.Exists(layout.NativePath("thing")));
    }

    [Fact]
    public void AScriptThatFailsLeavesNeitherDirectoryBehind()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: fixture.sh
            Data: .thing/Thing
            """));

        Script("fixture.sh", "exit 3");

        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());

        var thrown = Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing")));

        Assert.Contains("fixture.sh exited with 3", thrown.Message);
        Assert.False(Directory.Exists(layout.NativePath("thing")));
        Assert.False(Directory.Exists(Path.Combine(root, ".thing", "Thing")));
        Assert.Empty(library.Installed());
    }

    [Fact]
    public void AScriptThisBuildDidNotShipSaysSoRatherThanFailingInsideSh()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: absent.sh
            """));

        var library = new Library(Layout(), new ProcessRunner());

        Assert.Contains(
            "absent.sh",
            Assert.Throws<FileNotFoundException>(() => library.Install(library.Find("thing")))
                .Message);
    }

    [Fact]
    public void ADataDirectorySomethingElseOwnsIsLeftAlone()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: fixture.sh
            Data: .thing/Thing
            """));

        Script("fixture.sh", "exit 0");
        var data = Directory.CreateDirectory(Path.Combine(root, ".thing", "Thing")).FullName;
        File.WriteAllText(Path.Combine(data, "mine.txt"), "");

        var library = new Library(Layout(), new UnusedRunner());

        Assert.Throws<InvalidOperationException>(() => library.Install(library.Find("thing")));
        Assert.True(File.Exists(Path.Combine(data, "mine.txt")));
    }

    [Fact]
    public void AWindowsScriptIsHandedThePrefixAndItsOwnWine()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Prefix: thing
            Script: fixture.sh
            """));

        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recording = new RecordingRunner();
        var installer = Path.Combine(root, "thing-setup.exe");
        File.WriteAllText(installer, "");

        var library = new Library(layout, recording);

        library.Install(library.Find("thing"), installer: installer);

        var script = Assert.Single(recording.Calls, call => call.File == "sh");

        Assert.Contains(recording.Calls, Synced);
        Assert.Equal(["-e", layout.LibraryScript(Vendor, "fixture.sh")], script.Arguments);
        Assert.Equal(layout.PrefixPath("thing"), script.WorkingDirectory);
        Assert.Equal(layout.PrefixPath("thing"), script.Environment["WINEPREFIX"]);
        Assert.Equal(installer, script.Environment["CABINET_ARCHIVE"]);
        Assert.Contains("WINE", script.Environment.Keys);
        Assert.DoesNotContain(recording.Calls, call => call.Arguments.Contains(installer)
            && call.File != "sh");
    }

    [Fact]
    public void AnEntrysWinetricksDependenciesRunBeforeItsInstaller()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Prefix: thing
            Script: fixture.sh
            Winetricks: corefonts
            """));

        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var installer = Path.Combine(root, "thing-setup.exe");
        File.WriteAllText(installer, "");
        var recording = new RecordingRunner(
            _ => Directory.CreateDirectory(Path.Combine(layout.PrefixPath("thing"), "dosdevices")));

        new Library(layout, recording).Install(
            new Library(layout, recording).Find("thing"), installer: installer);

        var winetricks = Assert.Single(
            recording.Calls, call => call.File == Cabinet.Core.Layout.Winetricks);
        var script = Assert.Single(recording.Calls, call => call.File == "sh");

        Assert.Equal(["--unattended", "corefonts"], winetricks.Arguments);
        Assert.True(recording.Calls.ToList().IndexOf(winetricks)
                    < recording.Calls.ToList().IndexOf(script));
    }

    [Fact]
    public void AnEntrysEnvironmentIsSetBeforeItsInstallerEverRuns()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Prefix: thing
            Script: fixture.sh
            Env: WINEDLLOVERRIDES=wbemprox=n
            """));

        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recording = new RecordingRunner();
        var installer = Path.Combine(root, "thing-setup.exe");
        File.WriteAllText(installer, "");

        var library = new Library(layout, recording);

        library.Install(library.Find("thing"), installer: installer);

        var script = Assert.Single(recording.Calls, call => call.File == "sh");

        Assert.Equal("wbemprox=n", script.Environment["WINEDLLOVERRIDES"]);
        Assert.Equal(
            "WINEDLLOVERRIDES=wbemprox=n",
            File.ReadAllText(layout.PrefixEnvFile("thing")).Trim());
    }

    [Fact]
    public void AnExistingPrefixGetsAnEntrysMissingEnvironment()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Env:
              TEST_SETTING=shared
              OTHER_SETTING=added
            """));

        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("thing"), "dosdevices"));
        File.WriteAllText(layout.PrefixEnvFile("thing"), "TEST_SETTING=custom\n");
        var installer = Path.Combine(root, "thing-setup.exe");
        File.WriteAllText(installer, "");

        var library = new Library(layout, new RecordingRunner());

        library.Install(library.Find("thing"), installer: installer);

        Assert.Equal("custom", new PrefixSettings(layout).Variables("thing")["TEST_SETTING"]);
        Assert.Equal("added", new PrefixSettings(layout).Variables("thing")["OTHER_SETTING"]);
    }
}
