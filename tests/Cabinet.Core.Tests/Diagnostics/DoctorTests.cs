using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class DoctorTests : IDisposable
{
    private static IniFile Info(string app, string runtime) =>
        IniFile.Parse(["[Instance]", $"app-extensions={app}", $"runtime-extensions={runtime}"]);

    [Fact]
    public void MountedCompatAndMatchingGraphicsPass()
    {
        var checks = Doctor.ThirtyTwoBit(Info(
            "org.freedesktop.Platform.Compat.i386=aa;org.freedesktop.Platform.GL32.default=bb",
            "org.freedesktop.Platform.GL.default=cc;org.freedesktop.Platform.GL.default=dd"));

        Assert.All(checks, check => Assert.Equal(Status.Ok, check.Status));
    }

    [Fact]
    public void MissingCompatFailsAndPointsAtAnUpdate()
    {
        var compat = Doctor.ThirtyTwoBit(Info(
            "org.winehq.Wine.gecko=aa",
            "org.freedesktop.Platform.GL.default=cc")).First();

        Assert.Equal(Status.Fail, compat.Status);
        Assert.Contains("flatpak update io.github.mark12870.cabinet", compat.Detail);
    }

    [Fact]
    public void NoAppExtensionsAtAllStillFails()
    {
        var info = IniFile.Parse(
            ["[Instance]", "runtime-extensions=org.freedesktop.Platform.GL.default=cc"]);

        var checks = Doctor.ThirtyTwoBit(info).ToList();

        Assert.Equal([Status.Fail, Status.Warn], checks.Select(check => check.Status));
    }

    [Fact]
    public void GraphicsWithoutTheirThirtyTwoBitHalfNameItOnce()
    {
        var graphics = Doctor.ThirtyTwoBit(Info(
            "org.freedesktop.Platform.Compat.i386=aa",
            "org.freedesktop.Platform.GL.nvidia-580-82-09=cc;org.freedesktop.Platform.GL.nvidia-580-82-09=dd"))
            .Last();

        Assert.Equal(Status.Warn, graphics.Status);
        Assert.Equal(
            "32-bit plugins cannot draw their editors — run "
            + "`flatpak install flathub org.freedesktop.Platform.GL32.nvidia-580-82-09`",
            graphics.Detail);
    }

    [Fact]
    public void OutsideAFlatpakThereIsNothingToCheck()
    {
        Assert.Empty(Doctor.ThirtyTwoBit(IniFile.Empty));
    }

    [Fact]
    public void APrefixHoldingNoRecordedPluginIsNobodysBusiness()
    {
        GivePrefix("aalto");

        Assert.DoesNotContain(Checks(), c => c.Name == "prefix updates");
    }

    [Fact]
    public void APluginRecordedInTwoPrefixesIsNamedWithTheOneCabinetActsOn()
    {
        GivePrefix("first");
        GivePrefix("second");
        File.WriteAllText(Layout.PrefixPluginsFile("first"), "aalto\n");
        File.WriteAllText(Layout.PrefixPluginsFile("second"), "aalto\n");

        var check = Assert.Single(Checks(), c => c.Name == "installed twice");

        Assert.Equal(Status.Warn, check.Status);
        Assert.StartsWith(
            "aalto is recorded in first and second, and Cabinet acts only on the one in second",
            check.Detail);
    }

    [Fact]
    public void ADirectoryThatCannotNameAPrefixIsReportedRatherThanDropped()
    {
        GivePrefix("gadget");
        Directory.CreateDirectory(Path.Combine(Layout.PrefixesDir, ".hidden"));

        var check = Assert.Single(Checks(), c => c.Name == "prefix names");

        Assert.Equal(Status.Warn, check.Status);
        Assert.StartsWith("'.hidden' in", check.Detail);
    }

    [Fact]
    public void APrefixBehindItsCatalogueSetupIsWarnedAbout()
    {
        GiveRunner("wine-9.21-staging-tkg");
        GivePrefix("gadget", "wine-9.21-staging-tkg");
        GiveEntry("acme", "gadget", "Gadget 2", "9.21");
        File.AppendAllText(Path.Combine(root, "library", "acme", "gadget.yml"), "Env: GADGET_MODE=on\n");
        GiveRecord("gadget", "gadget");
        PrefixSetup.From(new PrefixConfig("", 1, "9.21", false, SyncMode.System, [],
            new Dictionary<string, string>(), false), null).Save(Layout.PrefixSetupFile("gadget"));

        var check = Assert.Single(Checks(), c => c.Name == "prefix updates");

        Assert.Equal(Status.Warn, check.Status);
        Assert.StartsWith("gadget has a changed setup for gadget", check.Detail);
    }

    [Fact]
    public void APrefixHoldingPluginsWithDifferentSetupsIsWarnedAbout()
    {
        GivePrefix("gadget");
        GiveEntry("acme", "gadget", "Gadget 2", "9.21");
        GiveEntry("other", "widget", "Widget", "9.21");
        File.WriteAllLines(Layout.PrefixPluginsFile("gadget"), ["gadget", "widget"]);

        var check = Assert.Single(Checks(), c => c.Name == "mixed prefixes");

        Assert.Equal(Status.Warn, check.Status);
        Assert.StartsWith("gadget holds gadget, widget", check.Detail);
    }

    [Fact]
    public void DoctorFailsWhenAPrefixNamesARunnerThatIsGone()
    {
        GivePrefix("aalto", "wine-9.21-staging");

        var check = Checks().Single(c => c.Name == "prefix runners");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("aalto -> wine-9.21-staging", check.Detail);
    }

    [Fact]
    public void DoctorPassesOnceThatRunnerIsThere()
    {
        GivePrefix("aalto", "wine-9.21-staging");
        GiveRunner("wine-9.21-staging");

        Assert.Equal(
            Status.Ok, Checks().Single(c => c.Name == "prefix runners").Status);
    }

    [Fact]
    public void DoctorReportsAnEnrolledDawWithoutTheSharedSocketDirectory()
    {
        var daw = "fm.reaper.Reaper";
        Directory.CreateDirectory(Layout.HostYabridgeDir);
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.SandboxYabridgeLink)!);
        File.CreateSymbolicLink(Layout.SandboxYabridgeLink, Layout.HostYabridgeDir);

        var overrides = Path.Combine(root, ".local", "share", "flatpak", "overrides", daw);
        Directory.CreateDirectory(Path.GetDirectoryName(overrides)!);
        File.WriteAllLines(
            overrides,
            [
                "[Context]",
                "devices=shm;",
                $"filesystems=xdg-run/yabridge:create;{Layout.HostAppFiles};{Layout.PrefixesDir};{Layout.NativeDir};{Layout.BridgeHome};",
                "",
                "[Session Bus Policy]",
                "org.freedesktop.Flatpak=talk",
                "",
                "[Environment]",
                $"WINELOADER={Layout.ShimPath}",
            ]);

        var check = Checks().Single(found => found.Name == $"DAW {daw}");

        Assert.Equal(Status.Fail, check.Status);
        Assert.StartsWith($"missing --env=YABRIDGE_TEMP_DIR={Layout.SocketDir}", check.Detail);
        Assert.Contains("cabinet enrol fm.reaper.Reaper", check.Detail);
        Assert.Equal(daw, check.Daw);
        Assert.Equal([daw], new Doctor(Layout, new UnusedRunner()).DawsMissingPermissions());
    }

    [Fact]
    public void DoctorTakesADawAsEnrolledOnlyByAnOverrideThatLoadsWineThroughCabinet()
    {
        var overrides = Path.Combine(root, ".local", "share", "flatpak", "overrides");
        Directory.CreateDirectory(overrides);
        File.WriteAllLines(Path.Combine(overrides, "org.example.Editor"), ["[Context]", "shared=network;"]);
        File.WriteAllLines(
            Path.Combine(overrides, "org.example.Backup"), ["[Context]", $"filesystems={Layout.PrefixesDir}:ro;"]);
        File.CreateSymbolicLink(Path.Combine(overrides, "org.example.Gone"), Path.Combine(root, "nowhere"));
        File.WriteAllLines(Path.Combine(overrides, "global"), ["[Environment]", $"WINELOADER={Layout.ShimPath}"]);
        File.WriteAllLines(Path.Combine(overrides, Layout.AppId), ["[Environment]", $"WINELOADER={Layout.ShimPath}"]);
        File.WriteAllLines(
            Path.Combine(overrides, "fm.reaper.Reaper"), ["[Environment]", $"WINELOADER={Layout.ShimPath}"]);
        File.WriteAllLines(
            Path.Combine(overrides, "com.bitwig.BitwigStudio"),
            ["[Session Bus Policy]", $"{Layout.BridgeBusName}=talk"]);

        Assert.Equal(
            ["DAW com.bitwig.BitwigStudio", "DAW fm.reaper.Reaper"],
            Checks().Select(check => check.Name).Where(name => name.StartsWith("DAW ", StringComparison.Ordinal)));
    }

    [Fact]
    public void DoctorDoesNotAcceptAFilesystemPathThatOnlyContainsTheExpectedPath()
    {
        var daw = "fm.reaper.Reaper";
        Directory.CreateDirectory(Layout.HostYabridgeDir);
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.SandboxYabridgeLink)!);
        File.CreateSymbolicLink(Layout.SandboxYabridgeLink, Layout.HostYabridgeDir);

        var overrides = Path.Combine(root, ".local", "share", "flatpak", "overrides", daw);
        Directory.CreateDirectory(Path.GetDirectoryName(overrides)!);
        File.WriteAllLines(
            overrides,
            [
                "[Context]",
                "devices=shm;",
                $"filesystems=xdg-run/yabridge:create;{Layout.HostAppFiles}-other;"
                    + $"{Layout.PrefixesDir};{Layout.NativeDir};{Layout.BridgeHome};",
                "",
                "[Session Bus Policy]",
                "org.freedesktop.Flatpak=talk",
                "",
                "[Environment]",
                $"WINELOADER={Layout.ShimPath}",
                $"YABRIDGE_TEMP_DIR={Layout.SocketDir}",
                $"YABRIDGE_DEBUG_FILE={Layout.RuntimeLogPath}",
                "YABRIDGE_NO_WATCHDOG=1",
            ]);

        var check = Checks().Single(found => found.Name == $"DAW {daw}");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("files/lib/yabridge:ro", check.Detail);
    }

    [Fact]
    public void DoctorTakesADawEnrolledWithTheBridgeAsEnrolled()
    {
        GiveEnrolment("fm.reaper.Reaper", ["io.github.mark12870.cabinet.Bridge=talk"], []);

        var check = Checks().Single(found => found.Name == "DAW fm.reaper.Reaper");

        Assert.Equal(Status.Ok, check.Status);
        Assert.Empty(new Doctor(Layout, new UnusedRunner()).DawsMissingPermissions());
    }

    [Fact]
    public void DoctorWarnsThatAnOlderEnrolmentStillGrantsHostCommands()
    {
        GiveEnrolment("fm.reaper.Reaper", ["org.freedesktop.Flatpak=talk"], LegacyEnvironment);

        var check = Checks().Single(found => found.Name == "DAW fm.reaper.Reaper");

        Assert.Equal(Status.Warn, check.Status);
        Assert.Contains("run any command on your host", check.Detail);
        Assert.Contains("cabinet enrol fm.reaper.Reaper", check.Detail);
        Assert.Empty(new Doctor(Layout, new UnusedRunner()).DawsMissingPermissions());
    }

    [Fact]
    public void DoctorStillWarnsWhileAReEnrolledDawKeepsHostCommands()
    {
        GiveEnrolment(
            "fm.reaper.Reaper",
            ["io.github.mark12870.cabinet.Bridge=talk", "org.freedesktop.Flatpak=talk"],
            LegacyEnvironment);

        var check = Checks().Single(found => found.Name == "DAW fm.reaper.Reaper");

        Assert.Equal(Status.Warn, check.Status);
        Assert.Contains("cabinet enrol fm.reaper.Reaper", check.Detail);
        Assert.Empty(new Doctor(Layout, new UnusedRunner()).DawsMissingPermissions());
    }

    [Fact]
    public void DoctorWarnsThatAnEnrolmentStillHoldsWhatAnOlderReleaseAskedFor()
    {
        GiveEnrolment(
            "fm.reaper.Reaper", ["io.github.mark12870.cabinet.Bridge=talk"], [$"WINELOADER={Layout.ShimPath}"]);

        var check = Checks().Single(found => found.Name == "DAW fm.reaper.Reaper");

        Assert.Equal(Status.Warn, check.Status);
        Assert.Contains("cabinet enrol fm.reaper.Reaper", check.Detail);
        Assert.Equal("fm.reaper.Reaper", check.Daw);
        Assert.Empty(new Doctor(Layout, new UnusedRunner()).DawsMissingPermissions());
    }

    [Fact]
    public void DoctorFailsADawThatCannotStartCabinetsWine()
    {
        GiveEnrolment("fm.reaper.Reaper", ["org.example.Other=talk"], LegacyEnvironment);

        var check = Checks().Single(found => found.Name == "DAW fm.reaper.Reaper");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("--talk-name=io.github.mark12870.cabinet.Bridge", check.Detail);
        Assert.Equal(["fm.reaper.Reaper"], new Doctor(Layout, new UnusedRunner()).DawsMissingPermissions());
    }

    [Fact]
    public void DoctorReportsMissingNativeScanPaths()
    {
        var check = Checks().Single(found => found.Name == "native DAWs");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("bridge what is installed again", check.Detail);
    }

    [Fact]
    public void DoctorReportsNativeLinksWithoutPublishedPlugins()
    {
        Enrolment.PublishNative(Layout);

        var check = Checks().Single(found => found.Name == "native DAWs");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("bridge what is installed again", check.Detail);
    }

    [Fact]
    public void DoctorPassesOnceTheNativeLinksReachTheBridge()
    {
        Directory.CreateDirectory(Layout.HostYabridgeDir);
        File.WriteAllText(
            Path.Combine(Layout.HostYabridgeDir, "libyabridge-chainloader-vst3.so"), "bridge");
        Directory.CreateDirectory(Layout.BridgeOutputDir(".vst3"));
        File.WriteAllText(
            Path.Combine(Layout.BridgeOutputDir(".vst3"), "plugin.so"), "plugin");
        Enrolment.PublishNative(Layout);

        Assert.Equal(
            Status.Ok, Checks().Single(found => found.Name == "native DAWs").Status);
    }

    [Fact]
    public void DoctorFailsOnANativeScanPathSomethingElseOwns()
    {
        Directory.CreateDirectory(Layout.CabinetScanDir(".vst3"));
        File.WriteAllText(Layout.WindowsScanDir(".vst3"), "someone else's");

        var check = Checks().Single(found => found.Name == "native DAWs");

        Assert.Equal(Status.Fail, check.Status);
        Assert.Contains("already owned by something else", check.Detail);
    }

    private readonly string root = TestRoot.Create("doctor");

    private Layout Layout =>
        new(root, "/run/user/1000", Path.Combine(root, "data"), null,
            Path.Combine(root, "library"));

    private IReadOnlyList<Check> Checks() => new Doctor(Layout, new UnusedRunner()).Run();

    private string[] LegacyEnvironment =>
        [$"WINELOADER={Layout.ShimPath}", $"YABRIDGE_DEBUG_FILE={Layout.RuntimeLogPath}"];

    private void GiveEnrolment(string daw, string[] busPolicy, string[] environment)
    {
        var overrides = Path.Combine(root, ".local", "share", "flatpak", "overrides", daw);
        Directory.CreateDirectory(Path.GetDirectoryName(overrides)!);
        File.WriteAllLines(
            overrides,
            [
                "[Context]",
                "devices=shm;",
                $"filesystems=xdg-run/yabridge:create;{Layout.HostYabridgeDir}:ro;{Layout.PrefixesDir}:ro;",
                "",
                "[Session Bus Policy]",
                .. busPolicy,
                "",
                "[Environment]",
                $"YABRIDGE_TEMP_DIR={Layout.SocketDir}",
                .. environment,
            ]);
    }

    private void GiveEntry(string vendor, string id, string name, string runner)
    {
        var dir = Path.Combine(root, "library", vendor);
        Directory.CreateDirectory(dir);

        File.WriteAllText(
            Path.Combine(dir, id + ".yml"),
            $"Name: {name}\nKind: windows\nSource: byo\nRunner: {runner}\n");
    }

    private void GiveRecord(string prefix, string id) =>
        File.WriteAllText(Layout.PrefixPluginsFile(prefix), id + Environment.NewLine);

    private void GiveRunner(string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Layout.RunnerWine(name))!);
        File.WriteAllText(Layout.RunnerWine(name), "");
        Directory.CreateDirectory(Path.Combine(Layout.RunnerPath(name), "lib32"));
    }

    private void GivePrefix(string name)
    {
        Directory.CreateDirectory(Layout.PrefixPath(name));
    }

    private void GivePrefix(string name, string runner)
    {
        Directory.CreateDirectory(Layout.PrefixPath(name));
        File.WriteAllText(Layout.PrefixRunnerFile(name), runner);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
