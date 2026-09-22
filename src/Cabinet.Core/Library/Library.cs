namespace Cabinet.Core;

public sealed partial class Library(Layout layout, IProcessRunner runner)
{
    private readonly Http http = new(runner);

    public IReadOnlyList<LibraryEntry> Entries()
    {
        if (!Directory.Exists(layout.LibraryDir))
        {
            return [];
        }

        var entries = Directory.EnumerateDirectories(layout.LibraryDir)
            .SelectMany(vendor => Directory.EnumerateFiles(vendor, "*.yml")
                .Select(path => LibraryEntry.Parse(
                    Path.GetFileNameWithoutExtension(path),
                    File.ReadAllText(path),
                    Path.GetFileName(vendor))))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .FirstOrDefault(same => same.Count() > 1) is { } clash)
        {
            throw new InvalidOperationException(
                $"two vendors both ship {clash.Key}.yml — "
                + $"{string.Join(" and ", clash.Select(entry => entry.Vendor))}");
        }

        return entries;
    }

    public static IReadOnlyList<string> Categories(IEnumerable<LibraryEntry> entries) =>
        [.. entries
            .Select(entry => entry.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)];

    public static IReadOnlyList<string> Developers(IEnumerable<LibraryEntry> entries) =>
        [.. entries
            .Select(entry => entry.Developer)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(developer => developer, StringComparer.OrdinalIgnoreCase)];

    public LibraryEntry Find(string id) =>
        Entries().FirstOrDefault(entry => entry.Id == id)
        ?? throw new KeyNotFoundException($"no plugin '{id}' in the library");

    public IReadOnlyDictionary<string, string?> Installed()
    {
        var installed = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var prefix in new Prefixes(layout, runner).Names())
        {
            foreach (var id in Recorded(prefix))
            {
                installed[id] = prefix;
            }
        }

        if (Directory.Exists(layout.NativeDir))
        {
            foreach (var id in Directory.EnumerateDirectories(layout.NativeDir)
                         .Select(path => Path.GetFileName(path))
                         .Where(id => Layout.IsName(id)
                                      && !Underway.Marked(layout.NativeInstalling(id))))
            {
                installed[id] = null;
            }
        }

        return installed;
    }

    public IReadOnlyList<LibraryEntry> Retired()
    {
        var shipped = Entries().Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        return Installed()
            .Where(held => !shipped.Contains(held.Key) && Layout.IsName(held.Key))
            .OrderBy(held => held.Key, StringComparer.Ordinal)
            .Select(held => LibraryEntry.Parse(
                held.Key,
                held.Value is { } prefix
                    ? $"Name: {held.Key}\nKind: windows\nSource: byo\nPrefix: {prefix}\n"
                    : $"Name: {held.Key}\nKind: native\nSource: byo\n"))
            .ToList();
    }

    public LibraryEntry Removable(string id) =>
        Entries().Concat(Retired()).FirstOrDefault(entry => entry.Id == id)
        ?? throw new KeyNotFoundException($"no plugin '{id}' in the library");

    public IReadOnlyList<(string Id, string? Prefix)> Unfinished()
    {
        var unfinished = new List<(string Id, string? Prefix)>();

        foreach (var prefix in new Prefixes(layout, runner).Names())
        {
            if (Pending.Parse(Underway.Abandoned(layout.PrefixInstalling(prefix))) is { } left)
            {
                unfinished.Add((left.Id, prefix));
            }
        }

        if (Directory.Exists(layout.NativeDir))
        {
            const string mark = Layout.NativeInstallingMarker;

            foreach (var marker in Directory.EnumerateFiles(layout.NativeDir, mark + "*")
                         .Order(StringComparer.Ordinal))
            {
                if (Underway.Abandoned(marker) is not null)
                {
                    unfinished.Add((Path.GetFileName(marker)[mark.Length..], null));
                }
            }
        }

        return unfinished;
    }

    public IReadOnlyList<(string Id, string Prefix)> LeftOpen() =>
        [
            .. new Prefixes(layout, runner).Names()
                .SelectMany(
                    prefix => Directory.EnumerateFiles(
                            layout.PrefixPath(prefix), Layout.OpenMarker + "*")
                        .Order(StringComparer.Ordinal)
                        .Where(marker => Underway.Abandoned(marker) is not null),
                    (prefix, marker) =>
                        (Path.GetFileName(marker)[Layout.OpenMarker.Length..], prefix)),
        ];

    public IReadOnlySet<string> Opened() =>
        new Prefixes(layout, runner).Names()
            .SelectMany(prefix => Directory.EnumerateFiles(
                layout.PrefixPath(prefix), Layout.OpenMarker + "*"))
            .Where(Underway.Held)
            .Select(marker => Path.GetFileName(marker)[Layout.OpenMarker.Length..])
            .ToHashSet(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> InstalledMoreThanOnce() =>
        new Prefixes(layout, runner).Names()
            .SelectMany(prefix => Recorded(prefix).Distinct(StringComparer.Ordinal),
                (prefix, id) => (Id: id, Prefix: prefix))
            .GroupBy(held => held.Id, StringComparer.Ordinal)
            .Where(same => same.Count() > 1)
            .ToDictionary(
                same => same.Key,
                IReadOnlyList<string> (same) => [.. same.Select(held => held.Prefix)],
                StringComparer.Ordinal);

    private string Where(LibraryEntry entry) =>
        Installed().TryGetValue(entry.Id, out var where) && where is not null
            ? where
            : throw NotInstalled(entry);

    private static KeyNotFoundException NotInstalled(LibraryEntry entry) =>
        new($"{entry.Name} is not installed");

    private IEnumerable<string[]> Lines(string prefix) =>
        File.Exists(layout.PrefixPluginsFile(prefix))
            ? File.ReadAllLines(layout.PrefixPluginsFile(prefix))
                .Select(line => line.Split(
                    '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(fields => fields.Length > 0)
            : [];

    public IEnumerable<string> Recorded(string prefix) =>
        Lines(prefix).Select(fields => fields[0]);

    private IEnumerable<string> RecordedKeys(string prefix, string id) =>
        Lines(prefix).Where(fields => fields[0] == id).SelectMany(fields => fields.Skip(1));

    private void Record(string prefix, string id, IEnumerable<string> keys)
    {
        var kept = Lines(prefix).Where(fields => fields[0] != id).ToList();
        kept.Add([id, .. keys]);
        Write(prefix, kept);
    }

    private void Forget(string prefix, string id) =>
        Write(prefix, Lines(prefix).Where(fields => fields[0] != id));

    private void Write(string prefix, IEnumerable<string[]> lines) =>
        File.WriteAllLines(
            layout.PrefixPluginsFile(prefix), lines.Select(fields => string.Join('\t', fields)));
}
