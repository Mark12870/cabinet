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

    public static LibraryEntry Parse(string id, string text, string vendor = "")
    {
        var fields = Fields(text);
        var kind = ParseKind(id, Required(id, fields, "Kind"));
        var source = ParseSource(id, Value(fields, "Source") ?? "download");
        var demo = Value(fields, "DemoUrl");

        if (source != PluginSource.Byo && Value(fields, "Url") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml says Source: {source.ToString().ToLowerInvariant()} but has no Url");
        }

        if (source == PluginSource.Byo && Value(fields, "Url") is not null)
        {
            throw new InvalidOperationException(
                $"{id}.yml is byo and carries Url — use DemoUrl for its downloadable demo");
        }

        if (source == PluginSource.Byo && Value(fields, "Sha256") is not null)
        {
            throw new InvalidOperationException(
                $"{id}.yml is byo and carries Sha256 — use DemoSha256 for its downloadable demo");
        }

        if (Value(fields, "DemoSha256") is not null && demo is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml carries DemoSha256 but has no DemoUrl");
        }

        if (demo is not null && Value(fields, "DemoSha256") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has a DemoUrl but no DemoSha256 — a download nobody checked "
                + "is not one Cabinet will run");
        }

        if (demo is not null && source != PluginSource.Byo)
        {
            throw new InvalidOperationException(
                $"{id}.yml carries DemoUrl but is not Source: byo — the demo and your "
                + "installer share one entry");
        }

        if (demo is not null && kind != PluginKind.Windows)
        {
            throw new InvalidOperationException(
                $"{id}.yml carries DemoUrl but is not a Windows plugin — only Wine can install it");
        }

        if (source == PluginSource.Download && Value(fields, "Sha256") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has no Sha256 — a download nobody checked is not one Cabinet will run");
        }

        if (source == PluginSource.Rolling && Value(fields, "Sha256") is not null)
        {
            throw new InvalidOperationException(
                $"{id}.yml is rolling and carries Sha256 — the vendor changes what is behind "
                + "that one URL, so a checksum here could only ever be the build whoever wrote "
                + "the entry happened to download");
        }

        if (kind == PluginKind.Windows && fields.ContainsKey("Data"))
        {
            throw new InvalidOperationException(
                $"{id}.yml is a Windows plugin and carries Data — what it writes stays in its "
                + "prefix");
        }

        if (kind == PluginKind.Windows && fields.ContainsKey("Relink"))
        {
            throw new InvalidOperationException(
                $"{id}.yml is a Windows plugin and carries Relink — its libraries come out of "
                + "its prefix, not the DAW's runtime");
        }

        if (kind == PluginKind.Native
            && new[]
            {
                "Prefix", "Runner", "Dxvk", "Sync", "Winetricks", "Env", "Desktop",
                "Launch", "LaunchService", "LaunchHelper", "LaunchArgs", "Scheme", "Keep",
                "Recover",
            }
                .FirstOrDefault(fields.ContainsKey)
                is { } windowsOnly)
        {
            throw new InvalidOperationException(
                $"{id}.yml is native and carries {windowsOnly} — a native plugin has no prefix, "
                + "no Wine, no Direct3D to replace, no environment to set and nothing to open");
        }

        if (source != PluginSource.Byo && fields.ContainsKey("Account"))
        {
            throw new InvalidOperationException(
                $"{id}.yml carries Account but Cabinet downloads it — a login page is only of "
                + "use where the file has to come from you");
        }

        if (Value(fields, "LaunchService") is not null && Value(fields, "Launch") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has LaunchService but no Launch");
        }

        if (Value(fields, "LaunchHelper") is not null && Value(fields, "Launch") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has LaunchHelper but no Launch");
        }

        if (Value(fields, "LaunchArgs") is not null && Value(fields, "Launch") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has LaunchArgs but no Launch");
        }

        if (Value(fields, "Scheme") is not null && Value(fields, "Launch") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has Scheme but no Launch — a link is handed to an app of its own");
        }

        if (Value(fields, "Recover") is not null && Value(fields, "Keep") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has Recover but no Keep — a recovery script needs the directory "
                + "whose downloads Cabinet holds on to");
        }

        if (Value(fields, "Keep") is not null && Value(fields, "Recover") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has Keep but no Recover — keeping downloads is only of use to a "
                + "script that recovers from them");
        }

        if (Value(fields, "Keep") is not null && Value(fields, "Launch") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has Keep but no Launch — downloads are kept while an app of its "
                + "own is open");
        }

        return new LibraryEntry(
            id,
            Required(id, fields, "Name"),
            kind,
            Value(fields, "Category") ?? "Plugin",
            Value(fields, "Summary") ?? "",
            Value(fields, "Homepage"),
            source,
            Value(fields, "Url"),
            Value(fields, "Account"),
            Value(fields, "Sha256"),
            demo,
            Value(fields, "DemoSha256"),
            Value(fields, "Prefix") is { } prefix ? ParsePrefix(id, prefix) : id,
            Value(fields, "Runner"),
            Value(fields, "Dxvk") is { } dxvk && bool.Parse(dxvk),
            Value(fields, "Sync") is { } sync ? PrefixSettings.ParseSync(sync) : SyncMode.System,
            ParseWinetricks(id, Value(fields, "Winetricks")),
            ParseEnv(id, Value(fields, "Env")),
            ParseRelink(id, Value(fields, "Relink")),
            Value(fields, "Desktop") is { } desktop && bool.Parse(desktop),
            Value(fields, "Script") is { } script ? ParseScript(id, script) : null,
            Value(fields, "Launch") is { } launch ? ParseLaunch(id, launch) : null,
            Value(fields, "LaunchService"),
            Value(fields, "LaunchHelper") is { } helper ? ParseLaunchHelper(id, helper) : null,
            ParseLaunchArgs(id, Value(fields, "LaunchArgs")),
            Value(fields, "Scheme") is { } scheme ? ParseScheme(id, scheme) : null,
            Value(fields, "Keep") is { } keep ? ParseKeep(id, keep) : null,
            Value(fields, "Recover") is { } recover ? ParseScript(id, recover) : null,
            Value(fields, "Data") is { } data ? ParseData(id, data) : null,
            Value(fields, "Developer"),
            Value(fields, "Version"),
            Value(fields, "Licence"),
            Sentence(Value(fields, "Licensing")),
            Split(Value(fields, "Formats")),
            Paragraphs(Value(fields, "Description")),
            vendor);
    }

    private static string ParsePrefix(string id, string name) =>
        Layout.IsName(name)
            ? name
            : throw new InvalidOperationException(
                $"{id}.yml has Prefix: {name} — the name of a prefix, one word of a path");

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

    private static string ParseScript(string id, string name)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".sh", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Script: {name} — the name of a .sh file this build ships, not a "
                + "path");
        }

        return name;
    }

    private static string ParseLaunch(string id, string path)
    {
        if (path is not [var drive, ':', '\\', ..] || !char.IsAsciiLetter(drive))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Launch: {path} — the Windows path of an executable its own "
                + @"installer leaves in the prefix, such as C:\Program Files\Thing\Thing.exe");
        }

        return path;
    }

    private static string ParseLaunchHelper(string id, string name)
    {
        if (name.Length <= ".exe".Length
            || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            throw new InvalidOperationException(
                $"{id}.yml has LaunchHelper: {name} — the name of the .exe its app leaves "
                + "running, such as ThingHelper.exe, not a path");
        }

        return name;
    }

    private static readonly string[] SharedSchemes = ["http", "https", "file", "mailto"];

    private static string ParseScheme(string id, string scheme)
    {
        if (scheme is not [var first, ..]
            || !char.IsAsciiLetterLower(first)
            || scheme.Any(character => !char.IsAsciiLetterLower(character)
                                       && !char.IsAsciiDigit(character)
                                       && character is not ('+' or '-' or '.')))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Scheme: {scheme} — the lower-case name before :// in the links "
                + "its app registers, such as thingmanager");
        }

        if (SharedSchemes.Contains(scheme, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Scheme: {scheme}, which every browser and desktop already opens");
        }

        return scheme;
    }

    private static IReadOnlyList<string> ParseLaunchArgs(string id, string? text)
    {
        if (text is null)
        {
            return [];
        }

        var found = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (found.Count == 0)
        {
            throw new InvalidOperationException(
                $"{id}.yml has LaunchArgs with nothing under it — one argument a line, such as "
                + "--disable-gpu");
        }

        return found;
    }

    private static IReadOnlyDictionary<string, string> ParseEnv(string id, string? text)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = line.IndexOf('=');

            if (at < 1 || line[..at].Trim() is not { Length: > 0 } key)
            {
                throw new InvalidOperationException(
                    $"{id}.yml has Env: {line.Trim()} — one KEY=VALUE a line, such as "
                    + "WINEDLLOVERRIDES=wbemprox=n");
            }

            if (PrefixSettings.Owned.Contains(key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{id}.yml sets {key}, which Cabinet sets itself — the shim drops it, so "
                    + "the entry would only look as though it took effect");
            }

            found[key] = line[(at + 1)..];
        }

        return found;
    }

    private static IReadOnlyList<string> ParseWinetricks(string id, string? text)
    {
        var found = Split(text);
        var invalid = found.FirstOrDefault(verb =>
            verb.Length == 0
            || !char.IsAsciiLetterOrDigit(verb[0])
            || verb.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character != '.'
                && character != '-'
                && character != '_'));

        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has Winetricks: {invalid} — verb names split by commas, such as corefonts, vcrun2022");
        }

        if (found.Distinct(StringComparer.OrdinalIgnoreCase).Count() != found.Count)
        {
            throw new InvalidOperationException(
                $"{id}.yml has duplicate Winetricks dependencies");
        }

        return found;
    }

    private static IReadOnlyDictionary<string, string> ParseRelink(string id, string? text)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = line.IndexOf('=');

            if (at < 1
                || line[..at].Trim() is not { Length: > 0 } soname
                || line[(at + 1)..].Trim() is not { Length: > 0 } replacement)
            {
                throw new InvalidOperationException(
                    $"{id}.yml has Relink: {line.Trim()} — one OLD.so.N = NEW.so.N a line, such "
                    + "as libcurl-gnutls.so.4 = libcurl.so.4");
            }

            if (replacement.Length > soname.Length)
            {
                throw new InvalidOperationException(
                    $"{id}.yml relinks {soname} to the longer {replacement} — the name is "
                    + "written back over the old one, and a string table cannot grow in place");
            }

            found[soname] = replacement;
        }

        return found;
    }

    private static string ParseKeep(string id, string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (Path.IsPathRooted(relative) || parts.Length == 0 || parts.Contains(".."))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Keep: {relative} — a directory inside the prefix whose "
                + "downloads Cabinet holds on to, such as drive_c/users/Public/Downloads");
        }

        return string.Join('/', parts);
    }

    private static string ParseData(string id, string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (Path.IsPathRooted(relative)
            || parts.Length < 2
            || parts.Contains("..")
            || Layout.ScanDirectories.Contains(parts[0], StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Data: {relative} — a directory of the plugin's own under your "
                + "home, such as .u-he/Podolski");
        }

        return string.Join('/', parts);
    }

    private static string? Sentence(string? value) =>
        value is null
            ? null
            : string.Join(' ', value.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static IReadOnlyList<string> Split(string? value) =>
        value is null
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> Paragraphs(string? value) =>
        value is null
            ? []
            : value.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => string.Join(' ', paragraph.Split(
                    '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                .Where(paragraph => paragraph.Length > 0)
                .ToList();

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var indented = new List<string>();
        string? block = null;

        void Close()
        {
            if (block is not null)
            {
                fields[block] = string.Join('\n', indented).Trim();
                indented.Clear();
                block = null;
            }
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (block is not null && (line.Length == 0 || char.IsWhiteSpace(raw[0])))
            {
                indented.Add(line);
                continue;
            }

            Close();

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var at = line.IndexOf(':');

            if (at <= 0)
            {
                continue;
            }

            var value = line[(at + 1)..].Trim().Trim('\'', '"');

            if (value.Length == 0)
            {
                block = line[..at].TrimEnd();
            }
            else
            {
                fields[line[..at].TrimEnd()] = value;
            }
        }

        Close();

        return fields;
    }

    private static string? Value(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static string Required(
        string id, IReadOnlyDictionary<string, string> fields, string key) =>
        Value(fields, key) ?? throw new InvalidOperationException($"{id}.yml has no {key}");

    private static PluginKind ParseKind(string id, string word) => word.ToLowerInvariant() switch
    {
        "windows" => PluginKind.Windows,
        "native" => PluginKind.Native,
        _ => throw new InvalidOperationException(
            $"{id}.yml has Kind: {word} — windows or native"),
    };

    private static PluginSource ParseSource(string id, string word) => word.ToLowerInvariant() switch
    {
        "download" => PluginSource.Download,
        "rolling" => PluginSource.Rolling,
        "byo" => PluginSource.Byo,
        _ => throw new InvalidOperationException(
            $"{id}.yml has Source: {word} — download, rolling or byo"),
    };
}

public enum RemovalKind
{
    Native,
    TakesPrefix,
    PluginOrPrefix,
    KeepsPrefix,
}

public sealed record Removal(
    LibraryEntry Entry, RemovalKind Kind, string? Prefix, IReadOnlyList<string> Sharing)
{
    public bool Agrees(Removal other) =>
        other.Entry.Id == Entry.Id
        && other.Kind == Kind
        && other.Prefix == Prefix
        && other.Sharing.ToHashSet(StringComparer.Ordinal).SetEquals(Sharing);
}

public enum StopResult
{
    Closed,
    LeftRunning,
    Forced,
}

public sealed record StopOutcome(StopResult Result, string Told);

internal sealed record Pending(string Id, bool Created, IReadOnlyList<string> Keys)
{
    private const string Made = "created";
    private const string Found = "found";

    public static Pending? Parse(string? text) =>
        text?.Split('\t', StringSplitOptions.TrimEntries) is [{ Length: > 0 } id, var made, .. var keys]
            ? new Pending(id, made == Made, [.. keys.Where(key => key.Length > 0)])
            : null;

    public override string ToString() => string.Join('\t', [Id, Created ? Made : Found, .. Keys]);
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

public sealed class Library(Layout layout, IProcessRunner runner)
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

    public void Install(
        LibraryEntry entry,
        string? prefix = null,
        string? installer = null,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null)
    {
        using var installing = Underway.Begin(layout.InstallLockPath(entry.Id))
                               ?? throw new InvalidOperationException(
                                   $"Cabinet is installing {entry.Name} right now — wait for "
                                   + "that to finish");
        var already = entry.Kind == PluginKind.Windows
            ? Installed().GetValueOrDefault(entry.Id)
            : null;
        var where = prefix
                    ?? already
                    ?? Unfinished().FirstOrDefault(left => left.Id == entry.Id).Prefix
                    ?? entry.Prefix;

        if (already is not null && already != where)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is installed in {already} already — install it again there, or "
                + "remove it first");
        }

        var installLog = layout.InstallLogPath(entry.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(installLog)!);
        File.WriteAllText(installLog, "");

        void Say(string line)
        {
            LogFile.Append(installLog, line);
            onOutput?.Invoke(line);
        }

        if (entry.Kind == PluginKind.Native)
        {
            if (prefix is not null)
            {
                throw new ArgumentException(
                    $"{entry.Name} is a Linux plugin, so it needs no prefix — your DAW loads it "
                    + "directly", nameof(prefix));
            }

            InstallNative(entry, installer, Say, onProgress);
            return;
        }

        InstallWindows(entry, where, installer, Say, onProgress);
    }

    public IReadOnlyList<UninstallEntry> Uninstallers(string prefix) =>
        new PrefixRegistry(layout, runner).Uninstallers(prefix);

    public IReadOnlyList<UninstallEntry> PossibleUninstallers(Removal removal) =>
        removal.Prefix is { } prefix && !RecordedKeys(prefix, removal.Entry.Id).Any()
            ? Candidates(prefix, removal.Entry)
            : [];

    private IReadOnlyList<UninstallEntry> Candidates(string prefix, LibraryEntry entry)
    {
        var attributed = Lines(prefix)
            .Where(fields => fields[0] != entry.Id)
            .SelectMany(fields => fields.Skip(1))
            .ToHashSet(StringComparer.Ordinal);

        return Uninstallers(prefix)
            .Where(one => !attributed.Contains(one.Key)
                          && !IsWine(one.Name)
                          && Names(one.Name, entry.Name))
            .ToList();
    }

    private static bool IsWine(string name) =>
        name.StartsWith("Wine ", StringComparison.OrdinalIgnoreCase);

    private static bool Names(string uninstaller, string name)
    {
        var words = Words(uninstaller);
        var wanted = string.Concat(Words(name));

        for (var first = 0; first < words.Count; first++)
        {
            var joined = "";

            for (var last = first; last < words.Count && joined.Length < wanted.Length; last++)
            {
                joined += words[last];

                if (joined == wanted)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> Words(string text) =>
        new string([.. text.Select(character =>
                char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public IReadOnlyList<string> Sharing(string prefix, string id) =>
        [.. Recorded(prefix).Where(other => other != id)];

    public Removal RemovalOf(LibraryEntry entry)
    {
        if (entry.Kind == PluginKind.Native)
        {
            return Directory.Exists(layout.NativePath(entry.Id))
                ? new Removal(entry, RemovalKind.Native, null, [])
                : throw NotInstalled(entry);
        }

        var where = Where(entry);
        var sharing = Sharing(where, entry.Id);

        return new Removal(
            entry,
            entry.Manager ? RemovalKind.TakesPrefix
            : sharing.Count > 0 ? RemovalKind.KeepsPrefix
            : RemovalKind.PluginOrPrefix,
            where,
            sharing);
    }

    private const int ServiceAlreadyRunning = 1056 & 0xff;

    private static readonly TimeSpan Stability = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    public void Launch(LibraryEntry entry, Action<string>? onOutput = null, string? link = null)
    {
        if (entry.Launch is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is a plugin, not an application Cabinet can open");
        }

        var where = Where(entry);

        var prefixes = new Prefixes(layout, runner);
        var log = layout.PrefixLaunchLog(where);

        void Say(string line)
        {
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
        }

        using var open = prefixes.OpenApp(where, $"open {entry.Name}");
        var pluginDirectories = layout.PrefixPluginDirs(where).ToList();

        foreach (var directory in pluginDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        var guarded = KeepDir(where, entry);
        var kept = new HashSet<string>(StringComparer.Ordinal);

        if (guarded is not null)
        {
            Directory.CreateDirectory(guarded);
        }

        var watch = new PluginWatch(Bundled(where));
        using var closed = new CancellationTokenSource();
        using var monitor = new PluginMonitor(
            guarded is null ? pluginDirectories : [.. pluginDirectories, guarded]);

        File.WriteAllText(log, "");
        Say($"Opening {entry.Name}. What it installs is bridged as it lands.");

        if (new VirtualDesktop(layout, runner).EnabledIn(where))
        {
            Say($"{where} draws on a desktop of its own, so {entry.Name} is confined to it, "
                + $"the pointer too, until {where}'s virtual desktop is turned off.");
        }

        if (entry.LaunchService is { } service)
        {
            Say($"Starting {service}.");
            var started = prefixes.RunJoined(where, ["sc", "start", service], logTo: log);

            if (!started.Ok && started.ExitCode != ServiceAlreadyRunning)
            {
                throw new InvalidOperationException(
                    $"{entry.Name}'s {service} service could not start (exit code {started.ExitCode})");
            }
        }

        var watching = Task.Run(() =>
        {
            while (monitor.Wait(
                closed.Token,
                watch.Pending ? Stability : Timeout.InfiniteTimeSpan))
            {
                try
                {
                    Hold(guarded, layout.PrefixKeptDir(where), kept, Say);

                    if (watch.Changed(Bundled(where)) is { } change)
                    {
                        Narrate(change, Say);
                        Bridge(prefixes, Say);
                        watch.Accept();
                    }
                }
                catch (Exception failure)
                {
                    Say(failure.Message);
                }
            }
        });

        using var opened = Underway.Begin(layout.PrefixOpen(where, entry.Id));
        opened?.Note(entry.Id);
        ProcessResult ran;

        try
        {
            ran = prefixes.RunJoined(
                where,
                [entry.Launch, .. entry.LaunchArgs, .. link is null ? [] : new[] { link }],
                logTo: log);

            if (entry.LaunchService is { } stopping)
            {
                prefixes.RunJoined(where, ["sc", "stop", stopping], logTo: log);
            }

            if (entry.LaunchHelper is { } helper && !Running(prefixes, where, entry.LaunchExe!))
            {
                prefixes.RunJoined(where, ["taskkill", "/f", "/im", helper], logTo: log);
            }
        }
        finally
        {
            open.Dispose();
            closed.Cancel();
            watching.Wait();
        }

        Hold(guarded, layout.PrefixKeptDir(where), kept, Say);

        if (watch.Closed(Bundled(where)) is { } change)
        {
            Narrate(change, Say);
        }

        Exception? recoverFailure = null;
        var recovered = true;

        if (entry.Recover is not null)
        {
            try
            {
                using var claim = prefixes.Claim(where, $"finish {entry.Name}'s install");

                new InstallScript(layout, runner).Recover(
                    entry,
                    layout.PrefixPath(where),
                    layout.PrefixKeptDir(where),
                    prefixes.Variables(where),
                    Say);

                Settle(prefixes, where, log);
            }
            catch (PrefixInUseException waiting)
            {
                recovered = false;
                Say($"{waiting.Message} What {entry.Name} downloaded is kept, and Cabinet "
                    + $"finishes the install the next time you open {entry.Name}.");
            }
            catch (Exception failure)
            {
                recoverFailure = failure;
            }
        }

        Exception? bridgeFailure = null;

        try
        {
            Bridge(prefixes, Say);
        }
        catch (Exception failure)
        {
            bridgeFailure = failure;
        }

        if (recovered && recoverFailure is null && bridgeFailure is null)
        {
            opened?.Finish();
        }

        var failures = new List<Exception>();

        if (!ran.Ok)
        {
            foreach (var line in Tail(log))
            {
                onOutput?.Invoke(line);
            }

            failures.Add(new InvalidOperationException($"{entry.Name} exited with {ran.ExitCode}"));
        }

        if (recoverFailure is not null)
        {
            failures.Add(recoverFailure);
        }

        if (bridgeFailure is not null)
        {
            failures.Add(bridgeFailure);
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }

        Say($"{entry.Name} closed.");
    }

    private static void Settle(Prefixes prefixes, string where, string? logTo = null) =>
        prefixes.Run(where, "wineserver", ["-k"], logTo: logTo);

    public StopOutcome Stop(
        LibraryEntry entry, TimeSpan? grace = null, Action<string>? onOutput = null)
    {
        if (entry.Launch is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is a plugin, not an application Cabinet can open");
        }

        var where = Where(entry);
        var prefixes = new Prefixes(layout, runner);
        var log = layout.PrefixLaunchLog(where);
        var waiting = grace ?? StopGrace;

        void Say(string line)
        {
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
        }

        Say($"Closing {entry.Name}.");
        prefixes.RunJoined(where, ["taskkill", "/f", "/im", entry.LaunchExe!], logTo: log);

        if (entry.LaunchService is { } service)
        {
            Say($"Stopping {service}.");
            prefixes.RunJoined(where, ["sc", "stop", service], logTo: log);
        }

        var deadline = DateTime.UtcNow + waiting;

        while (Running(prefixes, where, entry.LaunchExe!))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return Force(entry, prefixes, where, waiting, log, Say);
            }

            Thread.Sleep(Beat);
        }

        Helper(entry, prefixes, where, log, Say);
        Say($"{entry.Name} is closed.");

        return new StopOutcome(StopResult.Closed, $"{entry.Name} is closed.");
    }

    private StopOutcome Force(
        LibraryEntry entry,
        Prefixes prefixes,
        string where,
        TimeSpan waiting,
        string log,
        Action<string> say)
    {
        var late = $"{entry.Name} was still running {waiting.TotalSeconds:0} seconds later.";

        try
        {
            using var guard = prefixes.Guard(where, "end its Wine");

            say($"{late} Ending every Wine process in {where}, Cabinet's own included.");
            prefixes.RunJoined(where, ["wineboot", "-k"], logTo: log);

            if (Running(prefixes, where, entry.LaunchExe!))
            {
                var left = $"{late} It survived being ended, so it is still running.";
                say(left);

                return new StopOutcome(StopResult.LeftRunning, left);
            }

            Helper(entry, prefixes, where, log, say);
            var ended = $"{entry.Name} would not close, so Cabinet ended Wine in {where}.";
            say(ended);

            return new StopOutcome(StopResult.Forced, ended);
        }
        catch (PrefixInUseException busy)
        {
            var left = $"{late} {busy.Message}";
            say(left);

            return new StopOutcome(StopResult.LeftRunning, left);
        }
    }

    private static void Helper(
        LibraryEntry entry, Prefixes prefixes, string where, string log, Action<string> say)
    {
        if (entry.LaunchHelper is { } helper)
        {
            say($"Closing {helper}.");
            prefixes.RunJoined(where, ["taskkill", "/f", "/im", helper], logTo: log);
        }
    }

    public LibraryEntry ForLink(string link)
    {
        var at = link.IndexOf(':');

        if (at < 1)
        {
            throw new InvalidOperationException($"{link} is not a link");
        }

        var scheme = link[..at].ToLowerInvariant();
        var entry = Entries().FirstOrDefault(candidate => candidate.Scheme == scheme)
            ?? throw new InvalidOperationException(
                $"no app in the library opens {scheme}: links");

        if (Installed().GetValueOrDefault(entry.Id) is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} opens {scheme}: links but is not installed");
        }

        return entry;
    }

    public void Open(string link, Action<string>? onOutput = null)
    {
        var entry = ForLink(link);
        var where = Where(entry);
        var prefixes = new Prefixes(layout, runner);

        if (prefixes.SessionLive(where) && Running(prefixes, where, entry.LaunchExe!))
        {
            var log = layout.PrefixLaunchLog(where);
            var line = $"Handing the link to {entry.Name}.";
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
            var handed = prefixes.RunJoined(where, ["start", link], logTo: log);

            if (!handed.Ok)
            {
                throw new InvalidOperationException(
                    $"{entry.Name} was not handed the link (exit code {handed.ExitCode})");
            }

            return;
        }

        Launch(entry, onOutput, link);
    }

    private static bool Running(Prefixes prefixes, string where, string exe) =>
        prefixes.RunJoined(where, ["tasklist", "/fo", "csv", "/nh"])
            .Stdout.Contains($"\"{exe}\"", StringComparison.OrdinalIgnoreCase);

    public string? LaunchLog(LibraryEntry entry)
    {
        var sections = new List<string>();

        if (LogFile.Read(layout.InstallLogPath(entry.Id)) is { } install)
        {
            sections.Add($"Cabinet installation log{Environment.NewLine}{install}");
        }

        if (Installed().GetValueOrDefault(entry.Id) is { } prefix
            && LogFile.Read(layout.PrefixLaunchLog(prefix)) is { } launch)
        {
            sections.Add($"Cabinet launch log{Environment.NewLine}{launch}");
        }

        if (LogFile.Read(layout.RuntimeLogPath) is { } runtime)
        {
            sections.Add($"yabridge runtime log (shared){Environment.NewLine}{runtime}");
        }

        return sections.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, sections)
            : null;
    }

    private static IEnumerable<string> Tail(string log) =>
        File.Exists(log)
            ? File.ReadLines(log)
                .Where(line => line.Trim().Length > 0)
                .TakeLast(20)
            : [];

    private string? KeepDir(string where, LibraryEntry entry) =>
        entry.Keep is { } keep
            ? Path.Combine(
                layout.PrefixPath(where), keep.Replace('/', Path.DirectorySeparatorChar))
            : null;

    private void Hold(
        string? source, string destination, ISet<string> kept, Action<string> onOutput)
    {
        if (source is null || !Directory.Exists(source))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);

            if (!kept.Add(name))
            {
                continue;
            }

            Directory.CreateDirectory(destination);

            var linked = runner.Run("ln", ["-f", file, Path.Combine(destination, name)]);

            onOutput(linked.Ok
                ? $"  keeping {name}"
                : $"  could not keep {name} (exit code {linked.ExitCode})");
        }
    }

    private static void Narrate(PluginChange change, Action<string>? onOutput)
    {
        foreach (var bundle in change.Appeared)
        {
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} appeared");
        }

        foreach (var bundle in change.Gone)
        {
            onOutput?.Invoke($"  removed {Path.GetFileName(bundle)}");
        }
    }

    public void Remove(
        Removal agreed,
        bool takePrefix = false,
        UninstallEntry? uninstaller = null,
        Action<string>? onOutput = null)
    {
        var entry = agreed.Entry;
        using var claim = agreed.Prefix is { } held
            ? new Prefixes(layout, runner).Claim(held, $"take {entry.Name} out of {held}")
            : null;
        var current = RemovalOf(entry);

        if (!current.Agrees(agreed))
        {
            throw new InvalidOperationException(
                $"{entry.Name} or the prefix holding it changed since you were asked, so nothing "
                + "was removed — look again before removing it");
        }

        switch (current.Kind)
        {
            case RemovalKind.Native:
                RemoveNative(entry, onOutput);
                return;
            case RemovalKind.TakesPrefix when !takePrefix:
                throw new InvalidOperationException(
                    $"{entry.Name}'s own uninstaller leaves everything it downloaded behind, so "
                    + "it goes only with its prefix");
            case RemovalKind.KeepsPrefix when takePrefix:
                throw new InvalidOperationException(
                    $"{current.Prefix} also holds {string.Join(" and ", current.Sharing)}, so "
                    + $"it stays when {entry.Name} goes");
        }

        if (takePrefix)
        {
            new Prefixes(layout, runner).Delete(current.Prefix!, onOutput);
            onOutput?.Invoke($"{entry.Name} and the prefix that held it are gone.");
            return;
        }

        RemoveWindows(entry, current.Prefix!, uninstaller, onOutput);
    }

    private void RemoveWindows(
        LibraryEntry entry, string prefix, UninstallEntry? uninstaller, Action<string>? onOutput)
    {
        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"take {entry.Name} out of {prefix}");
        var recorded = RecordedKeys(prefix, entry.Id).ToList();
        var chosen = recorded.Count > 0
            ? Uninstallers(prefix)
                .Where(one => recorded.Contains(one.Key, StringComparer.Ordinal))
                .ToList()
            : Chosen(entry, prefix, Candidates(prefix, entry), uninstaller);

        if (chosen.Count == 0)
        {
            throw new InvalidOperationException(NotFound(entry, prefix));
        }

        var before = Bundled(prefix);

        foreach (var one in chosen)
        {
            onOutput?.Invoke($"Uninstalling {one.Name}…");
            Uninstall(prefixes, prefix, one.Command, onOutput);
        }

        var gone = before.Except(Bundled(prefix), StringComparer.Ordinal).ToList();

        if (gone.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s uninstaller left every plugin in {prefix} where it was, so "
                + "nothing has been removed — a cancelled uninstaller looks exactly like this");
        }

        foreach (var bundle in gone.OrderBy(path => path, StringComparer.Ordinal))
        {
            onOutput?.Invoke($"  removed {Path.GetFileName(bundle)}");
        }

        Forget(prefix, entry.Id);
        Bridge(prefixes, onOutput);
        onOutput?.Invoke($"{entry.Name} is gone from {prefix}, which stays.");
    }

    private const string Batch = "cabinet-uninstall.bat";

    private void Uninstall(
        Prefixes prefixes, string prefix, string command, Action<string>? onOutput)
    {
        var script = Path.Combine(layout.PrefixPath(prefix), "drive_c", Batch);
        File.WriteAllText(script, command + "\r\n");

        try
        {
            prefixes.Run(prefix, "wine", ["cmd", "/c", @"C:\" + Batch], onOutput);
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static IReadOnlyList<UninstallEntry> Chosen(
        LibraryEntry entry,
        string prefix,
        IReadOnlyList<UninstallEntry> possible,
        UninstallEntry? uninstaller)
    {
        if (uninstaller is not null)
        {
            return possible.FirstOrDefault(one => one.Key == uninstaller.Key) is { } current
                ? [current]
                : throw new InvalidOperationException(
                    $"{uninstaller.Name} is not an uninstaller that could be {entry.Name}'s");
        }

        return possible.Count > 1
            ? throw new InvalidOperationException(
                $"{string.Join(" and ", possible.Select(one => one.Name))} could each be "
                + $"{entry.Name}'s uninstaller in {prefix}, so Cabinet will not guess — choose one")
            : possible;
    }

    public static string NotFound(LibraryEntry entry, string prefix) =>
        $"Nothing in prefix {prefix} looks like {entry.Name}'s uninstaller, so there is no way "
        + $"to take it out on its own — deleting {prefix} removes it with everything in the "
        + "prefix";

    private IEnumerable<string> Registered(string prefix) =>
        Uninstallers(prefix).Select(one => one.Key).ToList();

    private IReadOnlySet<string> Bundled(string prefix) =>
        layout.PrefixPluginDirs(prefix)
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFileSystemEntries)
            .ToHashSet(StringComparer.Ordinal);

    private void RemoveNative(LibraryEntry entry, Action<string>? onOutput)
    {
        var id = entry.Id;
        var root = layout.NativePath(id);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"{id} is not installed");
        }

        using var installing = Underway.Begin(layout.NativeInstalling(id))
                               ?? throw new InvalidOperationException(
                                   $"Cabinet is installing {entry.Name} right now — wait for "
                                   + "that to finish");

        foreach (var link in LinksInto(root).ToList())
        {
            File.Delete(link);
            onOutput?.Invoke($"  unlinked {Path.GetFileName(link)}");
        }

        if (entry.Data is { } relative)
        {
            var data = layout.DataPath(relative);

            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
                onOutput?.Invoke($"  removed {data}");
            }
        }

        using (var removing = Staging.Create(layout.NativeDir, "plugin"))
        {
            Directory.Move(root, Path.Combine(removing.Path, id));
        }

        installing.Finish();
        onOutput?.Invoke($"{id} and everything it linked are gone.");
    }

    private void InstallWindows(
        LibraryEntry entry,
        string prefix,
        string? installer,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        if (entry.Source == PluginSource.Byo && installer is null && entry.DemoUrl is null)
        {
            throw new InvalidOperationException(Undownloadable(entry));
        }

        if (installer is not null && !File.Exists(installer))
        {
            throw new FileNotFoundException($"no such file: {installer}", installer);
        }

        var prefixes = new Prefixes(layout, runner);
        var existing = prefixes.List().FirstOrDefault(one => one.Name == prefix);
        Directory.CreateDirectory(layout.PrefixPath(prefix));
        using var claim = prefixes.Claim(prefix, $"install {entry.Name} into {prefix}");
        using var underway = Underway.Begin(layout.PrefixInstalling(prefix))
                             ?? throw new PrefixInUseException(
                                 $"Cabinet is already installing into {prefix}, so it will not "
                                 + $"install {entry.Name} there yet — wait for that to finish.");
        var left = Pending.Parse(underway.Left);
        var created = existing is null || left is { Created: true } && !Recorded(prefix).Any();
        var pending = new Pending(entry.Id, created, left?.Id == entry.Id ? left.Keys : []);
        underway.Note(pending.ToString());

        if (!created && entry.Runner is { } wanted && !Answers(existing!.Runner, wanted))
        {
            onOutput?.Invoke(
                $"{prefix} keeps {existing.Runner}; {entry.Name} would rather have Wine {wanted}.");
        }

        prefixes.Prepare(
            prefix,
            created && entry.Runner is { } spec
                ? EnsureRunner(spec, onOutput, onProgress)
                : null,
            onOutput);

        if (entry.Env.Count > 0)
        {
            var settings = new PrefixSettings(layout);
            var current = settings.Variables(prefix);
            var added = entry.Env.Keys.Where(key => !current.ContainsKey(key)).ToList();

            foreach (var key in added)
            {
                settings.SetVariable(prefix, key, entry.Env[key]);
            }

            if (added.Count > 0)
            {
                onOutput?.Invoke($"Set {string.Join(", ", added)}.");
            }
        }

        if (entry.Winetricks.Count > 0)
        {
            var result = new Winetricks(layout, runner).Apply(prefix, entry.Winetricks, onOutput);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"{entry.Name}'s Winetricks dependencies exited with {result.ExitCode}");
            }
        }

        using (var staging = Staging.Create(layout.TempDir, "library"))
        {
            var chosen = installer ?? Fetch(entry, staging.Path, onOutput, onProgress);
            var before = Registered(prefix);

            if (entry.Script is null)
            {
                var result = prefixes.RunInstaller(prefix, chosen, onOutput);

                if (!result.Ok)
                {
                    throw new InvalidOperationException(
                        $"the {entry.Name} installer exited with {result.ExitCode}");
                }
            }
            else
            {
                new InstallScript(layout, runner).Run(
                    entry,
                    chosen,
                    staging.Path,
                    layout.PrefixPath(prefix),
                    prefixes.Variables(prefix),
                    onOutput);

                Settle(prefixes, prefix);
            }

            var appeared = Registered(prefix).Except(before, StringComparer.Ordinal).ToList();

            pending = pending with
            {
                Keys = appeared.Count > 0 ? appeared
                    : pending.Keys.Count > 0 ? pending.Keys
                    : [.. RecordedKeys(prefix, entry.Id)],
            };
            underway.Note(pending.ToString());
        }

        var dxvk = new Dxvk(layout, runner);

        if (entry.Dxvk && dxvk.InstalledIn(prefix) is null)
        {
            dxvk.Install(prefix, onOutput, onProgress);
        }

        var desktop = new VirtualDesktop(layout, runner);

        if (entry.Desktop && !desktop.EnabledIn(prefix))
        {
            desktop.Set(prefix, onOutput);
        }

        if (created && entry.Sync != SyncMode.System)
        {
            prefixes.SetSync(prefix, entry.Sync);
            onOutput?.Invoke($"Sync mode {PrefixSettings.Word(entry.Sync)}.");
        }

        Record(prefix, entry.Id, pending.Keys);
        Bridge(prefixes, onOutput);
        underway.Finish();
        ForgetUnfinished(entry, prefix);
    }

    private void ForgetUnfinished(LibraryEntry entry, string finished)
    {
        foreach (var (id, elsewhere) in Unfinished())
        {
            if (id == entry.Id && elsewhere is { } other && other != finished
                && Underway.Begin(layout.PrefixInstalling(other)) is { } left)
            {
                left.Finish();
            }
        }
    }

    private void InstallNative(
        LibraryEntry entry,
        string? supplied,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        if (entry.Source == PluginSource.Byo && supplied is null)
        {
            throw new InvalidOperationException(Undownloadable(entry));
        }

        if (supplied is not null && !File.Exists(supplied))
        {
            throw new FileNotFoundException($"no such file: {supplied}", supplied);
        }

        var root = layout.NativePath(entry.Id);
        var marker = layout.NativeInstalling(entry.Id);
        var data = entry.Data is { } relative ? layout.DataPath(relative) : null;
        var interrupted = Underway.Marked(marker);

        if (!interrupted && Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"{entry.Name} is installed already — remove it first");
        }

        using var underway = Underway.Begin(marker)
                             ?? throw new InvalidOperationException(
                                 $"Cabinet is already installing {entry.Name} — wait for that to "
                                 + "finish");
        var links = new List<(string Link, string? Replaced)>();
        var madeData = false;

        try
        {
            if (underway.Left is { } left)
            {
                onOutput?.Invoke($"Clearing what an unfinished install of {entry.Name} left.");
                Clear(root, left.Split('\n').ElementAtOrDefault(1) == data ? data : null);
            }

            underway.Note(entry.Id);

            if (data is not null && Directory.Exists(data))
            {
                throw new InvalidOperationException(
                    $"{data} is already there — {entry.Name} keeps its presets in it, so move it "
                    + "aside first");
            }

            using var staging = Staging.Create(layout.TempDir, "library");
            var archive = supplied ?? Fetch(entry, staging.Path, onOutput, onProgress);
            Directory.CreateDirectory(root);

            if (data is not null)
            {
                underway.Note($"{entry.Id}\n{data}");
                Directory.CreateDirectory(data);
                madeData = true;
                onOutput?.Invoke($"Its presets and resources go in {data}.");
            }

            Lay(entry, archive, root, data, staging.Path, onOutput);
            Relink(entry, root, onOutput);
            Link(entry, root, links, onOutput);
        }
        catch
        {
            Unlink(links);
            Discard(root);

            if (madeData)
            {
                Discard(data!);
            }

            underway.Finish();
            throw;
        }

        underway.Finish();
    }

    private void Clear(string root, string? data)
    {
        foreach (var link in LinksInto(root).ToList())
        {
            File.Delete(link);
        }

        Discard(root);

        if (data is not null)
        {
            Discard(data);
        }
    }

    private string EnsureRunner(
        string spec, Action<string>? onOutput, Action<double>? onProgress)
    {
        var runners = new Runners(layout, runner);

        if (runners.List().FirstOrDefault(one => Answers(one.Name, spec)) is { } already)
        {
            return already.Name;
        }

        onOutput?.Invoke($"Fetching Wine {spec}, which this plugin's editor needs.");
        return runners.Install(new RunnerIndex(runner).Find(spec), onOutput, onProgress).Name;
    }

    public static bool Answers(string name, string spec) =>
        name == spec
        || RunnerIndex.Families.Any(family => Runners.DeriveName(family.AssetFor(spec)) == name)
        || RunnerIndex.MatchesFixedRunner(name, spec);

    private string Fetch(
        LibraryEntry entry,
        string staging,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        var url = entry.DemoUrl ?? entry.Url!;
        var checksum = entry.DemoSha256 ?? entry.Sha256;
        var target = Path.Combine(staging, ArchiveName(url, entry.Kind));

        http.ToFile(url, target, onOutput, onProgress);

        if (checksum is { } expected)
        {
            onOutput?.Invoke($"Checking sha256 {expected[..12]}…");
            Checksum.Expect(target, expected);
        }
        else
        {
            onOutput?.Invoke(Unverifiable(url));
        }

        return target;
    }

    public static string ArchiveName(LibraryEntry entry) =>
        ArchiveName(entry.Url ?? entry.DemoUrl!, entry.Kind);

    private static string ArchiveName(string url, PluginKind kind)
    {
        var path = url.TrimEnd('/');
        var name = path[(path.LastIndexOf('/') + 1)..];

        return kind == PluginKind.Windows && !Path.HasExtension(name)
            ? name + ".exe"
            : name;
    }

    private static string Undownloadable(LibraryEntry entry) =>
        $"{entry.Name} cannot be downloaded, so it needs the file you have"
        + (entry.Account is { } account ? $" from {account}" : "");

    public static string Unverifiable(string url) =>
        $"{new Uri(url).Host} publishes no checksum and changes this download with every "
        + "release, so nothing here can verify what arrives — only that it came from the "
        + "vendor over HTTPS.";

    private void Lay(
        LibraryEntry entry,
        string archive,
        string root,
        string? data,
        string staging,
        Action<string>? onOutput)
    {
        if (entry.Script is null)
        {
            Unpack(archive, root, onOutput);
            return;
        }

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CABINET_DEST"] = root,
        };

        if (data is not null)
        {
            variables["CABINET_DATA"] = data;
        }

        new InstallScript(layout, runner).Run(entry, archive, staging, root, variables, onOutput);
    }

    private void Unpack(string archive, string root, Action<string>? onOutput)
    {
        onOutput?.Invoke($"Unpacking {Path.GetFileName(archive)}");

        var result = archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? runner.Run("unzip", ["-q", "-o", archive, "-d", root], onOutput: onOutput)
            : runner.Run("tar", ["-xf", archive, "-C", root], onOutput: onOutput);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"could not unpack {Path.GetFileName(archive)}");
        }
    }

    private static void Relink(LibraryEntry entry, string root, Action<string>? onOutput)
    {
        if (entry.Relink.Count == 0)
        {
            return;
        }

        var wanted = entry.Relink.OrderBy(one => one.Key, StringComparer.Ordinal).ToList();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(file => new FileInfo(file).LinkTarget is null)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            foreach (var (soname, replacement) in wanted)
            {
                if (Elf.Relink(file, soname, replacement))
                {
                    onOutput?.Invoke(
                        $"  {Path.GetRelativePath(root, file)}: {soname} → {replacement}");
                }
            }
        }
    }

    private void Link(
        LibraryEntry entry,
        string root,
        List<(string Link, string? Replaced)> made,
        Action<string>? onOutput)
    {
        foreach (var bundle in Bundles(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var directory = layout.NativeScanDir(Path.GetExtension(bundle));
            Directory.CreateDirectory(directory);

            var link = Path.Combine(directory, Path.GetFileName(bundle));
            var replaced = new FileInfo(link).LinkTarget;

            if (replaced is null
                    ? Path.Exists(link)
                    : !Inside(link, replaced, root)
                      && !(Inside(link, replaced, layout.NativeDir) && !Path.Exists(link)))
            {
                throw new InvalidOperationException(
                    $"{link} is already there and is not one of Cabinet's links — move it aside");
            }

            made.Add((link, replaced));

            if (replaced is not null)
            {
                File.Delete(link);
            }

            File.CreateSymbolicLink(link, bundle);
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} → {directory}");
        }

        if (made.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s archive holds no .vst3, .clap, .lv2 or .so where a DAW "
                + "would find one");
        }
    }

    private static void Unlink(IEnumerable<(string Link, string? Replaced)> made)
    {
        foreach (var (link, replaced) in made.Reverse())
        {
            File.Delete(link);

            if (replaced is not null)
            {
                File.CreateSymbolicLink(link, replaced);
            }
        }
    }

    private static bool Inside(string link, string target, string directory) =>
        Path.GetFullPath(target, Path.GetDirectoryName(link)!)
            .StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private IEnumerable<string> LinksInto(string root)
    {
        foreach (var extension in Layout.PluginExtensions)
        {
            var directory = layout.NativeScanDir(extension);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var link in Directory.EnumerateFileSystemEntries(directory))
            {
                if (new FileInfo(link).LinkTarget is { } target && Inside(link, target, root))
                {
                    yield return link;
                }
            }
        }
    }

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

    private void Bridge(Prefixes prefixes, Action<string>? onOutput) =>
        new Yabridgectl(layout, runner).Bridge(prefixes.List(), onOutput);

    private static readonly IReadOnlyList<string> BundleDirectories =
        [".vst3", ".clap", ".vst", ".lv2", ".lxvst"];

    private static IEnumerable<string> Bundles(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (IsPlugin(entry))
            {
                yield return entry;
                continue;
            }

            if (!Directory.Exists(entry) || IsBundle(entry))
            {
                continue;
            }

            foreach (var nested in Directory.EnumerateFileSystemEntries(entry))
            {
                if (IsPlugin(nested))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsPlugin(string path) =>
        Layout.PluginExtensions.Contains(Path.GetExtension(path));

    private static bool IsBundle(string path) =>
        BundleDirectories.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static void Discard(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
