namespace Cabinet.Core;

public enum PluginKind
{
    Windows,
    Native,
}

public enum PluginSource
{
    Download,
    Rolling,
    Byo,
}

public sealed record LibraryEntry(
    string Id,
    string Name,
    PluginKind Kind,
    string Category,
    string Summary,
    string? Homepage,
    PluginSource Source,
    string? Url,
    string? Account,
    string? Sha256,
    string? DemoUrl,
    string? DemoSha256,
    string Prefix,
    string? Runner,
    bool Dxvk,
    SyncMode Sync,
    IReadOnlyList<string> Winetricks,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyDictionary<string, string> Relink,
    bool Desktop,
    string? Script,
    string? Launch,
    string? LaunchService,
    string? LaunchHelper,
    IReadOnlyList<string> LaunchArgs,
    string? Scheme,
    string? Keep,
    string? Recover,
    string? Data,
    string? Developer,
    string? Version,
    string? Licence,
    string? Licensing,
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> Description,
    string Vendor)
{
    public bool Manager => Launch is not null;

    public string? LaunchExe => Launch?.Split('\\')[^1];

    public string Consent =>
        $"Cabinet installs {Name} without showing you its licence, so installing it accepts "
        + $"{(Developer is null ? "its developer" : Developer)}'s terms on your behalf."
        + (Winetricks.Count == 0
            ? ""
            : $" Winetricks accepts the licences of {string.Join(", ", Winetricks)} as well.");

    public static LibraryEntry Parse(string id, string text, string vendor = "") =>
        LibraryEntryParser.Parse(id, text, vendor);

    public IReadOnlyList<string> Requirements()
    {
        var costs = new List<string>();

        if (Runner is { } wine)
        {
            costs.Add($"Wine {wine}");
        }

        if (Dxvk)
        {
            costs.Add("DXVK");
        }

        if (Sync != SyncMode.System)
        {
            costs.Add(PrefixSettings.Word(Sync));
        }

        if (Env.Count > 0)
        {
            costs.Add(string.Join(", ", Env.Keys));
        }

        if (Winetricks.Count > 0)
        {
            costs.Add($"Winetricks {string.Join(", ", Winetricks)}");
        }

        return costs;
    }
}

public sealed record LibraryFilter(
    string? Search = null,
    string? Category = null,
    string? Developer = null,
    PluginKind? Kind = null,
    bool? Installed = null)
{
    public bool Matches(LibraryEntry entry, bool installed) =>
        (Category is null
            || string.Equals(Category, entry.Category, StringComparison.OrdinalIgnoreCase))
        && (Developer is null
            || string.Equals(Developer, entry.Developer, StringComparison.OrdinalIgnoreCase))
        && (Kind is null || Kind == entry.Kind)
        && (Installed is null || Installed == installed)
        && Terms().All(term =>
            Haystack(entry).Contains(term, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> Terms() =>
        (Search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string Haystack(LibraryEntry entry) => string.Join(
        ' ',
        entry.Name,
        entry.Id,
        entry.Developer,
        entry.Category,
        entry.Summary,
        entry.Vendor);
}
