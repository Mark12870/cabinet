namespace Cabinet.Core;

public sealed record ComponentEntry(
    string Name,
    string Category,
    string SubCategory,
    string Channel,
    long Date);

public sealed record ComponentFile(string FileName, string Url, string Checksum);

public static class Components
{
    private const string Repository = "https://raw.githubusercontent.com/bottlesdevs/components/main/";

    public const string IndexUrl = Repository + "index.yml";

    public static string ManifestUrl(string name) => $"{Repository}runners/wine/{name}.yml";

    public static IReadOnlyList<ComponentEntry> Entries(string yaml)
    {
        var entries = new List<ComponentEntry>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string? name = null;

        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (line.Length == trimmed.Length && line[^1] == ':')
            {
                if (name is not null)
                {
                    entries.Add(EntryOf(name, fields));
                }

                name = line[..^1];
                fields.Clear();
                continue;
            }

            var split = trimmed.IndexOf(':');
            if (name is not null && split > 0)
            {
                fields[trimmed[..split]] = Value(trimmed[(split + 1)..]);
            }
        }

        if (name is not null)
        {
            entries.Add(EntryOf(name, fields));
        }

        return entries;
    }

    public static ComponentFile Manifest(string yaml)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.Trim().TrimStart('-').TrimStart();
            var split = line.IndexOf(':');

            if (split > 0 && !fields.ContainsKey(line[..split]))
            {
                fields[line[..split]] = Value(line[(split + 1)..]);
            }
        }

        return new ComponentFile(
            Field(fields, "file_name"), Field(fields, "url"), Field(fields, "file_checksum"));
    }

    private static ComponentEntry EntryOf(string name, IReadOnlyDictionary<string, string> fields) =>
        new(name,
            Field(fields, "Category"),
            Field(fields, "Sub-category"),
            Field(fields, "Channel"),
            long.TryParse(Field(fields, "Date"), out var date) ? date : 0);

    private static string Field(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : "";

    private static string Value(string raw) => raw.Trim().Trim('\'', '"');
}
