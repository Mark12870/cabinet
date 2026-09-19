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
    public void HelpAfterASubcommandPrintsTheUsageToo()
    {
        var outcome = cli.Run("library", "--help");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("Usage:", outcome.Out);
    }

    [Fact]
    public void AnUnknownCommandIsAUsageError()
    {
        var outcome = cli.Run("frobnicate");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: unknown command 'frobnicate' — `cabinet --help` lists them\n", outcome.Error);
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
    public void EnrolRefusesAnythingButAFlatpakIdBeforeTouchingTheDisk()
    {
        var outcome = cli.Run("enrol", "../../.ssh");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: ../../.ssh is not a Flatpak application id\n", outcome.Error);
        Assert.False(Directory.Exists(cli.Layout.PrefixesDir));
    }

    [Fact]
    public void EnrolPrintsTheOverrideAndWritesNothingOutsideCabinet()
    {
        var outcome = cli.Run("enrol", "fm.reaper.Reaper");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains(Enrolment.OverrideCommand("fm.reaper.Reaper", cli.Layout), outcome.Out);
        Assert.Contains(Enrolment.TrustBoundary("fm.reaper.Reaper"), outcome.Out);
        Assert.False(Directory.Exists(Path.Combine(cli.Layout.Home, ".var", "app", "fm.reaper.Reaper")));
    }

    [Fact]
    public void AMissingArgumentIsAUsageError()
    {
        var outcome = cli.Run("show");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected a prefix name\n", outcome.Error);
    }

    [Fact]
    public void AnUnknownOptionIsAUsageError()
    {
        var outcome = cli.Run("list", "--frob");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: unknown option '--frob'\n", outcome.Error);
    }

    [Fact]
    public void OptionsWithoutACommandDoNotOpenTheWindow()
    {
        var outcome = cli.Run("--json");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected a command — `cabinet --help` lists them\n", outcome.Error);
    }

    [Fact]
    public void JsonIsHonouredBeforeOrAfterTheCommand()
    {
        cli.Prefix("gadget");

        var trailing = cli.Run("list", "--json");
        var leading = cli.Run("--json", "list");

        Assert.Equal(0, trailing.Exit);
        Assert.Equal(trailing.Out, leading.Out);
        Assert.StartsWith("[", trailing.Out);
    }

    [Fact]
    public void JsonOnACommandWithoutAJsonFormIsAUsageError()
    {
        cli.Prefix("gadget");

        var outcome = cli.Run("show", "gadget", "--json");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: --json does not apply to `cabinet show`\n", outcome.Error);
        Assert.Empty(outcome.Out);
    }

    [Fact]
    public void WhatFollowsThePrefixOfRunIsPassedToTheCommandAsItIs()
    {
        cli.Prefix("gadget");

        cli.Run("run", "gadget", "cmd", "/c", "--json", "--", "echo");

        var ran = Assert.Single(cli.Runner.Ran, call => call.File == "cmd");
        Assert.Equal(["/c", "--json", "--", "echo"], ran.Arguments);
        Assert.True(ran.Interactive);
    }

    [Fact]
    public void ADelimiterAfterThePrefixOfRunLetsTheCommandStartWithADash()
    {
        cli.Prefix("gadget");

        cli.Run("run", "gadget", "--", "--dash", "argument");

        Assert.Equal(["argument"], Assert.Single(cli.Runner.Ran, call => call.File == "--dash").Arguments);
    }

    [Fact]
    public void WinetricksVerbsAfterADelimiterArePassedOnAsTheyAre()
    {
        cli.Prefix("gadget");
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("gadget"), "dosdevices"));

        var outcome = cli.Run("winetricks", "gadget", "--", "--force", "vcrun2019");

        Assert.Equal(0, outcome.Exit);
        Assert.Equal(
            ["--unattended", "--force", "vcrun2019"],
            Assert.Single(cli.Runner.Ran, call => call.File == Layout.Winetricks).Arguments);
    }

    [Fact]
    public void ArgumentsBeyondWhatACommandTakesAreAUsageErrorAndNothingRuns()
    {
        cli.Prefix("gadget");

        var outcome = cli.Run("use", "gadget", Layout.BundledRunner, "extra");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: unexpected 'extra' after `cabinet use`\n", outcome.Error);
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void NarrowingFlagsApplyOnlyToTheLibraryListing()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        var outcome = cli.Run("library", "show", "thing", "--installed");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: --installed does not apply to `cabinet library show`\n", outcome.Error);
    }

    [Fact]
    public void AnOptionGivenTwiceIsAUsageError()
    {
        var outcome = cli.Run("library", "--search", "a", "--search", "b");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: --search is given twice\n", outcome.Error);
    }

    [Fact]
    public void AWordAfterAWindowsIdIsTheInstallerNeverThePrefix()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        var outcome = cli.Run("library", "install", "thing", "Setup.exe");

        Assert.Equal(4, outcome.Exit);
        Assert.Equal("cabinet: no such file: Setup.exe\n", outcome.Error);
        Assert.False(Directory.Exists(cli.Layout.PrefixPath("Setup.exe")));
    }

    [Fact]
    public void AnInstallSaysWhoseTermsItAcceptsBeforeItStarts()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\nDeveloper: Vendor\n");

        var outcome = cli.Run("library", "install", "thing", "Setup.exe");

        Assert.Equal(
            "Cabinet installs Thing without showing you its licence, so installing it\n"
            + "accepts Vendor's terms on your behalf.\n\n",
            outcome.Out);
    }

    [Fact]
    public void APrefixForAWindowsInstallIsGivenByOption()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        var outcome = cli.Run("library", "install", "thing", "--prefix", "gadget");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal(
            "cabinet: Thing cannot be downloaded — pass the installer you already have: "
            + "`cabinet library install thing --prefix gadget <installer.exe>`\n",
            outcome.Error);
        Assert.Empty(cli.Runner.Calls);
    }

    [Fact]
    public void ALinuxPluginTakesNoPrefix()
    {
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");

        var outcome = cli.Run("library", "install", "synth", "--prefix", "gadget", "synth.tar.gz");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: Synth is a Linux plugin, so it takes no --prefix\n", outcome.Error);
    }

    [Fact]
    public void WhatIsNotThereExitsWithFour()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        Assert.Equal(4, cli.Run("show", "gadget").Exit);
        Assert.Equal(4, cli.Run("library", "show", "nothing").Exit);
        Assert.Equal(4, cli.Run("library", "log", "thing").Exit);
        Assert.Equal(4, cli.Run("runners", "rm", "wine-0").Exit);
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

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: --search needs something after it" + Environment.NewLine, outcome.Error);
    }

    [Fact]
    public void AnUnknownKindFails()
    {
        var outcome = cli.Run("library", "--kind", "mac");

        Assert.Equal(2, outcome.Exit);
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

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected <name>=<value>, got '=value'" + Environment.NewLine, outcome.Error);
    }

    [Fact]
    public void UsingARunnerMovesThePrefixAndUpdatesItForThatWine()
    {
        cli.Prefix("gadget");
        File.WriteAllText(cli.Layout.PrefixRunnerFile("gadget"), "wine-9.21");

        var outcome = cli.Run("use", "gadget", Layout.BundledRunner);

        Assert.Equal(0, outcome.Exit);
        Assert.Equal("gadget now runs on bundled." + Environment.NewLine, outcome.Out);
        Assert.False(File.Exists(cli.Layout.PrefixRunnerFile("gadget")));
        Assert.Equal(["-u"], Assert.Single(cli.Runner.Ran).Arguments);
    }

    [Fact]
    public void APluginYouHadToBuyIsToldWhichCommandTakesItsInstaller()
    {
        cli.Catalogue("gadget", "Name: Gadget\nKind: windows\nSource: byo\n");

        var outcome = cli.Run("library", "install", "gadget");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal(
            "cabinet: Gadget cannot be downloaded — pass the installer you already have: "
            + "`cabinet library install gadget <installer.exe>`\n",
            outcome.Error);
        Assert.Empty(cli.Runner.Calls);
    }

    [Fact]
    public void ANewPrefixCannotReuseTheNameOfOneThatIsThere()
    {
        cli.Prefix("gadget");

        var outcome = cli.Run("new", "gadget", Layout.BundledRunner);

        Assert.Equal(1, outcome.Exit);
        Assert.Equal("cabinet: a prefix named gadget is already there\n", outcome.Error);
    }

    [Fact]
    public void EveryCommandTheUsageListsIsDispatchedAndEveryDispatchedCommandIsListed()
    {
        var source = Repo.Read("src/Cabinet.Cli/Program.cs");

        Assert.Equal(string.Join(Environment.NewLine, Listed(source)), string.Join(Environment.NewLine, Dispatched(source)));
    }

    [Fact]
    public void EveryReadmeExampleIsAListedCommandOnAnEntryThatShips()
    {
        var listed = Listed(Repo.Read("src/Cabinet.Cli/Program.cs"));
        var examples = ReadmeExample().Matches(Repo.Read("README.md"))
            .Select(example => example.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length > 0)
            .ToList();
        var shipped = Directory.EnumerateDirectories(Repo.Path("data/library"))
            .Where(vendor => !Directory.EnumerateFiles(vendor, "*.md").Any())
            .SelectMany(vendor => Directory.EnumerateFiles(vendor, "*.yml"))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.Ordinal);

        var known = listed.Concat(listed.Select(command => command.Split(' ')[0])).ToHashSet(StringComparer.Ordinal);

        Assert.Subset(known, examples
            .Select(words => words is ["library" or "runners", var sub, ..] && !sub.StartsWith('-')
                ? $"{words[0]} {sub}"
                : words[0])
            .ToHashSet(StringComparer.Ordinal));
        Assert.Subset(shipped, examples
            .Where(words => words is ["library", "show" or "install" or "remove" or "launch" or "stop" or "log", _, ..])
            .Select(words => words[2])
            .ToHashSet(StringComparer.Ordinal));
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
                     ("Parse(CommandLine", ""), ("Library(CommandLine", "library "),
                     ("Runners(CommandLine", "runners "), ("Set(CommandLine", "set "),
                 })
        {
            var start = source.IndexOf("private static Func<int> " + method, StringComparison.Ordinal);
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

    [GeneratedRegex(@"^flatpak run io\.github\.mark12870\.cabinet +(.*?) *$", RegexOptions.Multiline)]
    private static partial Regex ReadmeExample();

    [GeneratedRegex(@"^\s+cabinet ?([A-Za-z<>\[\]|.= -]*?)\s{2,}", RegexOptions.Multiline)]
    private static partial Regex UsageLine();

    [GeneratedRegex(@"^\s+""([a-z-]+)""(?: or ""[a-z-]+"")? =>", RegexOptions.Multiline)]
    private static partial Regex SwitchArm();

    [GeneratedRegex(@"^\s+null =>", RegexOptions.Multiline)]
    private static partial Regex NullArm();
}
