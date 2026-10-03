using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class EnrolmentTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    [Theory]
    [InlineData("--device=shm")]
    [InlineData("--filesystem=xdg-run/yabridge:create")]
    [InlineData("--talk-name=io.github.mark12870.cabinet.Bridge")]
    [InlineData("--env=YABRIDGE_TEMP_DIR=/run/user/1000/yabridge")]
    [InlineData("--filesystem=~/.local/share/flatpak/app/"
                + "io.github.mark12870.cabinet/current/active/files/lib/yabridge:ro")]
    [InlineData("--filesystem=~/.var/app/io.github.mark12870.cabinet/data/prefixes:ro")]
    public void TheOverrideCarriesEverythingTheBoundaryNeeds(string expected)
    {
        Assert.Contains(expected, Enrolment.OverrideArguments("fm.reaper.Reaper", Layout));
    }

    [Fact]
    public void TheOverrideGrantsNoHostCommands()
    {
        Assert.DoesNotContain(
            "--talk-name=org.freedesktop.Flatpak", Enrolment.OverrideArguments("fm.reaper.Reaper", Layout));
    }

    [Theory]
    [InlineData("--env=WINELOADER=")]
    [InlineData("--env=YABRIDGE_DEBUG_FILE=")]
    [InlineData("--env=YABRIDGE_NO_WATCHDOG=")]
    [InlineData("--filesystem=~/.var/app/io.github.mark12870.cabinet/data/native")]
    [InlineData("--filesystem=~/.var/app/io.github.mark12870.cabinet/data/bridge")]
    [InlineData("--filesystem=~/.local/share/flatpak/app/io.github.mark12870.cabinet/current/active/files:")]
    public void TheOverrideLeavesOutWhatTheDawNeverNeeds(string unneeded)
    {
        Assert.DoesNotContain(
            Enrolment.OverrideArguments("fm.reaper.Reaper", Layout),
            argument => argument.StartsWith(unneeded, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryOverrideFlagIsOfAKindDoctorChecks()
    {
        var unknown = Enrolment.OverrideArguments("fm.reaper.Reaper", Layout)
            .Where(argument => argument.StartsWith("--", StringComparison.Ordinal))
            .Where(argument => argument != "--user")
            .Where(argument => !new[] { "--device=", "--filesystem=", "--talk-name=", "--env=" }
                .Any(kind => argument.StartsWith(kind, StringComparison.Ordinal)));

        Assert.Empty(unknown);
    }

    [Fact]
    public void TheOverrideScopesItselfToTheUser()
    {
        var arguments = Enrolment.OverrideArguments("fm.reaper.Reaper", Layout);

        Assert.Equal("override", arguments[0]);
        Assert.Contains("--user", arguments);
        Assert.Contains("fm.reaper.Reaper", arguments);
    }

    [Fact]
    public void PathsUnderTheHomeAreLeftForFlatpakToExpandAndTheRestAreSpelledOut()
    {
        var command = Enrolment.OverrideCommand("fm.reaper.Reaper", Layout);

        Assert.DoesNotContain("$XDG_RUNTIME_DIR", command);
        Assert.DoesNotContain("/home/u/", command);
        Assert.Contains("--env=YABRIDGE_TEMP_DIR=/run/user/1000/yabridge", command);
        Assert.StartsWith("flatpak override --user fm.reaper.Reaper", command);
    }

    [Fact]
    public void AFreshEnrolmentTakesNothingAway()
    {
        using var home = new TempHome();

        Assert.Empty(Enrolment.Retirements("fm.reaper.Reaper", home.Layout));
    }

    [Fact]
    public void EnrollingAgainTakesBackWhatAnOlderReleaseGranted()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        Directory.CreateDirectory(layout.FlatpakOverridesDir);
        File.WriteAllLines(
            layout.FlatpakOverride("fm.reaper.Reaper"),
            [
                "[Context]",
                $"filesystems={layout.HostAppFiles}:ro;{layout.PrefixesDir}:ro;"
                    + $"{layout.NativeDir}:ro;{layout.BridgeHome}:ro;xdg-run/yabridge:create;~/Music:ro;",
                "",
                "[Session Bus Policy]",
                "org.freedesktop.Flatpak=talk",
                "",
                "[Environment]",
                "WINELOADER=/somewhere/cabinet-wine",
                "YABRIDGE_TEMP_DIR=/run/user/1000/yabridge",
                "YABRIDGE_NO_WATCHDOG=1",
            ]);

        Assert.Equal(
            [
                "--no-talk-name=org.freedesktop.Flatpak",
                "--nofilesystem=~/.local/share/flatpak/app/io.github.mark12870.cabinet/current/active/files",
                "--nofilesystem=~/data/native",
                "--nofilesystem=~/data/bridge",
                "--unset-env=WINELOADER",
                "--unset-env=YABRIDGE_NO_WATCHDOG",
            ],
            Enrolment.Retirements("fm.reaper.Reaper", layout));
    }

    [Fact]
    public void TheCommandTakesBackBeforeItGrantsSoNoRetirementCancelsAGrant()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        Directory.CreateDirectory(layout.FlatpakOverridesDir);
        File.WriteAllLines(
            layout.FlatpakOverride("fm.reaper.Reaper"),
            ["[Context]", $"filesystems={layout.HostAppFiles}/:ro;{layout.PrefixesDir}/:ro;"]);

        var command = Enrolment.OverrideCommand("fm.reaper.Reaper", layout);

        Assert.Equal(
            ["--nofilesystem=~/.local/share/flatpak/app/io.github.mark12870.cabinet/current/active/files"],
            Enrolment.Retirements("fm.reaper.Reaper", layout));
        Assert.True(command.IndexOf("--nofilesystem=", StringComparison.Ordinal)
                    < command.IndexOf("--device=shm", StringComparison.Ordinal));
    }

    [Fact]
    public void EnrollingASecondTimeTakesNothingMoreAway()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        Directory.CreateDirectory(layout.FlatpakOverridesDir);
        File.WriteAllLines(
            layout.FlatpakOverride("fm.reaper.Reaper"),
            [
                "[Context]",
                $"filesystems=!{layout.HostAppFiles};!{layout.NativeDir};{layout.HostYabridgeDir}:ro;~/Own:ro;",
                "",
                "[Session Bus Policy]",
                "org.freedesktop.Flatpak=none",
            ]);

        Assert.Empty(Enrolment.Retirements("fm.reaper.Reaper", layout));
    }

    [Fact]
    public void AReadOnlyGrantDoesNotCoverOneThatMustCreate()
    {
        using var home = new TempHome();

        Assert.False(Enrolment.Covers(home.Layout, "xdg-run/yabridge:ro;", "xdg-run/yabridge:create"));
        Assert.True(Enrolment.Covers(home.Layout, "xdg-run/yabridge:create;", "xdg-run/yabridge:create"));
        Assert.True(Enrolment.Covers(home.Layout, $"{home.Layout.PrefixesDir};", $"{home.Layout.PrefixesDir}:ro"));
    }

    [Fact]
    public void AStartLeavesARealBridgeOutputBesideARealScanPathAlone()
    {
        using var home = new TempHome();
        var windows = home.Layout.WindowsScanDir(".vst3");
        var output = home.Layout.BridgeOutputDir(".vst3");
        Directory.CreateDirectory(windows);
        Directory.CreateDirectory(Path.Combine(output, "Plugin.vst3"));

        Enrolment.MoveBridgeOutputIntoScanDirectories(home.Layout);

        Assert.True(Directory.Exists(Path.Combine(output, "Plugin.vst3")));
        Assert.Null(new DirectoryInfo(output).LinkTarget);
    }

    [Fact]
    public void AnOlderGrantOfTheWholeInstallationStillCoversTheBridge()
    {
        using var home = new TempHome();
        var layout = home.Layout;

        Assert.True(Enrolment.Covers(layout, $"{layout.HostAppFiles}:ro;", $"{layout.HostYabridgeDir}:ro"));
        Assert.False(Enrolment.Covers(layout, $"!{layout.HostAppFiles};", $"{layout.HostYabridgeDir}:ro"));
        Assert.False(Enrolment.Covers(layout, $"{layout.HostAppFiles}-other:ro;", $"{layout.HostYabridgeDir}:ro"));
    }

    [Fact]
    public void TheSelfTestRunsTheShimInsideTheDaw()
    {
        Assert.Equal(
            "flatpak run --command=/home/u/.local/share/flatpak/app/io.github.mark12870.cabinet"
            + "/current/active/files/lib/yabridge/cabinet-wine fm.reaper.Reaper "
            + "--cabinet-self-test",
            Enrolment.SelfTestCommand("fm.reaper.Reaper", Layout));
    }

    [Fact]
    public void PublishingGivesNativeDawsSeparateCabinetScanPaths()
    {
        using var home = new TempHome();

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Empty(conflicts);
        Assert.True(Directory.Exists(home.Layout.WindowsScanDir(".vst3")));
        Assert.Null(new DirectoryInfo(home.Layout.WindowsScanDir(".vst3")).LinkTarget);
        Assert.Equal(
            home.Layout.WindowsScanDir(".vst3"),
            new DirectoryInfo(home.Layout.BridgeOutputDir(".vst3")).LinkTarget);
    }

    [Fact]
    public void PublishingMovesWhatTheOldOutputLinkHeldIntoTheScanPath()
    {
        using var home = new TempHome();
        var output = home.Layout.BridgeOutputDir(".vst3");
        Directory.CreateDirectory(Path.Combine(output, "Plugin.vst3"));
        Directory.CreateDirectory(home.Layout.CabinetScanDir(".vst3"));
        File.CreateSymbolicLink(home.Layout.WindowsScanDir(".vst3"), output);

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Empty(conflicts);
        Assert.True(Directory.Exists(Path.Combine(home.Layout.WindowsScanDir(".vst3"), "Plugin.vst3")));
        Assert.Null(new DirectoryInfo(home.Layout.WindowsScanDir(".vst3")).LinkTarget);
        Assert.Equal(home.Layout.WindowsScanDir(".vst3"), new DirectoryInfo(output).LinkTarget);
    }

    [Fact]
    public void AnInterruptedMoveOfTheBridgeOutputFinishesOnTheNextStart()
    {
        using var home = new TempHome();
        var output = home.Layout.BridgeOutputDir(".vst3");
        Directory.CreateDirectory(Path.Combine(output, "Plugin.vst3"));
        Directory.CreateDirectory(home.Layout.CabinetScanDir(".vst3"));

        Enrolment.MoveBridgeOutputIntoScanDirectories(home.Layout);

        Assert.True(Directory.Exists(Path.Combine(home.Layout.WindowsScanDir(".vst3"), "Plugin.vst3")));
        Assert.Equal(home.Layout.WindowsScanDir(".vst3"), new DirectoryInfo(output).LinkTarget);
    }

    [Fact]
    public void AStartRepointsTheBridgeOutputSomethingElseRedirected()
    {
        using var home = new TempHome();
        var windows = home.Layout.WindowsScanDir(".vst3");
        var output = home.Layout.BridgeOutputDir(".vst3");
        var elsewhere = Directory.CreateDirectory(Path.Combine(home.Root, "elsewhere")).FullName;
        Directory.CreateDirectory(Path.Combine(windows, "Plugin.vst3"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.CreateSymbolicLink(output, elsewhere);

        Enrolment.MoveBridgeOutputIntoScanDirectories(home.Layout);

        Assert.Equal(windows, new DirectoryInfo(output).LinkTarget);
        Assert.True(Directory.Exists(Path.Combine(windows, "Plugin.vst3")));
        Assert.True(Directory.Exists(elsewhere));
    }

    [Fact]
    public void NativePluginsMoveIntoTheScanPathsAndLeaveALinkBehind()
    {
        using var home = new TempHome();
        var bundle = Path.Combine(home.Layout.NativePath("synth"), "Folder", "Synth.vst3");
        Directory.CreateDirectory(bundle);
        var vst2 = Path.Combine(home.Layout.NativePath("synth"), "Folder", "Synth.so");
        File.WriteAllText(vst2, "vst2");
        var placed = home.Layout.NativePlacement(bundle);
        Directory.CreateDirectory(home.Layout.NativeScanDir(".vst3"));
        File.CreateSymbolicLink(placed, bundle);
        Directory.CreateDirectory(home.Layout.NativeScanDir(".so"));
        File.CreateSymbolicLink(home.Layout.NativePlacement(vst2), vst2);

        Enrolment.MoveNativeIntoScanDirectories(home.Layout);

        Assert.True(Directory.Exists(placed));
        Assert.Null(new DirectoryInfo(placed).LinkTarget);
        Assert.Equal(placed, new DirectoryInfo(bundle).LinkTarget);
        Assert.Equal("vst2", File.ReadAllText(home.Layout.NativePlacement(vst2)));
        Assert.Null(new FileInfo(home.Layout.NativePlacement(vst2)).LinkTarget);
        Assert.Empty(Enrolment.UnmovedNative(home.Layout));
    }

    [Fact]
    public void ANativeBundleNothingPublishedStaysInCabinet()
    {
        using var home = new TempHome();
        var bundle = Path.Combine(home.Layout.NativePath("synth"), "Synth.vst3");
        Directory.CreateDirectory(bundle);

        Enrolment.MoveNativeIntoScanDirectories(home.Layout);

        Assert.True(Directory.Exists(bundle));
        Assert.Null(new DirectoryInfo(bundle).LinkTarget);
        Assert.False(Path.Exists(home.Layout.NativePlacement(bundle)));
    }

    [Fact]
    public void ANativePluginBeingInstalledIsLeftForItsInstallToFinish()
    {
        using var home = new TempHome();
        var bundle = Path.Combine(home.Layout.NativePath("synth"), "Synth.vst3");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(home.Layout.NativeScanDir(".vst3"));
        File.CreateSymbolicLink(home.Layout.NativePlacement(bundle), bundle);
        using var installing = Underway.Begin(home.Layout.NativeInstalling("synth"))!;

        Enrolment.MoveNativeIntoScanDirectories(home.Layout);

        Assert.Null(new DirectoryInfo(bundle).LinkTarget);
        Assert.Equal([home.Layout.NativePlacement(bundle)], Enrolment.UnmovedNative(home.Layout));
    }

    [Fact]
    public void ARelativeLinkInsideAPluginFollowsTheBundleItPointsInto()
    {
        using var home = new TempHome();
        var root = home.Layout.NativePath("synth");
        var module = Path.Combine(root, "Synth.vst3", "Contents", "x86_64-linux");
        Directory.CreateDirectory(module);
        File.WriteAllText(Path.Combine(module, "Synth.so"), "module");
        var clap = Path.Combine(root, "Synth.clap");
        File.CreateSymbolicLink(clap, Path.Combine("Synth.vst3", "Contents", "x86_64-linux", "Synth.so"));
        foreach (var bundle in new[] { Path.Combine(root, "Synth.vst3"), clap })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(home.Layout.NativePlacement(bundle))!);
            File.CreateSymbolicLink(home.Layout.NativePlacement(bundle), bundle);
        }

        Enrolment.MoveNativeIntoScanDirectories(home.Layout);

        var placed = home.Layout.NativePlacement(clap);
        Assert.Equal(
            Path.Combine(home.Layout.NativePlacement(Path.Combine(root, "Synth.vst3")), "Contents", "x86_64-linux", "Synth.so"),
            new FileInfo(placed).LinkTarget);
        Assert.Equal("module", File.ReadAllText(placed));
        Assert.Equal(placed, new FileInfo(clap).LinkTarget);
    }

    [Fact]
    public void NoNativePluginMovesIntoACabinetScanPathSomethingElseOwns()
    {
        using var home = new TempHome();
        var bundle = Path.Combine(home.Layout.NativePath("synth"), "Synth.vst3");
        Directory.CreateDirectory(bundle);
        var elsewhere = Directory.CreateDirectory(Path.Combine(home.Layout.Home, "elsewhere", "native")).FullName;
        Directory.CreateDirectory(home.Layout.ScanDir(".vst3"));
        File.CreateSymbolicLink(home.Layout.CabinetScanDir(".vst3"), Path.GetDirectoryName(elsewhere)!);
        File.CreateSymbolicLink(Path.Combine(elsewhere, "Synth.vst3"), bundle);

        Enrolment.MoveNativeIntoScanDirectories(home.Layout);

        Assert.Null(new DirectoryInfo(bundle).LinkTarget);
        Assert.Equal(bundle, new DirectoryInfo(Path.Combine(elsewhere, "Synth.vst3")).LinkTarget);
    }

    [Fact]
    public void ANativeScanEntryCabinetDidNotMakeIsLeftWhereItIs()
    {
        using var home = new TempHome();
        var bundle = Path.Combine(home.Layout.NativePath("synth"), "Synth.vst3");
        Directory.CreateDirectory(bundle);
        var placed = Path.Combine(home.Layout.NativeScanDir(".vst3"), "Synth.vst3");
        Directory.CreateDirectory(placed);

        Enrolment.MoveNativeIntoScanDirectories(home.Layout);

        Assert.True(Directory.Exists(bundle));
        Assert.Null(new DirectoryInfo(bundle).LinkTarget);
    }

    [Fact]
    public void PublishingAgainKeepsCabinetScanPaths()
    {
        using var home = new TempHome();
        Enrolment.PublishNative(home.Layout);

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Empty(conflicts);
        Assert.Equal(
            home.Layout.WindowsScanDir(".clap"),
            new DirectoryInfo(home.Layout.BridgeOutputDir(".clap")).LinkTarget);
    }

    [Fact]
    public void AForeignNativeScanPathIsNeverReplaced()
    {
        using var home = new TempHome();
        var elsewhere = Directory.CreateDirectory(Path.Combine(home.Layout.Home, "elsewhere")).FullName;
        Directory.CreateDirectory(home.Layout.CabinetScanDir(".vst3"));
        File.CreateSymbolicLink(home.Layout.WindowsScanDir(".vst3"), elsewhere);

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Equal([home.Layout.WindowsScanDir(".vst3")], conflicts);
        Assert.Equal(elsewhere, new DirectoryInfo(home.Layout.WindowsScanDir(".vst3")).LinkTarget);
    }

    [Fact]
    public void AForeignCabinetLinkIsNeverReplaced()
    {
        using var home = new TempHome();
        var elsewhere = Path.Combine(home.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(home.Layout.ScanDir(".vst3"));
        File.CreateSymbolicLink(home.Layout.CabinetScanDir(".vst3"), elsewhere);

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Equal([home.Layout.CabinetScanDir(".vst3")], conflicts);
        Assert.Equal(elsewhere, new DirectoryInfo(home.Layout.CabinetScanDir(".vst3")).LinkTarget);
    }

    [Fact]
    public void TheLegacyCabinetLinkBecomesAFolderWithAWindowsLink()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.BridgeOutputDir(".vst3"));
        Directory.CreateDirectory(home.Layout.ScanDir(".vst3"));
        File.CreateSymbolicLink(home.Layout.CabinetScanDir(".vst3"), home.Layout.BridgeOutputDir(".vst3"));

        Enrolment.MoveLegacyScanLinks(home.Layout);

        Assert.Null(new DirectoryInfo(home.Layout.CabinetScanDir(".vst3")).LinkTarget);
        Assert.Equal(
            home.Layout.BridgeOutputDir(".vst3"),
            new DirectoryInfo(home.Layout.WindowsScanDir(".vst3")).LinkTarget);
        Assert.Empty(Enrolment.PublishNative(home.Layout));
    }

    [Fact]
    public void MovingLegacyLinksLeavesAForeignCabinetLinkAlone()
    {
        using var home = new TempHome();
        var elsewhere = Path.Combine(home.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(home.Layout.ScanDir(".clap"));
        File.CreateSymbolicLink(home.Layout.CabinetScanDir(".clap"), elsewhere);

        Enrolment.MoveLegacyScanLinks(home.Layout);

        Assert.Equal(elsewhere, new DirectoryInfo(home.Layout.CabinetScanDir(".clap")).LinkTarget);
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Fact]
    public void MovingLegacyLinksTwiceChangesNothingMore()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        var ours = Path.Combine(layout.NativePath("thing"), "libThing.so");
        Directory.CreateDirectory(layout.NativePath("thing"));
        File.WriteAllText(ours, "");
        Directory.CreateDirectory(layout.BridgeOutputDir(".vst"));
        Directory.CreateDirectory(layout.ScanDir(".so"));
        File.CreateSymbolicLink(layout.CabinetScanDir(".vst"), layout.BridgeOutputDir(".vst"));
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".so"), "libThing.so"), ours);

        Enrolment.MoveLegacyScanLinks(layout);
        Enrolment.MoveLegacyScanLinks(layout);

        Assert.Equal([layout.CabinetScanDir(".vst")], Directory.EnumerateFileSystemEntries(layout.ScanDir(".so")));
        Assert.Equal(
            [layout.NativeScanDir(".so"), layout.WindowsScanDir(".vst")],
            Directory.EnumerateFileSystemEntries(layout.CabinetScanDir(".vst")).Order());
        Assert.Equal(ours, new FileInfo(Path.Combine(layout.NativeScanDir(".so"), "libThing.so")).LinkTarget);
    }

    [Fact]
    public void ALegacyNativeLinkStaysWhenItsNewPlaceIsTaken()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        var ours = Path.Combine(layout.NativePath("thing"), "Thing.clap");
        var taken = Path.Combine(layout.NativeScanDir(".clap"), "Thing.clap");
        Directory.CreateDirectory(layout.NativePath("thing"));
        File.WriteAllText(ours, "");
        Directory.CreateDirectory(taken);
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".clap"), "Thing.clap"), ours);

        Enrolment.MoveLegacyScanLinks(layout);

        Assert.Equal(ours, new FileInfo(Path.Combine(layout.ScanDir(".clap"), "Thing.clap")).LinkTarget);
        Assert.Null(new DirectoryInfo(taken).LinkTarget);
    }

    [Fact]
    public void MovingLegacyLinksOnAFreshHomeCreatesNothing()
    {
        using var home = new TempHome();

        Enrolment.MoveLegacyScanLinks(home.Layout);

        Assert.All(Layout.ScanDirectories, directory =>
            Assert.False(Path.Exists(home.Layout.ScanDir(directory))));
    }

    [Fact]
    public void MovingLegacyLinksLeavesPluginsThatAreNotLinksAlone()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        var bundle = Path.Combine(layout.ScanDir(".vst3"), "Installed.vst3");
        var library = Path.Combine(layout.ScanDir(".so"), "libInstalled.so");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(layout.ScanDir(".so"));
        File.WriteAllText(library, "");

        Enrolment.MoveLegacyScanLinks(layout);

        Assert.Equal([bundle], Directory.EnumerateFileSystemEntries(layout.ScanDir(".vst3")));
        Assert.Equal([library], Directory.EnumerateFileSystemEntries(layout.ScanDir(".so")));
    }

    [Fact]
    public void LegacyNativeLinksMoveIntoTheNativeFolderAndOthersStay()
    {
        using var home = new TempHome();
        var layout = home.Layout;
        var ours = Path.Combine(layout.NativePath("thing"), "Thing.vst3");
        var lv2 = Path.Combine(layout.NativePath("thing"), "Thing.lv2");
        var theirs = Path.Combine(home.Root, "elsewhere", "Theirs.vst3");
        Directory.CreateDirectory(ours);
        Directory.CreateDirectory(lv2);
        Directory.CreateDirectory(theirs);
        Directory.CreateDirectory(layout.ScanDir(".vst3"));
        Directory.CreateDirectory(layout.ScanDir(".lv2"));
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".vst3"), "Thing.vst3"), ours);
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".vst3"), "Theirs.vst3"), theirs);
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".lv2"), "Thing.lv2"), lv2);

        Enrolment.MoveLegacyScanLinks(layout);

        Assert.Equal(
            ours, new DirectoryInfo(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")).LinkTarget);
        Assert.False(Path.Exists(Path.Combine(layout.ScanDir(".vst3"), "Thing.vst3")));
        Assert.True(Path.Exists(Path.Combine(layout.ScanDir(".vst3"), "Theirs.vst3")));
        Assert.True(Path.Exists(Path.Combine(layout.ScanDir(".lv2"), "Thing.lv2")));
    }

    [Fact]
    public void LegacyCabinetLinksAreRemovedWithoutTouchingUpstreamFiles()
    {
        using var home = new TempHome();
        home.GiveBridge("libyabridge-chainloader-vst3.so");
        Directory.CreateDirectory(home.Layout.NativeYabridgeDir);
        var cabinetLink = Path.Combine(
            home.Layout.NativeYabridgeDir, "libyabridge-chainloader-vst3.so");
        File.CreateSymbolicLink(
            cabinetLink,
            Path.Combine(home.Layout.HostYabridgeDir, "libyabridge-chainloader-vst3.so"));
        var upstreamFile = Path.Combine(home.Layout.NativeYabridgeDir, "yabridgectl");
        File.WriteAllText(upstreamFile, "upstream");

        Enrolment.RemoveLegacyNativeLinks(home.Layout);

        Assert.False(Path.Exists(cabinetLink));
        Assert.Equal("upstream", File.ReadAllText(upstreamFile));
    }

    [Fact]
    public void AnAlreadyEmptiedNativeYabridgeDirectoryIsLeftAlone()
    {
        using var home = new TempHome();
        home.GiveBridge("cabinet-wine", "yabridge-host.exe");
        Directory.CreateDirectory(home.Layout.NativeYabridgeDir);

        Enrolment.RemoveLegacyNativeLinks(home.Layout);

        Assert.Empty(Directory.EnumerateFileSystemEntries(home.Layout.NativeYabridgeDir));
    }

    [Fact]
    public void ANativeYabridgeDirectorySymlinkIsLeftAlone()
    {
        using var home = new TempHome();
        var upstream = Path.Combine(home.Root, "upstream-yabridge");
        Directory.CreateDirectory(upstream);
        File.WriteAllText(Path.Combine(upstream, "yabridgectl"), "upstream");
        Directory.CreateDirectory(Path.GetDirectoryName(home.Layout.NativeYabridgeDir)!);
        File.CreateSymbolicLink(home.Layout.NativeYabridgeDir, upstream);

        Enrolment.RemoveLegacyNativeLinks(home.Layout);

        Assert.Equal(upstream, new DirectoryInfo(home.Layout.NativeYabridgeDir).LinkTarget);
        Assert.Equal("upstream", File.ReadAllText(Path.Combine(upstream, "yabridgectl")));
    }

    [Theory]
    [InlineData("fm.reaper.Reaper")]
    [InlineData("org.ardour.Ardour")]
    [InlineData("com.bitwig.BitwigStudio")]
    [InlineData("io.github.some_one.my-daw")]
    public void AFlatpakApplicationIdIsADaw(string id)
    {
        Assert.True(Enrolment.IsAppId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("global")]
    [InlineData("org.ardour")]
    [InlineData("org..Ardour")]
    [InlineData("org.ardour.8Ardour")]
    [InlineData("../../.ssh")]
    [InlineData("org.ardour.Ardour --reset")]
    [InlineData("org.my-co.Daw")]
    public void AnythingElseIsRefused(string id)
    {
        Assert.False(Enrolment.IsAppId(id));
    }

    [Fact]
    public void TheTrustBoundaryKeepsWineOffTheHostAndNamesTheScanDirectories()
    {
        var boundary = Enrolment.TrustBoundary("fm.reaper.Reaper");

        Assert.Contains(Enrolment.Grants, grant => grant.Contains("not on your host", StringComparison.Ordinal));
        Assert.All(
            Layout.BridgedScanDirectories.Append(".lv2"),
            directory => Assert.Contains("~/" + directory, boundary));
    }

    private sealed class TempHome : IDisposable
    {
        private readonly string root = TestRoot.Create("enrolment");

        public Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

        public string Root => root;

        public void GiveBridge(params string[] files)
        {
            Directory.CreateDirectory(Layout.HostYabridgeDir);

            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(Layout.HostYabridgeDir, file), "bridge");
            }
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
