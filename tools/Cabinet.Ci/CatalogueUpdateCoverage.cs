using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cabinet.Ci;

internal static class CatalogueUpdateCoverage
{
    private const string ScenarioNamespace = "Cabinet.Runtime.Tests.Scenarios.";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<CatalogueUpdateStatus> Read(string repository, string configuration)
    {
        var library = Path.Combine(repository, "data", "library");
        var entries = ReadCatalogue(library);
        var rules = ReadConfiguration(configuration);
        var byId = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var configured = new Dictionary<string, UpdateRule>(StringComparer.Ordinal);
        var blocked = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rule in rules.Entries ?? [])
        {
            if (!byId.TryGetValue(rule.Id, out var entry))
            {
                throw new InvalidDataException($"catalogue update ID '{rule.Id}' is not shipped");
            }

            if (!string.Equals(rule.Path, entry.Path, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"catalogue update path for '{rule.Id}' does not match the library entry");
            }

            if (entry.Source != "download")
            {
                throw new InvalidDataException($"catalogue update rule '{rule.Id}' is not a pinned download entry");
            }

            ValidateMetadataSource(rule);
            if (!configured.TryAdd(rule.Id, rule))
            {
                throw new InvalidDataException($"catalogue update ID '{rule.Id}' is configured more than once");
            }
        }

        foreach (var item in rules.Blocked ?? [])
        {
            if (!byId.TryGetValue(item.Id, out var entry))
            {
                throw new InvalidDataException($"catalogue update blocked ID '{item.Id}' is not shipped");
            }

            if (entry.Source != "download")
            {
                throw new InvalidDataException($"catalogue update block '{item.Id}' is not a pinned download entry");
            }

            if (string.IsNullOrWhiteSpace(item.Reason))
            {
                throw new InvalidDataException($"catalogue update block '{item.Id}' has no reason");
            }

            if (configured.ContainsKey(item.Id) || !blocked.TryAdd(item.Id, item.Reason.Trim()))
            {
                throw new InvalidDataException($"catalogue update ID '{item.Id}' is configured and blocked more than once");
            }
        }

        var scenarioMap = ReadScenarioMap(repository);
        var fixtures = ReadFixtures(repository);
        var statuses = new List<CatalogueUpdateStatus>(entries.Count);
        foreach (var entry in entries)
        {
            var scenario = scenarioMap.TryGetValue(entry.Id, out var mapped) && mapped.Length == 1
                ? ScenarioNamespace + mapped[0]
                : "";
            var discovery = entry.Source switch
            {
                "download" when configured.ContainsKey(entry.Id) => "enabled",
                "download" when blocked.ContainsKey(entry.Id) => "blocked",
                "download" => throw new InvalidDataException(
                    $"pinned download entry '{entry.Id}' needs a metadata rule or an explicit block"),
                "rolling" => "rolling",
                "byo" => "byo",
                _ => throw new InvalidDataException($"catalogue entry '{entry.Id}' has unsupported source '{entry.Source}'")
            };
            var reason = discovery switch
            {
                "enabled" => "verified metadata source configured",
                "blocked" => blocked[entry.Id],
                "rolling" => "rolling source has no pinned version",
                "byo" => "version is supplied by the user",
                _ => ""
            };
            var blockers = AutomationBlockers(repository, entry, scenarioMap, scenario, fixtures);
            statuses.Add(new CatalogueUpdateStatus(entry.Id, entry.Path, entry.Version, entry.Source,
                scenario, discovery, reason, blockers));
        }

