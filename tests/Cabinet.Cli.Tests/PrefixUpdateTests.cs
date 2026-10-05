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
    public void DecliningPreviewsThePrefixAndLeavesItsSetupUnrecorded()
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

        var outcome = cli.Answer("y\n", "library", "update", "--all");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("Apply 2 prefix update(s)?", outcome.Error);
        Assert.Equal(2, cli.Runner.Ran.Count(call => call.File == Core.Layout.Winetricks));
        Assert.Contains("Every prefix setup is up to date.", cli.Run("library", "update", "--all").Out);
    }

    private void Installed(string text)
    {
        cli.Catalogue("thing", text);
        cli.Prefix("thing", "thing");
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("thing"), "dosdevices"));
    }
}
