using System.Text.Json;
using Cabinet.Ci;

namespace Cabinet.Core.Tests;

public sealed class CatalogueUpdateCoverageTests
{
    [Fact]
    public void RepositoryCatalogueHasCompleteCoverage()
    {
        var statuses = CatalogueUpdateCoverage.Read(Repo.Root, Repo.Path("data/catalogue-updates.json"));

        Assert.NotEmpty(statuses);
        Assert.All(statuses, status => Assert.Contains(status.Discovery, new[] { "enabled", "blocked", "rolling", "byo" }));
    }

    [Fact]
    public void UnclassifiedPinnedDownloadFails()
    {
        using var fixture = new CoverageFixture();
        fixture.AddEntry("unclassified", "download", "1.2.3", "VST3");
        fixture.SetConfiguration();

        var error = Assert.Throws<InvalidDataException>(fixture.Read);

        Assert.Contains("needs a metadata rule or an explicit block", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRulesAndRuleBlockConflictsFail()
    {
        using var fixture = new CoverageFixture();
        fixture.AddEntry("configured", "download", "1.2.3", "VST3");
        var rule = fixture.Rule("configured", "configured/configured.yml");
        fixture.SetConfiguration([rule, rule]);

        Assert.Throws<InvalidDataException>(fixture.Read);

        fixture.SetConfiguration([rule], [new { id = "configured", reason = "no supported metadata source" }]);

        Assert.Throws<InvalidDataException>(fixture.Read);
    }

    [Fact]
    public void RollingAndByoEntriesNeedNoConfiguration()
    {
        using var fixture = new CoverageFixture();
        fixture.AddEntry("rolling-item", "rolling", "1.2.3", "VST3");
        fixture.AddEntry("byo-item", "byo", "1.2.3", "VST3");
        fixture.SetConfiguration();

        var statuses = fixture.Read().ToDictionary(status => status.Id, StringComparer.Ordinal);

        Assert.Equal("rolling", statuses["rolling-item"].Discovery);
        Assert.Equal("byo", statuses["byo-item"].Discovery);
    }

    [Fact]
    public void ClapAndMissingScenarioBlockAutomationSeparately()
    {
        using var fixture = new CoverageFixture();
        fixture.AddEntry("clap-item", "download", "1.2.3", "CLAP");
        fixture.AddEntry("no-scenario", "download", "1.2.3", "VST3");
        fixture.AddScenario("ClapScenario", "clap-item", "CLAP");
        fixture.SetConfiguration(
            [fixture.Rule("clap-item", "clap-item/clap-item.yml"),
             fixture.Rule("no-scenario", "no-scenario/no-scenario.yml")]);

        var statuses = fixture.Read().ToDictionary(status => status.Id, StringComparer.Ordinal);

        Assert.Equal("Cabinet.Runtime.Tests.Scenarios.ClapScenario", statuses["clap-item"].Scenario);
        Assert.Contains("excluded runtime format: CLAP", statuses["clap-item"].AutomationBlockers);
        Assert.Equal("", statuses["no-scenario"].Scenario);
        Assert.Contains("runtime scenario missing", statuses["no-scenario"].AutomationBlockers);
    }

    [Fact]
    public void UnsupportedBaselineVersionBlocksAutomationButNotDiscovery()
    {
        using var fixture = new CoverageFixture();
        fixture.AddEntry("alpha", "download", "3.0 beta", "VST3");
        fixture.AddScenario("AlphaScenario", "alpha", "VST3");
        fixture.SetConfiguration([fixture.Rule("alpha", "alpha/alpha.yml")]);

        var status = Assert.Single(fixture.Read());

        Assert.Equal("enabled", status.Discovery);
        Assert.Contains("unsupported-version", status.AutomationBlockers);
        Assert.False(status.AutomaticEligible);
    }

    [Fact]
    public void ADeclaredSupportedFormatMissingFromTheScenarioBlocksAutomation()
    {
        using var fixture = new CoverageFixture();
        fixture.SetSupportedFormats("VST3", "VST2");
        fixture.AddEntry("plugin", "download", "1.2.3", "VST3, VST2");
        fixture.AddScenario("PluginScenario", "plugin", "VST3");
        fixture.SetConfiguration([fixture.Rule("plugin", "plugin/plugin.yml")]);

        var status = Assert.Single(fixture.Read());

        Assert.Contains("scenario does not bridge format: VST2", status.AutomationBlockers);
        Assert.False(status.AutomaticEligible);
    }

    [Fact]
    public void CoverageWriterIncludesCountsAndPerEntryFailures()
    {
        using var fixture = new CoverageFixture();
        var status = new CatalogueUpdateStatus("alpha", "alpha/alpha.yml", "1.2.3", "download", "", "enabled",
            "verified metadata source configured", ["unsupported-version"])
        {
            Outcome = "failed",
            Error = "upstream unavailable"
        };

        CatalogueUpdateCoverage.Write(fixture.Output, [status]);

        var json = File.ReadAllText(Path.Combine(fixture.Output, "coverage.json"));
        var markdown = File.ReadAllText(Path.Combine(fixture.Output, "coverage.md"));
        Assert.Contains("\"discoveryEnabled\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"automaticEligible\": 0", json, StringComparison.Ordinal);
        Assert.Contains("unsupported-version", markdown, StringComparison.Ordinal);
        Assert.Contains("upstream unavailable", markdown, StringComparison.Ordinal);
    }

    private sealed class CoverageFixture : IDisposable
    {
        public CoverageFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"cabinet-coverage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "data", "library"));
            var scenarios = Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios");
            Directory.CreateDirectory(scenarios);
            File.WriteAllText(Path.Combine(scenarios, "ScenarioHarness.cs"), """
                private static readonly Dictionary<string, (string Extension, string Carla)> Formats = new()
                {
                    ["VST3"] = (".vst3", "vst3"),
                };
                """);
            File.WriteAllText(Path.Combine(scenarios, "InstalledEntry.cs"), """
                public static TheoryData<string> Formats(string id) => [.. Find(id).Formats.Where(format => format != "CLAP")];
                """);
        }

        public string Root { get; }
        public string Configuration => Path.Combine(Root, "catalogue-updates.json");
        public string Output => Path.Combine(Root, "output");

        public void SetSupportedFormats(params string[] formats)
        {
            var members = string.Join(", ", formats.Select(format => $"[\"{format}\"] = (\".plugin\", \"plugin\")"));
            File.WriteAllText(Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios", "ScenarioHarness.cs"),
                "private static readonly Dictionary<string, (string Extension, string Carla)> Formats = new() { " + members + " };");
        }

        public void AddEntry(string id, string source, string version, string formats)
        {
            var vendor = Path.Combine(Root, "data", "library", id);
            Directory.CreateDirectory(vendor);
            File.WriteAllText(Path.Combine(vendor, id + ".yml"),
                $"Name: {id}\nVersion: {version}\nFormats: {formats}\nSource: {source}\n");
        }

        public object Rule(string id, string path) => new
        {
            id,
            path,
            allowedDownloadHosts = new[] { "example.test" },
            source = new
            {
                type = "github",
                repository = "vendor/project",
                versionRegex = "^(?<version>[0-9.]+)$",
                assetRegex = "^project.zip$"
            }
        };

        public void AddScenario(string name, string id, string format)
        {
            File.WriteAllText(Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios", name + ".cs"),
                $$"""
                namespace Cabinet.Runtime.Tests.Scenarios;
                public sealed class {{name}}
                {
                    private const string Id = "{{id}}";
                    private static readonly Dictionary<string, string> Bridges = new()
                    {
                        ["{{format}}"] = "Plugin.vst3",
                    };
                }
                """);
        }

        public void SetConfiguration(object[]? configured = null, object[]? blocked = null) =>
            File.WriteAllText(Configuration, JsonSerializer.Serialize(new
            {
                entries = configured ?? [],
                blocked = blocked ?? []
            }));

        public IReadOnlyList<CatalogueUpdateStatus> Read() => CatalogueUpdateCoverage.Read(Root, Configuration);

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
