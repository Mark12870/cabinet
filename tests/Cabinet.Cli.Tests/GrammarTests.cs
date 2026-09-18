using System.Text.RegularExpressions;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Cli.Tests;

public sealed partial class GrammarTests : IDisposable
{
    private readonly Cli cli = new();

    public void Dispose() => cli.Dispose();

    [Fact]
    public void HelpPrintsTheUsageAndSucceeds()
    {
        var outcome = cli.Run("--help");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("Usage:", outcome.Out);
        Assert.Empty(outcome.Error);
    }

    [Fact]
    public void AnUnknownCommandPrintsTheUsageToStderrAndExitsWithTwo()
    {
        var outcome = cli.Run("frobnicate");

        Assert.Equal(2, outcome.Exit);
        Assert.StartsWith("cabinet: unknown command 'frobnicate'", outcome.Error);
        Assert.Contains("Usage:", outcome.Error);
        Assert.Empty(outcome.Out);
    }

    [Fact]
    public void AnUnknownSubcommandNamesTheWholeCommand()
    {
        Assert.StartsWith("cabinet: unknown command 'library frob'", cli.Run("library", "frob").Error);
        Assert.StartsWith("cabinet: unknown command 'runners frob'", cli.Run("runners", "frob").Error);
        Assert.StartsWith("cabinet: unknown command 'set gadget frob'", cli.Run("set", "gadget", "frob").Error);
    }

    [Fact]
    public void AMissingArgumentFailsWithExitCodeOne()
    {
        var outcome = cli.Run("show");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal("cabinet: expected a prefix name" + Environment.NewLine, outcome.Error);
    }

    [Fact]
    public void JsonIsStrippedWhereverItAppears()
    {
        cli.Prefix("gadget");

        var trailing = cli.Run("list", "--json");
        var leading = cli.Run("--json", "list");

        Assert.Equal(0, trailing.Exit);
        Assert.Equal(trailing.Out, leading.Out);
        Assert.StartsWith("[", trailing.Out);
    }

    [Fact]
    public void JsonIsStrippedEvenFromWhatRunPassesToTheCommand()
    {
        cli.Prefix("gadget");

        cli.Run("run", "gadget", "cmd", "/c", "--json", "echo");

        var ran = Assert.Single(cli.Runner.Ran, call => call.File == "cmd");
        Assert.Equal(["/c", "echo"], ran.Arguments);
        Assert.True(ran.InheritStdin);
    }

    [Fact]
    public void ArgumentsBeyondWhatACommandTakesAreIgnored()
    {
        cli.Prefix("gadget");

        var plain = cli.Run("show", "gadget");
        var extra = cli.Run("show", "gadget", "sync", "fsync");

        Assert.Equal(0, extra.Exit);
        Assert.Equal(plain.Out, extra.Out);
    }

