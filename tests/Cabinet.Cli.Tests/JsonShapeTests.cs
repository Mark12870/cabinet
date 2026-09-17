using System.Text.Json;

namespace Cabinet.Cli.Tests;

public sealed class JsonShapeTests : IDisposable
{
    private static readonly string[] EntryKeys =
    [
        "id", "name", "kind", "category", "summary", "homepage", "source", "demo", "account",
        "prefix", "runner", "dxvk", "sync", "winetricks", "desktop", "env", "script", "manager",
        "launchService", "launchHelper", "launchArgs", "scheme", "data", "developer", "version",
        "licence", "licensing", "formats", "description", "installed", "installedIn",
    ];

    private readonly Cli cli = new();

    public void Dispose() => cli.Dispose();

    [Fact]
    public void AListedPrefixCarriesItsSettings()
    {
        cli.Prefix("gadget");

        var prefix = Assert.Single(Parsed.Objects(cli.Run("list", "--json").Out));

        Assert.Equal(
            ["name", "path", "initialised", "runner", "dxvk", "sync", "desktop"],
            Parsed.Keys(prefix));
        Assert.Equal(JsonValueKind.False, prefix.GetProperty("initialised").ValueKind);
        Assert.Equal(JsonValueKind.Null, prefix.GetProperty("dxvk").ValueKind);
    }

    [Fact]
    public void ALibraryEntryCarriesEveryCatalogueField()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        var entry = Assert.Single(Parsed.Objects(cli.Run("library", "--json").Out));

        Assert.Equal(EntryKeys, Parsed.Keys(entry));
    }

    [Fact]
    public void ShowingOneEntryUsesTheLibraryShapeInAnArray()
    {
        cli.Catalogue("thing", "Name: Thing\nKind: windows\nSource: byo\n");

        var entry = Assert.Single(Parsed.Objects(cli.Run("library", "show", "thing", "--json").Out));

        Assert.Equal(EntryKeys, Parsed.Keys(entry));
    }

    [Fact]
    public void ANativeEntryHasNoPrefix()
    {
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");

        var entry = Assert.Single(Parsed.Objects(cli.Run("library", "--json").Out));

        Assert.Equal(JsonValueKind.Null, entry.GetProperty("prefix").ValueKind);
    }

    [Fact]
    public void EveryDoctorCheckHasANameAStatusAndADetail()
    {
        var checks = Parsed.Objects(cli.Run("doctor", "--json").Out);

        Assert.NotEmpty(checks);
        Assert.All(checks, check => Assert.Equal(["name", "status", "detail"], Parsed.Keys(check)));
        Assert.All(checks, check =>
            Assert.Contains(check.GetProperty("status").GetString(), new[] { "ok", "warn", "fail" }));
    }

    [Fact]
    public void AboutDescribesTheBuildInOneObject()
    {
        var about = JsonDocument.Parse(cli.Run("about", "--json").Out).RootElement;

        Assert.Equal(
            ["version", "remote", "url", "commit", "origin", "yabridge", "wine", "homepage", "bugtracker"],
            Parsed.Keys(about));
    }
}
