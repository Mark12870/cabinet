using System.Collections.Concurrent;
using Cabinet.Core;

namespace Cabinet.Contract.Tests;

public sealed class ShimContractTests : IDisposable
{
    private readonly Shim shim = new();

    public void Dispose() => shim.Dispose();

    [Fact]
    public void DirectWineStreamsBothStreamsAndCarriesItsExitStatus()
    {
        var streamed = new ConcurrentQueue<string>();

        var result = shim.Prefixes.Run(Shim.Prefix, "wine", ["exit", "3"], streamed.Enqueue);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out 3" + Environment.NewLine, result.Stdout);
        Assert.Equal("err 3" + Environment.NewLine, result.Stderr);
        Assert.Equal(["err 3", "out 3"], streamed.Order());
        Assert.Empty(shim.Sockets);
    }

    [Fact]
    public void AJoinedJobCarriesItsOutputAndExitStatusBackThroughTheSession()
    {
        shim.Settle("session.log");
        var streamed = new ConcurrentQueue<string>();

        var result = shim.Prefixes.RunJoined(Shim.Prefix, ["exit", "3"], streamed.Enqueue);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out 3" + Environment.NewLine, result.Stdout);
        Assert.Equal("err 3" + Environment.NewLine, result.Stderr);
        Assert.Equal(["err 3", "out 3"], streamed.Order());
    }

    [Fact]
    public void ALoggedJoinedJobWritesBothStreamsToTheLogAndCarriesItsExitStatus()
    {
        shim.Settle("session.log");
        var log = shim.Scratch("job.log");

        var result = shim.Prefixes.RunJoined(Shim.Prefix, ["exit", "3"], logTo: log);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(["err 3", "out 3"], File.ReadAllLines(log).Order());
    }

    [Fact]
    public void ACapturedCallThatStartsTheSessionReturnsOnlyOnceTheSessionHasRetired()
    {
        var result = shim.Prefixes.RunJoined(Shim.Prefix, ["exit", "3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Empty(shim.Sockets);
    }

    [Fact]
    public async Task TheSessionReadsAsLiveWhileAJobRunsAndStillOnceItHasEnded()
    {
        var started = shim.Scratch("started");
        var release = shim.Scratch("release");

        var before = shim.Prefixes.SessionLive(Shim.Prefix);
        shim.Settle("session.log");
        var held = Task.Run(() => shim.Prefixes.RunJoined(Shim.Prefix, ["hold", started, release]));
        Assert.True(Shim.Appears(started));
        var during = shim.Prefixes.SessionLive(Shim.Prefix);
        File.WriteAllText(release, "");
        var status = (await held).ExitCode;
        var after = shim.Prefixes.SessionLive(Shim.Prefix);

        Assert.False(before);
        Assert.True(during);
        Assert.Equal(0, status);
        Assert.True(after);
    }

    [Fact]
    public void DirectWineUnsetsAnEmptyValueAndTheBlankedSocket()
    {
        shim.WriteEnvironment("CABINET_PROBE=one", "CABINET_BLANK=");

        var result = shim.Prefixes.Run(
            Shim.Prefix, "wine", ["env", "CABINET_PROBE", "CABINET_BLANK", "WAYLAND_DISPLAY"]);

        Assert.Equal(
            ["CABINET_PROBE=[one]", "CABINET_BLANK unset", "WAYLAND_DISPLAY unset"],
            Lines(result.Stdout));
    }

    [Fact]
    public void AJoinedJobSeesTheCurrentEnvironmentFileWithEmptyValuesAndAnEmptySocket()
    {
        shim.WriteEnvironment("CABINET_PROBE=one");
        shim.Settle("session.log");
        shim.WriteEnvironment("CABINET_PROBE=two", "CABINET_BLANK=");

        var result = shim.Prefixes.RunJoined(
            Shim.Prefix, ["env", "CABINET_PROBE", "CABINET_BLANK", "WAYLAND_DISPLAY"]);

        Assert.Equal(
            ["CABINET_PROBE=[two]", "CABINET_BLANK=[]", "WAYLAND_DISPLAY=[]"],
            Lines(result.Stdout));
    }

    [Fact]
    public void DirectWineInheritsTheCallersStdin()
    {
        var result = shim.Prefixes.Run(Shim.Prefix, "wine", ["stdin"]);

        Assert.Equal(CallersStdin(), result.Stdout.Trim());
    }

    [Fact]
    public void AJoinedJobInheritsTheCallersStdin()
    {
        shim.Settle("session.log");

        var result = shim.Prefixes.RunJoined(Shim.Prefix, ["stdin"]);

        Assert.Equal(CallersStdin(), result.Stdout.Trim());
    }

    [Fact]
    public void CoreAndTheShimAgreeOnWhereASessionsFilesLive()
    {
        var paths = shim.Prefixes.Session(Shim.Prefix);
        shim.Settle("session.log");

        Assert.Equal([paths.Socket], shim.Sockets);
        Assert.Contains(
            $"prefix {shim.Layout.PrefixPath(Shim.Prefix)}",
            File.ReadAllText(paths.Record));
        Assert.Contains("runner wine", File.ReadAllText(paths.Record));
    }

    [Fact]
    public async Task APluginJobIsRefusedWhileCabinetIsChangingThePrefix()
    {
        using var claim = shim.Prefixes.Claim(Shim.Prefix, "change the prefix");

        var refused = await Task.Run(() => shim.Plugin(["exit", "0"]));

        Assert.Equal(127, refused.ExitCode);
        Assert.Contains("Cabinet is changing this prefix", refused.Stderr);
        Assert.Empty(shim.Sockets);
    }

    [Fact]
    public void CabinetsOwnJobsStillRunWhileItIsChangingThePrefix()
    {
        using var claim = shim.Prefixes.Claim(Shim.Prefix, "change the prefix");

        var result = shim.Prefixes.RunJoined(Shim.Prefix, ["exit", "3"]);

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task ALivePluginJobMakesCabinetRefuseToChangeThePrefix()
    {
        var started = shim.Scratch("started");
        var release = shim.Scratch("release");
        var held = Task.Run(() => shim.Plugin(["hold", started, release]));
        Assert.True(Shim.Appears(started));

        var refused = Assert.Throws<PrefixInUseException>(
            () => shim.Prefixes.Claim(Shim.Prefix, "change the prefix"));

        File.WriteAllText(release, "");
        Assert.Equal(0, (await held).ExitCode);
        Assert.Contains("A DAW is using plugins from contract", refused.Message);
    }

    [Fact]
    public void ASessionKeepsItsOwnDiagnosticsAndTellsTheFailingJob()
    {
        shim.GiveUnusableRunner("stalled");
        var paths = shim.Prefixes.Session(Shim.Prefix);

        var result = shim.Prefixes.RunJoined(Shim.Prefix, ["exit", "0"]);

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("cannot start Wine", result.Stderr);
        Assert.Contains("cannot start Wine", File.ReadAllText(paths.Log));
    }

    private static string CallersStdin() => new FileInfo("/proc/self/fd/0").LinkTarget!;

    private static string[] Lines(string output) =>
        output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
