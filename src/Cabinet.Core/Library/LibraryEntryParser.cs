namespace Cabinet.Core;

internal static class LibraryEntryParser
{
    public static LibraryEntry Parse(string id, string text, string vendor)
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
