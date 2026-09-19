using Cabinet.Core.Tests;

namespace Cabinet.Cli.Tests;

public sealed class ExitStatusTests
{
    [Fact]
    public void RunExitsWithItsCommandsOwnStatus()
    {
        using var cli = new Cli(new RecordingRunner(exits: args => args is ["/c", "exit"] ? 7 : 0));
        cli.Prefix("gadget");

        var outcome = cli.Run("run", "gadget", "cmd", "/c", "exit");

        Assert.Equal(7, outcome.Exit);
    }

    [Fact]
    public void AFailingInstallerIsNamedAndExitsWithOne()
    {
        using var cli = new Cli(new RecordingRunner(
            exits: args => args.Any(arg => arg.EndsWith("Setup.exe", StringComparison.Ordinal)) ? 3010 : 0));
        cli.Prefix("gadget");
        var installer = Path.Combine(cli.Layout.TempDir, "Setup.exe");
        Directory.CreateDirectory(cli.Layout.TempDir);
        File.WriteAllText(installer, "");

        var outcome = cli.Run("install", "gadget", installer);

        Assert.Equal(1, outcome.Exit);
        Assert.EndsWith("cabinet: Setup.exe exited with 3010\n", outcome.Error);
    }
}
