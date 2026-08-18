using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class ProcessRunnerTests
{
    private static readonly ProcessRunner Subject = new();

    [Fact]
    public void OutputIsStreamedLineByLineAsWellAsCollected()
    {
        var streamed = new List<string>();

        var result = Subject.Run(
            "sh", ["-c", "echo one; echo two; echo three"], onOutput: streamed.Add);

        Assert.Equal(["one", "two", "three"], streamed);
        Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine
                     + "three" + Environment.NewLine, result.Stdout);
        Assert.True(result.Ok);
    }

    [Fact]
    public void StderrIsStreamedToo()
    {
        var streamed = new List<string>();

        var result = Subject.Run("sh", ["-c", "echo boom >&2"], onOutput: streamed.Add);

        Assert.Equal(["boom"], streamed);
        Assert.Contains("boom", result.Stderr);
    }

    [Fact]
    public void TheExitCodeSurvivesStreaming()
    {
        var result = Subject.Run("sh", ["-c", "echo out; exit 3"], onOutput: _ => { });

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Ok);
    }

    [Fact]
    public void OutputIsStillCollectedWithNoSink()
    {
        var result = Subject.Run("sh", ["-c", "echo quiet"]);

        Assert.Contains("quiet", result.Stdout);
    }

    [Fact]
    public void TheEnvironmentReachesTheChild()
    {
        var result = Subject.Run(
            "sh", ["-c", "echo $CABINET_TEST"],
            new Dictionary<string, string> { ["CABINET_TEST"] = "carried" });

        Assert.Contains("carried", result.Stdout);
    }

    [Fact]
    public void AnEmptyValueUnsetsTheVariableRatherThanBlankingIt()
    {
        var result = Subject.Run(
            "sh", ["-c", "if [ -n \"${CABINET_GONE+set}\" ]; then echo set; else echo unset; fi"],
            new Dictionary<string, string> { ["CABINET_GONE"] = "" });

        Assert.Contains("unset", result.Stdout);
    }
}