        return statuses;
    }

    public static void Write(string outputDirectory, IReadOnlyList<CatalogueUpdateStatus> statuses)
    {
        Directory.CreateDirectory(outputDirectory);
        var ordered = statuses.OrderBy(status => status.Id, StringComparer.Ordinal).ToArray();
        var discoveryEnabled = ordered.Count(status => status.Discovery == "enabled");
        var automaticEligible = ordered.Count(status => status.AutomaticEligible);
        var json = new
        {
            discoveryEnabled,
            automaticEligible,
            entries = ordered.Select(status => new
            {
                id = status.Id,
                path = status.Path,
                version = status.Version,
                source = status.Source,
                scenario = status.Scenario,
                discovery = status.Discovery,
                reason = status.Reason,
                automationBlockers = status.AutomationBlockers,
                outcome = status.Outcome,
                availableVersion = status.AvailableVersion,
                error = status.Error
            })
        };
        File.WriteAllText(Path.Combine(outputDirectory, "coverage.json"),
            JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));

        var markdown = new List<string>
        {
            "# Catalogue update coverage",
            "",
            $"Discovery enabled: {discoveryEnabled}; automatic eligible: {automaticEligible}.",
            "",
            "| ID | Source | Discovery | Outcome | Available version | Blockers / error |",
            "| --- | --- | --- | --- | --- | --- |"
        };
        markdown.AddRange(ordered.Select(status =>
        {
            var details = status.AutomationBlockers.ToList();
            if (!string.IsNullOrWhiteSpace(status.Error))
            {
                details.Add(status.Error);
            }

            return string.Join(" | ",
                "| " + Markdown(status.Id),
                Markdown(status.Source),
                Markdown(status.Discovery + (status.Reason.Length == 0 ? "" : ": " + status.Reason)),
                Markdown(status.Outcome),
                Markdown(status.AvailableVersion ?? ""),
                Markdown(string.Join("; ", details))) + " |";
        }));
        File.WriteAllText(Path.Combine(outputDirectory, "coverage.md"), string.Join(Environment.NewLine, markdown)
            + Environment.NewLine);
    }

    private static List<CatalogueEntry> ReadCatalogue(string library)
    {
        if (!Directory.Exists(library))
        {
            throw new DirectoryNotFoundException($"catalogue library not found: {library}");
        }

        var entries = new List<CatalogueEntry>();
        foreach (var vendor in Directory.EnumerateDirectories(library).Order(StringComparer.Ordinal))
        {
            if (Directory.EnumerateFiles(vendor, "*.md").Any())
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(vendor, "*.yml").Order(StringComparer.Ordinal))
            {
                var filename = Path.GetFileName(path);
                if (filename.StartsWith("prefix-", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(library, path).Replace(Path.DirectorySeparatorChar, '/');
                var text = File.ReadAllText(path);
                entries.Add(new(Path.GetFileNameWithoutExtension(path), relative,
                    Field(text, "Version"), Field(text, "Source"), Field(text, "Formats")));
            }
        }

        return entries;
    }

    private static UpdateConfiguration ReadConfiguration(string path)
    {
        var configuration = JsonSerializer.Deserialize<UpdateConfiguration>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("catalogue update configuration is empty");
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in configuration.Entries ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Path)
                || !entryIds.Add(entry.Id))
            {
                throw new InvalidDataException("catalogue update IDs must be unique and have paths");
            }
        }

        var blockedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in configuration.Blocked ?? [])
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || !blockedIds.Add(item.Id))
            {
                throw new InvalidDataException("catalogue update blocked IDs must be unique");
            }
        }

        return new(configuration.Entries ?? [], configuration.Blocked ?? []);
    }

    private static void ValidateMetadataSource(UpdateRule rule)
    {
        var source = rule.Source;
        var validSource = source?.Type switch
        {
            "github" => !string.IsNullOrWhiteSpace(source.Repository)
                && !string.IsNullOrWhiteSpace(source.VersionRegex) && !string.IsNullOrWhiteSpace(source.AssetRegex),
            "vendor-page" or "redirect" or "document" or "debian" => Uri.TryCreate(source.PageUrl, UriKind.Absolute, out var page)
                && page.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(source.DownloadRegex)
                && (source.Type != "document" || !string.IsNullOrWhiteSpace(source.ReleaseRegex))
                && (source.Type != "debian" || !string.IsNullOrWhiteSpace(source.Package)
                    && Uri.TryCreate(source.DownloadBase, UriKind.Absolute, out var downloadBase)
                    && downloadBase.Scheme == Uri.UriSchemeHttps),
            _ => false
        };
        if (!validSource || rule.AllowedDownloadHosts is not { Length: > 0 }
            || rule.AllowedDownloadHosts.Any(host => !Regex.IsMatch(host ?? "", "^[a-z0-9.-]+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        {
            throw new InvalidDataException($"catalogue update rule '{rule.Id}' has no supported verified metadata source");
        }
    }

    private static Dictionary<string, string[]> ReadScenarioMap(string repository)
    {
        var directory = Path.Combine(repository, "tests", "Cabinet.Runtime.Tests", "Scenarios");
        if (!Directory.Exists(directory))
        {
            return new(StringComparer.Ordinal);
        }

        return Directory.EnumerateFiles(directory, "*.cs")
            .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Text: File.ReadAllText(path)))
            .SelectMany(file => Regex.Matches(file.Text, "const\\s+string\\s+Id\\s*=\\s*\"([^\"]+)\"")
                .Select(match => (Id: match.Groups[1].Value, file.Name)))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Name)
                .Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    private static RuntimeFixtures ReadFixtures(string repository)
    {
        var directory = Path.Combine(repository, "tests", "Cabinet.Runtime.Tests", "Scenarios");
        var harness = Path.Combine(directory, "ScenarioHarness.cs");
        var installed = Path.Combine(directory, "InstalledEntry.cs");
        if (!File.Exists(harness) || !File.Exists(installed))
        {
            return new([], [], false, false);
        }

        var formats = DictionaryKeys(File.ReadAllText(harness), "Formats");
        var installedText = File.ReadAllText(installed);
        var exclusion = Regex.Matches(installedText, "format\\s*!=\\s*\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return new(formats, exclusion, formats.Length > 0,
            Regex.IsMatch(installedText, "Formats\\s*\\(\\s*string\\s+id\\s*\\).*Where\\s*\\(",
                RegexOptions.Singleline));
    }

    private static string[] AutomationBlockers(string repository, CatalogueEntry entry,
        IReadOnlyDictionary<string, string[]> scenarioMap, string scenario, RuntimeFixtures fixtures)
    {
        var blockers = new List<string>();
        if (!IsSupportedVersion(entry.Version))
        {
            blockers.Add("unsupported-version");
        }

        if (!fixtures.FormatsAvailable)
        {
            blockers.Add("scenario harness formats fixture unavailable");
        }

        if (!fixtures.ExclusionsAvailable)
        {
            blockers.Add("installed-entry format exclusions unavailable");
        }

        if (!scenarioMap.TryGetValue(entry.Id, out var mapped))
        {
            blockers.Add("runtime scenario missing");
            return blockers.ToArray();
        }

        if (mapped.Length != 1)
        {
            blockers.Add("runtime scenario mapping is ambiguous");
            return blockers.ToArray();
        }

        if (scenario.Length == 0)
        {
            blockers.Add("runtime scenario unavailable");
            return blockers.ToArray();
        }

        return AddScenarioCapabilities(repository, blockers, entry, fixtures, mapped[0]);
    }

    private static string[] AddScenarioCapabilities(string repository, List<string> blockers, CatalogueEntry entry,
        RuntimeFixtures fixtures, string scenarioName)
    {
        var path = Path.Combine(repository, "tests", "Cabinet.Runtime.Tests", "Scenarios", scenarioName + ".cs");
        if (!File.Exists(path))
        {
            blockers.Add("runtime scenario fixture unavailable");
            return blockers.ToArray();
        }

        var bridges = DictionaryKeys(File.ReadAllText(path), "Bridges");
        if (bridges.Length == 0)
        {
            blockers.Add("scenario bridge formats unavailable");
        }

        var declared = entry.Formats.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (declared.Length == 0)
        {
            blockers.Add("catalogue formats unavailable");
            return blockers.ToArray();
        }

        foreach (var format in declared)
        {
            if (fixtures.ExcludedFormats.Contains(format))
            {
                blockers.Add($"excluded runtime format: {format}");
            }
            else if (!fixtures.Formats.Contains(format, StringComparer.Ordinal))
            {
                blockers.Add($"unsupported runtime format: {format}");
            }
            else if (!bridges.Contains(format, StringComparer.Ordinal))
            {
                blockers.Add($"scenario does not bridge format: {format}");
            }
        }

        return blockers.ToArray();
    }

    private static string[] DictionaryKeys(string source, string dictionary)
    {
        var match = Regex.Match(source,
            $@"\b{Regex.Escape(dictionary)}\s*=\s*new\s*\(\s*\)\s*\{{(?<body>[\s\S]*?)\}};");
        return Regex.Matches(match.Groups["body"].Value, "\\[\\s*\"([^\"]+)\"\\s*\\]")
            .Select(found => found.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsSupportedVersion(string version) =>
        Regex.IsMatch(version,
            "^(?:0|[1-9][0-9]*)(?:[._](?:0|[1-9][0-9]*))*(?:-[0-9]+)?$",
            RegexOptions.CultureInvariant);

    private static string Field(string text, string name) =>
        Regex.Match(text, $"^{Regex.Escape(name)}:\\s*(.+)$", RegexOptions.Multiline) is { Success: true } match
            ? match.Groups[1].Value.Trim()
            : "";

    private static string Markdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private sealed record CatalogueEntry(string Id, string Path, string Version, string Source, string Formats);
    private sealed record UpdateConfiguration(UpdateRule[]? Entries, BlockedRule[]? Blocked);
    private sealed record UpdateRule(string Id, string Path, string[]? AllowedDownloadHosts,
        SourceRule? Source);
    private sealed record BlockedRule(string Id, string Reason);
    private sealed record SourceRule(string? Type, string? Repository, string? VersionRegex, string? AssetRegex,
        string? PageUrl, string? DownloadRegex, string? ReleaseRegex, string? Package, string? DownloadBase);
    private sealed record RuntimeFixtures(string[] Formats, HashSet<string> ExcludedFormats,
        bool FormatsAvailable, bool ExclusionsAvailable);

}

internal sealed class CatalogueUpdateStatus(string id, string path, string version, string source, string scenario,
    string discovery, string reason, string[] automationBlockers)
{
    public string Id { get; } = id;
    public string Path { get; } = path;
    public string Version { get; } = version;
    public string Source { get; } = source;
    public string Scenario { get; } = scenario;
    public string Discovery { get; } = discovery;
    public string Reason { get; } = reason;
    public string[] AutomationBlockers { get; set; } = automationBlockers;
    public string Outcome { get; set; } = "not-run";
    public string? AvailableVersion { get; set; }
    public string? Error { get; set; }
    public bool AutomaticEligible => Discovery == "enabled" && AutomationBlockers.Length == 0;
}