    [Fact]
    public void AWindowsInstallTakesItsSecondWordAsThePrefixNotTheInstaller()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        var outcome = cli.Run("library", "install", "thing", "Setup.exe");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal(
            "cabinet: Thing cannot be downloaded — pass the installer you already have: "
            + "`cabinet library install thing Setup.exe <installer.exe>`" + Environment.NewLine,
            outcome.Error);
    }

    [Fact]
    public void NarrowingTheLibraryByKindKeepsOnlyThatKind()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");

        var outcome = cli.Run("library", "--kind", "linux", "--json");

        Assert.Equal(["synth"], Parsed.Ids(outcome.Out));
    }

    [Fact]
    public void ANarrowingFlagWithoutItsValueFails()
    {
        var outcome = cli.Run("library", "--search");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal("cabinet: --search needs something after it" + Environment.NewLine, outcome.Error);
    }

    [Fact]
    public void AnUnknownKindFails()
    {
        var outcome = cli.Run("library", "--kind", "mac");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal("cabinet: --kind takes windows or linux, not mac" + Environment.NewLine, outcome.Error);
    }

    [Fact]
    public void AnAssignmentSetsAVariable()
    {
        cli.Prefix("gadget");

        var outcome = cli.Run("set", "gadget", "env", "KEY=value");

        Assert.Equal(0, outcome.Exit);
        Assert.Equal("KEY=value in gadget." + Environment.NewLine, outcome.Out);
        Assert.Equal("value", new PrefixSettings(cli.Layout).Variables("gadget")["KEY"]);
    }

    [Fact]
    public void AnEmptyAssignmentRemovesTheVariable()
    {
        cli.Prefix("gadget");
        cli.Run("set", "gadget", "env", "KEY=value");

        var outcome = cli.Run("set", "gadget", "env", "KEY=");

        Assert.Equal(0, outcome.Exit);
        Assert.Equal("KEY removed from gadget." + Environment.NewLine, outcome.Out);
        Assert.DoesNotContain("KEY", new PrefixSettings(cli.Layout).Variables("gadget").Keys);
    }

    [Fact]
    public void AnAssignmentWithoutAKeyFails()
    {
        cli.Prefix("gadget");

        var outcome = cli.Run("set", "gadget", "env", "=value");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal("cabinet: expected <name>=<value>, got '=value'" + Environment.NewLine, outcome.Error);
    }

    [Fact]
    public void UsingARunnerOnlyRecordsItAndLeavesWinebootToTheUser()
    {
        cli.Prefix("gadget");
        File.WriteAllText(cli.Layout.PrefixRunnerFile("gadget"), "wine-9.21");

        var outcome = cli.Run("use", "gadget", Layout.BundledRunner);

        Assert.Equal(0, outcome.Exit);
        Assert.Equal(
            "gadget now runs on bundled." + Environment.NewLine
            + "Run `cabinet run gadget wineboot -u` to update the prefix for it." + Environment.NewLine,
            outcome.Out);
        Assert.False(File.Exists(cli.Layout.PrefixRunnerFile("gadget")));
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void EveryCommandTheUsageListsIsDispatchedAndEveryDispatchedCommandIsListed()
    {
        var source = Repo.Read("src/Cabinet.Cli/Program.cs");

        Assert.Equal(string.Join(Environment.NewLine, Listed(source)), string.Join(Environment.NewLine, Dispatched(source)));
    }

    private static SortedSet<string> Listed(string source)
    {
        var usage = source[source.IndexOf("Usage:", StringComparison.Ordinal)..source.IndexOf("Options:", StringComparison.Ordinal)];
        var commands = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match line in UsageLine().Matches(usage))
        {
            var words = line.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !word.StartsWith('<') && !word.StartsWith('['))
                .Take(2);
            commands.Add(string.Join(' ', words));
        }

        commands.Remove("");
        return commands;
    }

    private static SortedSet<string> Dispatched(string source)
    {
        var commands = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (method, prefix) in new[]
                 {
                     ("Dispatch(", ""), ("Library(Layout", "library "), ("Runners(Layout", "runners "),
                     ("Set(Layout", "set "),
                 })
        {
            var start = source.IndexOf("private static int " + method, StringComparison.Ordinal);
            var body = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];

            foreach (Match arm in SwitchArm().Matches(body))
            {
                commands.Add(prefix + arm.Groups[1].Value);
            }

            foreach (Match _ in NullArm().Matches(body))
            {
                commands.Add(prefix.Trim());
            }
        }

        commands.Remove("set");
        return commands;
    }

    [GeneratedRegex(@"^\s+cabinet ?([A-Za-z<>\[\]|.= -]*?)\s{2,}", RegexOptions.Multiline)]
    private static partial Regex UsageLine();

    [GeneratedRegex(@"^\s+""([a-z-]+)""(?: or ""[a-z-]+"")? =>", RegexOptions.Multiline)]
    private static partial Regex SwitchArm();

    [GeneratedRegex(@"^\s+null =>", RegexOptions.Multiline)]
    private static partial Regex NullArm();
}
