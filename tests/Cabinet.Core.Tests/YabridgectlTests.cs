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
    public void ARegisteredCanonicalEquivalentIsNotAddedAgain()
    {
        var layout = TestLayout();
        var pluginDirectory = layout.PrefixVst3Dir("gadget");
        Directory.CreateDirectory(pluginDirectory);
        var runner = new RecordingRunner(
            outputs: args => args.SequenceEqual(["list"]) ? pluginDirectory + "/.\n" : "");
        var prefix = new Prefix(
            "gadget", layout.PrefixPath("gadget"), true, Cabinet.Core.Layout.BundledRunner,
            null, SyncMode.System, false);

        var result = new Yabridgectl(layout, runner).SyncPrefixes([prefix]);

        Assert.True(result.Ok);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["add", _]);
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
    public void IndependentRegistrationFailuresAreAllReportedAndDoNotSync()
    {
        var layout = TestLayout();
        var first = layout.PrefixVst3Dir("first");
        var second = layout.PrefixVst3Dir("second");
        var stale = layout.PrefixVst3Dir("gone");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var runner = new ResultRunner(args => args switch
        {
            ["list"] => new(0, stale + "\n", ""),
            ["add", var directory] when directory == first =>
                new(7, "first output\n", "first error\n"),
            ["rm", var directory] when directory == stale =>
                new(8, "stale output\n", "stale error\n"),
            _ => new(0, "", ""),
        });
        var prefixes = new[]
        {
            new Prefix("first", layout.PrefixPath("first"), true, Cabinet.Core.Layout.BundledRunner,
                null, SyncMode.System, false),
            new Prefix("second", layout.PrefixPath("second"), true, Cabinet.Core.Layout.BundledRunner,
                null, SyncMode.System, false),
        };

        var result = new Yabridgectl(layout, runner).SyncPrefixes(prefixes);

        Assert.False(result.Ok);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains($"add {first}", result.Stdout);
        Assert.Contains("first output", result.Stdout);
        Assert.Contains($"add {first}", result.Stderr);
        Assert.Contains("first error", result.Stderr);
        Assert.Contains($"remove {stale}", result.Stdout);
        Assert.Contains("stale output", result.Stdout);
        Assert.Contains($"remove {stale}", result.Stderr);
        Assert.Contains("stale error", result.Stderr);
        Assert.Contains(runner.Calls, call => call is ["add", var directory] && directory == second);
        Assert.DoesNotContain(runner.Calls, call => call is ["sync", "--prune"]);
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

    [Fact]
    public void BridgeIncludesFailureOutputWhenNoOutputHandlerWasGiven()
    {
        var layout = TestLayout();
        var runner = new ResultRunner(args => args switch
        {
            ["list"] => new(0, "", ""),
            ["sync", "--prune"] => new(9, "sync output\n", "sync error\n"),
            _ => new(0, "", ""),
        });

        var failure = Assert.Throws<InvalidOperationException>(
            () => new Yabridgectl(layout, runner).Bridge([], null));

        Assert.Contains("yabridgectl exited with 9", failure.Message);
        Assert.Contains("sync output", failure.Message);
        Assert.Contains("sync error", failure.Message);
    }

    [Fact]
    public void BridgeHandsAFailedDirectoryAndItsErrorToTheOutput()
    {
        var layout = TestLayout();
        var broken = layout.PrefixVst3Dir("broken");
        Directory.CreateDirectory(broken);
        var runner = new ResultRunner(args => args switch
        {
            ["list"] => new(0, "", ""),
            ["add", _] => new(3, "", "cannot read it\n"),
            _ => new(0, "", ""),
        });
        var prefix = new Prefix(
            "broken", layout.PrefixPath("broken"), true, Cabinet.Core.Layout.BundledRunner,
            null, SyncMode.System, false);
        var said = new List<string>();

        var failure = Assert.Throws<InvalidOperationException>(
            () => new Yabridgectl(layout, runner).Bridge([prefix], said.Add));

        Assert.Contains($"add {broken} failed with exit code 3", said);
        Assert.Contains("cannot read it", said);
        Assert.Contains("yabridgectl exited with 3", failure.Message);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class ResultRunner(Func<IReadOnlyList<string>, ProcessResult> result) : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public ProcessResult Run(
            string file,
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string>? env = null,
            Action<string>? onOutput = null,
            string? workingDirectory = null,
            string? logTo = null,
            IReadOnlySet<string>? blankEnvironment = null,
            bool interactive = false,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(args);
            return result(args);
        }
    }
}
