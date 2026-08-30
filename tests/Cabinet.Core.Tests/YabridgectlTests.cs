using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class YabridgectlTests : IDisposable
{
    private const string Prefixes =
        "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes";

    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

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
            null, SyncMode.System, null);

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
            null, SyncMode.System, null);

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
            null, SyncMode.System, null);

        var result = new Yabridgectl(layout, runner).SyncPrefixes([prefix]);

        Assert.False(result.Ok);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(["sync", "--prune"]));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
