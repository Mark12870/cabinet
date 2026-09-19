using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class EnrolmentTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    [Theory]
    [InlineData("--device=shm")]
    [InlineData("--filesystem=xdg-run/yabridge:create")]
    [InlineData("--talk-name=org.freedesktop.Flatpak")]
    [InlineData("--env=YABRIDGE_TEMP_DIR=/run/user/1000/yabridge")]
    [InlineData("--env=YABRIDGE_DEBUG_FILE=/run/user/1000/yabridge/yabridge.log")]
    [InlineData("--env=YABRIDGE_NO_WATCHDOG=1")]
    [InlineData("--filesystem=/home/u/.local/share/flatpak/app/"
                + "io.github.mark12870.cabinet/current/active/files:ro")]
    [InlineData("--filesystem=/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes:ro")]
    [InlineData("--filesystem=/home/u/.var/app/io.github.mark12870.cabinet/data/native:ro")]
    [InlineData("--filesystem=/home/u/.var/app/io.github.mark12870.cabinet/data/bridge:ro")]
    [InlineData("--env=WINELOADER=/home/u/.local/share/flatpak/app/"
                + "io.github.mark12870.cabinet/current/active/files/lib/yabridge/cabinet-wine")]
    public void TheOverrideCarriesEverythingTheBoundaryNeeds(string expected)
    {
        Assert.Contains(expected, Enrolment.OverrideArguments("fm.reaper.Reaper", Layout));
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
    public void PathsAreSpelledOutBecauseFlatpakDoesNoExpansion()
    {
        var command = Enrolment.OverrideCommand("fm.reaper.Reaper", Layout);

        Assert.DoesNotContain("$XDG_RUNTIME_DIR", command);
        Assert.DoesNotContain("~", command);
        Assert.StartsWith("flatpak override --user fm.reaper.Reaper", command);
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
        Assert.Equal(
            home.Layout.BridgeOutputDir(".vst3"),
            File.ResolveLinkTarget(
                home.Layout.WindowsScanDir(".vst3"), false)!.FullName);
    }

    [Fact]
    public void PublishingAgainKeepsCabinetScanPaths()
    {
        using var home = new TempHome();
        Enrolment.PublishNative(home.Layout);

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Empty(conflicts);
        Assert.Equal(
            home.Layout.BridgeOutputDir(".clap"),
            new DirectoryInfo(home.Layout.WindowsScanDir(".clap")).LinkTarget);
    }

    [Fact]
    public void AForeignNativeScanPathIsNeverReplaced()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.WindowsScanDir(".vst3"));

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Equal([home.Layout.WindowsScanDir(".vst3")], conflicts);
        Assert.Null(new DirectoryInfo(home.Layout.WindowsScanDir(".vst3")).LinkTarget);
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
    public void TheTrustBoundaryNamesTheHostAccessAndTheScanDirectories()
    {
        var boundary = Enrolment.TrustBoundary("fm.reaper.Reaper");

        Assert.Contains("run any command on your host", boundary);
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
