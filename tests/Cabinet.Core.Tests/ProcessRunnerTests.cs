using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class ProcessRunnerTests : IDisposable
{
    private static readonly ProcessRunner Subject = new();
    private readonly string root = TestRoot.Create("process-runner");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void OutputIsStreamedLineByLineAsWellAsCollected()
    {
        var streamed = new List<string>();

        var result = Subject.Run(
            "sh", ["-c", "echo one; echo two; echo three"], onOutput: streamed.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two", "three"], streamed);
        Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine
                     + "three" + Environment.NewLine, result.Stdout);
        Assert.True(result.Ok);
    }

    [Fact]
    public void StderrIsStreamedToo()
    {
        var streamed = new List<string>();

        var result = Subject.Run(
            "sh", ["-c", "echo boom >&2"], onOutput: streamed.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["boom"], streamed);
        Assert.Contains("boom", result.Stderr);
    }

    [Fact]
    public void TheExitCodeSurvivesStreaming()
    {
        var result = Subject.Run(
            "sh", ["-c", "echo out; exit 3"], onOutput: _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Ok);
    }

    [Fact]
    public void OutputIsStillCollectedWithNoSink()
    {
        var result = Subject.Run("sh", ["-c", "echo quiet"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("quiet", result.Stdout);
    }

    [Fact]
    public void TheEnvironmentReachesTheChild()
    {
        var result = Subject.Run(
            "sh", ["-c", "echo $CABINET_TEST"],
            new Dictionary<string, string> { ["CABINET_TEST"] = "carried" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("carried", result.Stdout);
    }

    [Fact]
    public void AnEmptyValueUnsetsTheVariableRatherThanBlankingIt()
    {
        var result = Subject.Run(
            "sh", ["-c", "printenv CABINET_GONE"],
            new Dictionary<string, string> { ["CABINET_GONE"] = "" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public void AnExplicitBlankValueRemainsPresent()
    {
        var result = Subject.Run(
            "sh",
            ["-c", "test \"${CABINET_BLANK+x}\" = x && test -z \"$CABINET_BLANK\""],
            blankEnvironment: new HashSet<string> { "CABINET_BLANK" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
    }

    [Fact]
    public void ACapturedChildReadsEndOfFileFromStdin()
    {
        var result = Subject.Run(
            "sh", ["-c", "if read value; then echo read; else echo eof; fi"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("eof" + Environment.NewLine, result.Stdout);
    }

    [Fact]
    public void AnInteractiveChildWritesToTheCallersOwnStreams()
    {
        var result = Subject.Run(
            "sh",
            ["-c", "[ /proc/$$/fd/1 -ef /proc/$PPID/fd/1 ] && [ /proc/$$/fd/2 -ef /proc/$PPID/fd/2 ]"],
            interactive: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("", result.Stdout);
    }

    [Fact]
    public void ACapturedChildDoesNotWriteToTheCallersOwnStreams()
    {
        var result = Subject.Run(
            "sh", ["-c", "[ /proc/$$/fd/1 -ef /proc/$PPID/fd/1 ]"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
    }

    [Fact]
    public void OutputWithoutATrailingNewlineIsCollectedAndStreamed()
    {
        var streamed = new List<string>();

        var result = Subject.Run(
            "sh", ["-c", "printf fragment"], onOutput: streamed.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("fragment", result.Stdout);
        Assert.Equal(["fragment"], streamed);
    }

    [Fact]
    public void AMeterDrawnWithCarriageReturnsArrivesAsOneLinePerUpdate()
    {
        var lines = new List<string>();

        Subject.Run(
            "sh",
            ["-c", @"printf '\r###  10.0%%\r##### 50.0%%\r####100.0%%\n' >&2"],
            onOutput: lines.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["", "###  10.0%", "##### 50.0%", "####100.0%"], lines);
    }

    [Fact]
    public void AChildWritingToALogNeverInheritsTheStdioItWasStartedWith()
    {
        var log = Path.Combine(root, "child-stdio.log");

        Subject.Run(
            "sh", ["-c", "readlink /proc/self/fd/1"], logTo: log,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(log, File.ReadAllText(log).Trim());

        File.Delete(log);
    }

    [Fact]
    public void ALoggedRunCollectsNothingButStillCarriesItsExitCode()
    {
        var log = Path.Combine(root, "logged-run.log");

        var result = Subject.Run(
            "sh", ["-c", "echo out; echo boom >&2; exit 3"], logTo: log,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        Assert.Equal(["out", "boom"], File.ReadAllLines(log));

        File.Delete(log);
    }

    [Fact]
    public void ALoggedRunAppendsToWhatIsAlreadyInTheLog()
    {
        var log = Path.Combine(root, "append.log");
        File.WriteAllText(log, "opening" + Environment.NewLine);

        Subject.Run("sh", ["-c", "echo printed"], logTo: log, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["opening", "printed"], File.ReadAllLines(log));

        File.Delete(log);
    }

    [Fact]
    public void TheEnvironmentReachesAChildWritingToALogToo()
    {
        var log = Path.Combine(root, "logged-environment.log");

        Subject.Run(
            "sh", ["-c", "echo $CABINET_TEST"],
            new Dictionary<string, string> { ["CABINET_TEST"] = "carried" },
            logTo: log,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("carried", File.ReadAllText(log).Trim());

        File.Delete(log);
    }

    [Fact]
    public async Task CancellingACapturedRunKillsItsProcessTree()
    {
        var started = Path.Combine(root, "captured-started");
        var child = Path.Combine(root, "captured-child");
        using var cancelled = new CancellationTokenSource();
        var running = Task.Run(() => Subject.Run(
            "sh",
            [
                "-c",
                "touch \"$1\"; sleep 30 & child=$!; "
                + "echo \"$child\" > \"$2\"; printf 'ready\\n'; wait",
                "sh",
                started,
                child,
            ],
            cancellationToken: cancelled.Token));

        await WaitForFile(child);
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var childPid = int.Parse(await File.ReadAllTextAsync(child, TestContext.Current.CancellationToken));
        await WaitForProcessExit(childPid);

        Assert.False(Directory.Exists($"/proc/{childPid}"));
    }

    [Fact]
    public async Task CancellingALoggedRunKillsItsProcessTree()
    {
        var child = Path.Combine(root, "logged-child");
        var log = Path.Combine(root, "cancelled.log");
        using var cancelled = new CancellationTokenSource();
        var running = Task.Run(() => Subject.Run(
            "sh",
            [
                "-c",
                "sleep 30 & child=$!; echo \"$child\" > \"$1\"; echo ready; wait",
                "sh",
                child,
            ],
            logTo: log,
            cancellationToken: cancelled.Token));

        await WaitForFile(child);
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var childPid = int.Parse(await File.ReadAllTextAsync(child, TestContext.Current.CancellationToken));
        await WaitForProcessExit(childPid);

        Assert.Contains("ready", File.ReadAllText(log));
        Assert.False(Directory.Exists($"/proc/{childPid}"));
    }

    [Fact]
    public async Task AProgramLeftHoldingTheOutputIsNamedAndCanBeStopped()
    {
        var streamed = new List<string>();
        var watched = new List<Lingering?>();
        Lingering.Watcher = found =>
        {
            watched.Add(found);
            found?.Stop();
        };

        try
        {
            var result = await Task.Run(() => Subject.Run("sh", ["-c", "sleep 60 & exit 0"], onOutput: streamed.Add))
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

            Assert.True(result.Ok);
            Assert.Equal(["sleep"], watched[0]!.Programs);
            Assert.Null(watched[1]);
            Assert.Contains("sh has finished, but it started sleep; waiting for that to close.", streamed);
        }
        finally
        {
            Lingering.Watcher = null;
        }
    }

    [Fact]
    public void AProgramLeftHoldingTheOutputIsWaitedForUntilItCloses()
    {
        var watched = new List<Lingering?>();
        Lingering.Watcher = watched.Add;

        try
        {
            var result = Subject.Run(
                "sh", ["-c", "(sleep 4; echo late) & exit 0"], onOutput: _ => { },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Ok);
            Assert.Contains("late", result.Stdout);
            Assert.Equal(2, watched.Count);
        }
        finally
        {
            Lingering.Watcher = null;
        }
    }

    [Fact]
    public async Task CancellingStopsAProgramLeftHoldingTheOutput()
    {
        using var cancelled = new CancellationTokenSource();
        var running = Task.Run(() => Subject.Run(
            "sh", ["-c", "sleep 60 & exit 0"], onOutput: _ => { }, cancellationToken: cancelled.Token));

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    private static async Task WaitForFile(string path)
    {
        for (var attempt = 0; attempt < 500 && !File.Exists(path); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(File.Exists(path), $"timed out waiting for {path}");
    }

    private static async Task WaitForProcessExit(int processId)
    {
        for (var attempt = 0; attempt < 500 && Directory.Exists($"/proc/{processId}"); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
