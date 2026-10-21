using System.Text.Json;

namespace Cabinet.Ci;

internal static class CatalogueUpdateConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static CatalogueUpdateConfig Read(string repository)
    {
        var library = Path.Combine(repository, "data", "library");
        if (!Directory.Exists(library))
        {
            throw new DirectoryNotFoundException($"catalogue library not found: {library}");
        }

        var entries = new List<CatalogueUpdateEntry?>();
        var blocked = new List<CatalogueUpdateBlocked?>();
        foreach (var vendor in Directory.EnumerateDirectories(library).Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(vendor, "catalogue-updates.json");
            if (!File.Exists(path))
            {
                continue;
            }

            var configuration = JsonSerializer.Deserialize<CatalogueUpdateConfig>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("catalogue update configuration is empty");
            entries.AddRange(configuration.Entries ?? []);
            blocked.AddRange(configuration.Blocked ?? []);
        }

        return new(entries.OrderBy(entry => entry?.Id ?? "", StringComparer.Ordinal).ToArray(),
            blocked.OrderBy(item => item?.Id ?? "", StringComparer.Ordinal).ToArray());
    }
}

internal sealed record CatalogueUpdateConfig(CatalogueUpdateEntry?[]? Entries, CatalogueUpdateBlocked?[]? Blocked);
internal sealed record CatalogueUpdateEntry(string Id, string Path, string Scenario, string[] AllowedDownloadHosts,
    CatalogueUpdateSource? Source);
internal sealed record CatalogueUpdateBlocked(string Id, string Reason);
internal sealed record CatalogueUpdateSource(string Type, string? Repository = null, string? VersionRegex = null,
    string? AssetRegex = null, string? PageUrl = null, string? DownloadRegex = null, string? ReleaseRegex = null,
    string? Package = null, string? DownloadBase = null, string? RelativePrefix = null);
