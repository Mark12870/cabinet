using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class YabridgectlTests : IDisposable
{
    private const string Prefixes =
        "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes";

    private readonly string root = TestRoot.Create("yabridgectl");

    private Layout TestLayout()
    {
        var yabridge = Path.Combine(root, "yabridge");
        Directory.CreateDirectory(yabridge);
        File.WriteAllText(Path.Combine(yabridge, "yabridgectl"), "");
        return new(root, "/run/user/1000", yabridgeDir: yabridge);
    }

    private static Yabridgectl Subject =>
        new(new Layout("/home/u", "/run/user/1000"), new UnusedRunner());

    [Fact]
    public void ADeletedPrefixIsUnregistered()
    {
        var stale = Subject.StaleRegistrations(
            [
                $"{Prefixes}/gone/drive_c/Program Files/VstPlugins",
                $"{Prefixes}/kept/drive_c/Program Files/VstPlugins",
            ],
            new HashSet<string> { $"{Prefixes}/kept/drive_c/Program Files/VstPlugins" });

        Assert.Equal([$"{Prefixes}/gone/drive_c/Program Files/VstPlugins"], stale);
    }

    [Fact]
    public void ADirectoryAddedByHandIsLeftAlone()
    {
        var stale = Subject.StaleRegistrations(
            ["/home/u/.wine/drive_c/Program Files/VstPlugins"], new HashSet<string>());

        Assert.Empty(stale);
    }

    [Fact]
    public void ASiblingOfThePrefixesDirectoryIsNotMistakenForOne()
    {
        var stale = Subject.StaleRegistrations(
            [$"{Prefixes}-backup/gone/drive_c/Program Files/VstPlugins"],
            new HashSet<string>());

        Assert.Empty(stale);
    }

    [Fact]
    public void AFailedPluginRegistrationIsReturnedInsteadOfBeingAccepted()
    {
        var layout = TestLayout();
        var pluginDirectory = layout.PrefixVst3Dir("gadget");
        Directory.CreateDirectory(pluginDirectory);
        var runner = new RecordingRunner(
            exits: args => args.SequenceEqual(["add", pluginDirectory]) ? 1 : 0);
        var prefix = new Prefix(
            "gadget", layout.PrefixPath("gadget"), true, Cabinet.Core.Layout.BundledRunner,
            null, SyncMode.System, false);

        var result = new Yabridgectl(layout, runner).SyncPrefixes([prefix]);

        Assert.False(result.Ok);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(["sync", "--prune"]));
    }

    [Fact]
    public void AFailedStaleRegistrationRemovalIsReturnedInsteadOfBeingAccepted()
    {
        var layout = TestLayout();
        var stale = layout.PrefixVst3Dir("gone");
        var runner = new RecordingRunner(
            exits: args => args.SequenceEqual(["rm", stale]) ? 1 : 0,
            outputs: args => args.SequenceEqual(["list"]) ? stale + "\n" : "");
        var prefix = new Prefix(
            "gadget", layout.PrefixPath("gadget"), true, Cabinet.Core.Layout.BundledRunner,
            null, SyncMode.System, false);

        var result = new Yabridgectl(layout, runner).SyncPrefixes([prefix]);

        Assert.False(result.Ok);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(["sync", "--prune"]));
    }

    [Fact]
    public void AFailedRegistrationListIsReturnedInsteadOfBeingAccepted()
    {
        var layout = TestLayout();
        var runner = new RecordingRunner(
            exits: args => args.SequenceEqual(["list"]) ? 1 : 0);
        var prefix = new Prefix(
            "gadget", layout.PrefixPath("gadget"), true, Cabinet.Core.Layout.BundledRunner,
            null, SyncMode.System, false);

        var result = new Yabridgectl(layout, runner).SyncPrefixes([prefix]);

        Assert.False(result.Ok);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(["sync", "--prune"]));
    }

    [Fact]
    public void YabridgectlUsesCabinetsPrivateConfigurationAndOutputTree()
    {
        var layout = TestLayout();
        var runner = new RecordingRunner();

        new Yabridgectl(layout, runner).Status();

        Assert.Equal(layout.BridgeHome, runner.Environment["HOME"]);
        Assert.Equal(layout.BridgeDataHome, runner.Environment["XDG_DATA_HOME"]);
        Assert.Equal(layout.BridgeConfigHome, runner.Environment["XDG_CONFIG_HOME"]);
        Assert.Equal(layout.BridgeClapHome, runner.Environment["CLAP_PATH"]);
        Assert.Equal(layout.SocketDir, runner.Environment["YABRIDGE_TEMP_DIR"]);
    }

    [Fact]
    public void ASuccessfulSyncLinksTheNativeScanPaths()
    {
        var layout = TestLayout();

        var result = new Yabridgectl(layout, new RecordingRunner()).SyncAndPublish([]);

        Assert.True(result.Ok);
        Assert.Equal(
            layout.BridgeOutputDir(".vst3"),
            new DirectoryInfo(layout.CabinetScanDir(".vst3")).LinkTarget);
    }

    [Fact]
    public void ASuccessfulSyncDoesNotFailOnAnOccupiedNativeScanPath()
    {
        var layout = TestLayout();
        Directory.CreateDirectory(layout.CabinetScanDir(".vst3"));

        var result = new Yabridgectl(layout, new RecordingRunner()).SyncAndPublish([]);

        Assert.True(result.Ok);
        Assert.True(Directory.Exists(layout.CabinetScanDir(".vst3")));
        Assert.Contains(layout.CabinetScanDir(".vst3"), result.Stderr);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
