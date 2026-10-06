using Cabinet.Core;
using Cabinet.Core.Tests;
using System.Text.Json;

namespace Cabinet.Cli.Tests;

public sealed class PrefixUpdateTests : IDisposable
{
    private const string Plugin = "Name: Thing\nKind: windows\nSource: byo\nDeveloper: Vendor\n";
    private readonly Cli cli = new();

    public void Dispose() => cli.Dispose();

    [Theory]
    [InlineData("--json")]
    [InlineData("extra")]
    [InlineData("--updates")]
    [InlineData("--all")]
    public void InvalidUpdateArgumentsFailBeforeBootstrap(string extra)
    {
        cli.Catalogue("thing", Plugin);

        var outcome = cli.Run("library", "update", "thing", extra);

        Assert.Equal(2, outcome.Exit);
        Assert.False(Directory.Exists(cli.Layout.PrefixesDir));
        Assert.Empty(cli.Runner.Calls);
    }

    [Fact]
    public void PendingUpdatesCannotBeCombinedWithNotInstalled()
    {
        var outcome = cli.Run("library", "--updates", "--not-installed");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: --updates and --not-installed exclude each other\n", outcome.Error);
        Assert.False(Directory.Exists(cli.Layout.PrefixesDir));
    }

    [Fact]
    public void DecliningPreviewsThePrefixAndLeavesItUnchanged()
    {
        Installed(Plugin + "Winetricks: corefonts\n");
        var markers = Directory.GetFiles(cli.Layout.PrefixPath("thing")).Order(StringComparer.Ordinal).ToList();

        var outcome = cli.Answer("n\n", "library", "update", "thing");

        Assert.Equal(3, outcome.Exit);
        Assert.Contains("Prefix update available", outcome.Out);
        Assert.Contains("corefonts", outcome.Out);
        Assert.Contains("Apply this setup to prefix 'thing'?", outcome.Error);
        Assert.Equal(markers, Directory.GetFiles(cli.Layout.PrefixPath("thing")).Order(StringComparer.Ordinal).ToList());
        Assert.Empty(cli.Runner.Calls);
    }

    [Fact]
    public void ConfirmingAppliesPrefixSettingsWithoutRunningAnInstaller()
    {
        Installed(Plugin + "Env: TEST_OPTION=enabled\n");

        var outcome = cli.Answer("y\n", "library", "update", "thing");
        var current = cli.Run("library", "show", "thing", "--json");
        var entry = Assert.Single(Parsed.Objects(current.Out));

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("prefix setup is up to date", outcome.Out);
        Assert.Equal("enabled", new PrefixSettings(cli.Layout).Variables("thing")["TEST_OPTION"]);
        Assert.DoesNotContain(cli.Runner.Ran, call => call.Arguments.Contains("/i"));
        Assert.DoesNotContain(cli.Runner.Ran, call => call.Arguments.Contains("start"));
        Assert.Equal("current", entry.GetProperty("prefixUpdate").GetString());
    }

    [Fact]
    public void AChangedRequirementIsListedAndShownAsAnAvailableUpdate()
    {
        Installed(Plugin);
        Assert.Equal(0, cli.Answer("y\n", "library", "update", "thing").Exit);
        cli.Catalogue("thing", Plugin + "Winetricks: corefonts\n");

        var listed = cli.Run("library");
        var shown = cli.Run("library", "show", "thing");
        var entry = Assert.Single(Parsed.Objects(cli.Run("library", "--updates", "--json").Out));

        Assert.StartsWith("up  thing", listed.Out);
        Assert.Contains("cabinet library update <id>", listed.Out);
        Assert.Contains("Prefix update available", shown.Out);
        Assert.Contains("corefonts", shown.Out);
        Assert.Contains("  Config                        , revision 1 (update available)\n", shown.Out);
        Assert.Contains("cabinet library update thing", shown.Out);
        Assert.Equal("available", entry.GetProperty("prefixUpdate").GetString());
        Assert.NotEmpty(entry.GetProperty("updateChanges").EnumerateArray());
    }

    [Fact]
    public void PendingUpdatesRespectTheOtherLibraryFilters()
    {
        Installed(Plugin + "Winetricks: corefonts\n");
        cli.Catalogue("other", "Name: Other\nKind: windows\nSource: byo\nDeveloper: Other Vendor\n");
        cli.Prefix("other", "other");
        cli.Catalogue("absent", Plugin);
        cli.Prefix("old", "retired");

        var outcome = cli.Run("library", "--updates", "--installed", "--developer", "Vendor", "--search", "Thing", "--kind", "windows", "--json");
        var entry = Assert.Single(Parsed.Objects(outcome.Out));

        Assert.Equal(0, outcome.Exit);
        Assert.Equal("thing", entry.GetProperty("id").GetString());
        Assert.Equal("available", entry.GetProperty("prefixUpdate").GetString());
    }

