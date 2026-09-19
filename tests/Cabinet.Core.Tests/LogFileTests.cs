using System.Text;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class LogFileTests : IDisposable
{
    private const int FourMegabytes = 4 * 1024 * 1024;

    private readonly string root = TestRoot.Create("log-file");

    private string Log => Path.Combine(root, "runtime.log");

    [Fact]
    public void ReadingAnActiveLogKeepsItsRecentCompleteLinesWithoutRewritingIt()
    {
        using var writer = new FileStream(
            Log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(Encoding.UTF8.GetBytes(new string('x', FourMegabytes) + "\nlast line\n"));
        writer.Flush();
        var before = File.ReadAllBytes(Log);

        Assert.Equal("last line\n", LogFile.Read(Log));
        Assert.Equal(before, File.ReadAllBytes(Log));
    }

    [Fact]
    public void AnOversizedLogIsSetAsideWholeRatherThanCut()
    {
        var content = new string('x', FourMegabytes) + "\nlast line\n";
        File.WriteAllText(Log, content);

        LogFile.Rotate(Log);

        Assert.False(File.Exists(Log));
        Assert.Equal(content, File.ReadAllText(Log + ".1"));
    }

    [Fact]
    public void AWriterStillHoldingARotatedLogKeepsWritingIntoIt()
    {
        File.WriteAllText(Log, new string('x', FourMegabytes + 1));
        using var writer = new FileStream(
            Log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        LogFile.Rotate(Log);
        writer.Write("late\n"u8);
        writer.Flush();

        Assert.EndsWith("late\n", File.ReadAllText(Log + ".1"));
    }

    [Fact]
    public void ALogWithinTheLimitIsLeftWhereItIs()
    {
        File.WriteAllText(Log, "short\n");

        LogFile.Rotate(Log);

        Assert.Equal("short\n", File.ReadAllText(Log));
        Assert.False(File.Exists(Log + ".1"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
