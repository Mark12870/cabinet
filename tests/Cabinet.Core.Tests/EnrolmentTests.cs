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
                home.Layout.CabinetScanDir(".vst3"), false)!.FullName);
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
            new DirectoryInfo(home.Layout.CabinetScanDir(".clap")).LinkTarget);
    }

    [Fact]
    public void AForeignNativeScanPathIsNeverReplaced()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.CabinetScanDir(".vst3"));

        var conflicts = Enrolment.PublishNative(home.Layout);

        Assert.Equal([home.Layout.CabinetScanDir(".vst3")], conflicts);
        Assert.Null(new DirectoryInfo(home.Layout.CabinetScanDir(".vst3")).LinkTarget);
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

    [Fact]
    public void LinkingPointsTheDawAtTheYabridgeItMustRead()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.DawDataHome("fm.reaper.Reaper"));

        var link = Enrolment.Link("fm.reaper.Reaper", home.Layout);

        Assert.Equal(home.Layout.DawYabridgeLink("fm.reaper.Reaper"), link);
        Assert.Equal(home.Layout.HostYabridgeDir, new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void LinkingAgainReplacesAStaleLink()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.DawDataHome("fm.reaper.Reaper"));
        File.CreateSymbolicLink(
            home.Layout.DawYabridgeLink("fm.reaper.Reaper"), "/somewhere/else");

        var link = Enrolment.Link("fm.reaper.Reaper", home.Layout);

        Assert.Equal(home.Layout.HostYabridgeDir, new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void ADawThatIsNotInstalledIsRefused()
    {
        using var home = new TempHome();

        Assert.Throws<DirectoryNotFoundException>(
            () => Enrolment.Link("fm.reaper.Reaper", home.Layout));
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