    [Fact]
    public void NativeAndAbsentEntriesHaveNoPrefixUpdate()
    {
        cli.Catalogue("thing", Plugin);
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");
        Directory.CreateDirectory(cli.Layout.NativePath("synth"));

        var absent = cli.Run("library", "update", "thing");
        var native = cli.Run("library", "update", "synth");
        var entries = Parsed.Objects(cli.Run("library", "--json").Out);

        Assert.Equal(4, absent.Exit);
        Assert.Equal("cabinet: Thing is not installed\n", absent.Error);
        Assert.Equal(1, native.Exit);
        Assert.Contains("has no Wine prefix to update", native.Error);
        Assert.All(entries, entry => Assert.Equal(JsonValueKind.Null, entry.GetProperty("prefixUpdate").ValueKind));
        Assert.Empty(cli.Runner.Calls);
    }

    [Fact]
    public void ACurrentPrefixDoesNotAskAgainOrRunAProcess()
    {
        Installed(Plugin);
        Assert.Equal(0, cli.Answer("y\n", "library", "update", "thing").Exit);
        var calls = cli.Runner.Calls.Count;

        var outcome = cli.Run("library", "update", "thing");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("prefix setup is up to date", outcome.Out);
        Assert.Empty(outcome.Error);
        Assert.Equal(calls, cli.Runner.Calls.Count);
    }

    [Fact]
    public void ACurrentPrefixShowsItsConfigWithoutAnUpdate()
    {
        cli.Catalogue("thing", Plugin + "Version: 1.0\n");
        cli.Prefix("thing", "thing");
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("thing"), "dosdevices"));

        var shown = cli.Run("library", "show", "thing");

        Assert.Contains("\nPrefix\n  Installed in                  thing  (`cabinet show thing`)\n"
            + "  Installed version             1.0\n", shown.Out);
        Assert.DoesNotContain("  Installed   ", shown.Out);
        Assert.Contains("  Config                        1.0, revision 1 (up to date)\n", shown.Out);
        Assert.DoesNotContain("Prefix update available", shown.Out);
    }

    [Fact]
    public void KeepingChangesLeavesAVariableTheUserChanged()
    {
        Installed(Plugin);
        var settings = new PrefixSettings(cli.Layout);
        settings.SetVariable("thing", "TEST_OPTION", "personal");
        cli.Catalogue("thing", Plugin + "Env: TEST_OPTION=enabled\n");

        var asked = cli.Answer("n\n", "library", "update", "thing");
        var kept = cli.Answer("y\n", "library", "update", "thing", "--keep-changes");

        Assert.Contains("pass --keep-changes to leave your own changes as they are", asked.Out);
        Assert.Contains("Change TEST_OPTION from personal to enabled.", asked.Out);
        Assert.Equal(0, kept.Exit);
        Assert.Equal("personal", settings.Variables("thing")["TEST_OPTION"]);
        Assert.Contains("prefix setup is up to date", cli.Run("library", "update", "thing").Out);
    }

    [Fact]
    public void ResetsOfOwnChangesAreListedInJson()
    {
        Installed(Plugin);
        new PrefixSettings(cli.Layout).SetVariable("thing", "TEST_OPTION", "personal");
        cli.Catalogue("thing", Plugin + "Env: TEST_OPTION=enabled\n");

        var entry = Assert.Single(Parsed.Objects(cli.Run("library", "show", "thing", "--json").Out));

        Assert.Empty(entry.GetProperty("updateChanges").EnumerateArray());
        Assert.Equal(["Change TEST_OPTION from personal to enabled."],
            entry.GetProperty("updateResets").EnumerateArray().Select(reset => reset.GetString()));
    }

    [Fact]
    public void AnUpdateWithoutOwnChangesNeedsNoChoice()
    {
        Installed(Plugin + "Env: TEST_OPTION=enabled\n");

        var outcome = cli.Answer("n\n", "library", "update", "thing");

        Assert.DoesNotContain("--keep-changes", outcome.Out);
        Assert.DoesNotContain("override your own changes", outcome.Out);
    }

    [Fact]
    public void UpdatingAllCanKeepOwnChanges()
    {
        Installed(Plugin);
        var settings = new PrefixSettings(cli.Layout);
        settings.SetVariable("thing", "TEST_OPTION", "personal");
        cli.Catalogue("thing", Plugin + "Env:\n  TEST_OPTION=enabled\n  ADDED=new\n");

        var outcome = cli.Answer("y\n", "library", "update", "--all", "--keep-changes");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("--keep-changes leaves your own changes as they are.", outcome.Out);
        Assert.Equal("personal", settings.Variables("thing")["TEST_OPTION"]);
        Assert.Equal("new", settings.Variables("thing")["ADDED"]);
    }

    [Fact]
    public void ANativePluginShowsNoPrefixSection()
    {
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");
        Directory.CreateDirectory(cli.Layout.NativePath("synth"));

        Assert.DoesNotContain("\nPrefix\n", cli.Run("library", "show", "synth").Out);
    }

    [Fact]
    public void AnEditedPrefixShowsWhatDiffersFromItsConfig()
    {
        Installed(Plugin + "Env: TEST_OPTION=enabled\n");
        File.WriteAllText(cli.Layout.PrefixSetupFile("thing"),
            "{\"version\":2,\"configVersion\":\"\",\"revision\":1,\"software\":null,\"runner\":null,"
            + "\"dxvk\":false,\"sync\":\"system\",\"winetricks\":[],\"env\":{\"TEST_OPTION\":\"enabled\"},\"desktop\":false}");
        new PrefixSettings(cli.Layout).SetVariable("thing", "TEST_OPTION", "personal");

        var shown = cli.Run("library", "show", "thing");
        var entry = Assert.Single(Parsed.Objects(cli.Run("library", "--json").Out));

        Assert.Contains("  Config                        , revision 1 (edited)\n", shown.Out);
        Assert.Contains("  Differs from the config       TEST_OPTION personal instead of enabled.\n", shown.Out);
        Assert.Equal("current", entry.GetProperty("prefixUpdate").GetString());
        Assert.Equal(["TEST_OPTION personal instead of enabled."],
            entry.GetProperty("prefixEdits").EnumerateArray().Select(edit => edit.GetString()));
    }

    [Fact]
    public void AnUpdateRefusesAPrefixInUseByADaw()
    {
        Installed(Plugin + "Winetricks: corefonts\n");
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(cli.Layout, "thing").Busy);

        var outcome = cli.Answer("y\n", "library", "update", "thing");

        Assert.Equal(1, outcome.Exit);
        Assert.Contains("DAW", outcome.Error);
        Assert.Equal("available", Assert.Single(Parsed.Objects(cli.Run("library", "--json").Out))
            .GetProperty("prefixUpdate").GetString());
    }

    [Fact]
    public void UpdatingNeedsAPluginIdOrAll()
    {
        var outcome = cli.Run("library", "update");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected a plugin id or --all\n", outcome.Error);
    }

    [Fact]
    public void UpdatingAllAppliesEveryPendingPrefixAfterOneQuestion()
    {
        Installed(Plugin + "Winetricks: corefonts\n");
        cli.Catalogue("other", "Name: Other\nKind: windows\nSource: byo\nWinetricks: vcrun2022\n");
        cli.Prefix("other", "other");
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("other"), "dosdevices"));
        RecordedBareSetup("other");

        var outcome = cli.Answer("y\n", "library", "update", "--all");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("Apply 2 prefix update(s)?", outcome.Error);
        Assert.Equal(2, cli.Runner.Ran.Count(call => call.File == Core.Layout.Winetricks));
        File.WriteAllText(cli.Layout.PrefixWinetricksLog("thing"), "corefonts\n");
        File.WriteAllText(cli.Layout.PrefixWinetricksLog("other"), "vcrun2022\n");
        Assert.Contains("Every prefix setup is up to date.", cli.Run("library", "update", "--all").Out);
    }

    private void Installed(string text)
    {
        cli.Catalogue("thing", text);
        cli.Prefix("thing", "thing");
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("thing"), "dosdevices"));
        RecordedBareSetup("thing");
    }

    private void RecordedBareSetup(string prefix) =>
        File.WriteAllText(cli.Layout.PrefixSetupFile(prefix),
            "{\"version\":2,\"configVersion\":\"\",\"revision\":1,\"software\":null,\"runner\":null,"
            + "\"dxvk\":false,\"sync\":\"system\",\"winetricks\":[],\"env\":{},\"desktop\":false}");
}
