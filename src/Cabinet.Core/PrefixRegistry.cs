using System.Text;

namespace Cabinet.Core;

public sealed record UninstallEntry(string Key, string Name, string Command);

public sealed class PrefixRegistry(Layout layout, IProcessRunner? runner = null)
{
    private static readonly IReadOnlyList<string> Uninstall =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\",
    ];

    public IReadOnlyList<UninstallEntry> Uninstallers(string prefix)
    {
        if (Live(prefix) is { } wine)
        {
            return LiveEntries(wine, prefix);
        }

        return
        [
            .. Entries(layout.PrefixSystemReg(prefix), "HKLM"),
            .. Entries(layout.PrefixUserReg(prefix), "HKCU"),
        ];
    }

    public string? Lookup(string prefix, string key, string name)
    {
        if (Live(prefix) is { } wine)
        {
            var result = wine.RunJoined(prefix, ["reg", "query", $@"HKCU\{key}", "/v", name]);

            return result.Ok ? QueryValue(result.Stdout, name) : null;
        }

        var path = layout.PrefixUserReg(prefix);

        if (!File.Exists(path))
        {
            return null;
        }

        string? section = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('['))
            {
                section = Section(line);
            }
            else if (string.Equals(section, key, StringComparison.OrdinalIgnoreCase)
                     && Value(line) is { } pair
                     && string.Equals(pair.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Text;
            }
        }

        return null;
    }

    private Prefixes? Live(string prefix)
    {
        if (runner is null)
        {
            return null;
        }

        var prefixes = new Prefixes(layout, runner);

        return prefixes.SessionLive(prefix) ? prefixes : null;
    }

    private static IReadOnlyList<UninstallEntry> LiveEntries(Prefixes wine, string prefix)
    {
        var entries = new List<UninstallEntry>();

        foreach (var root in new[] { "HKLM", "HKCU" })
        {
            foreach (var branch in Uninstall)
            {
                var key = $@"{root}\{branch.TrimEnd('\\')}";
                var result = wine.RunJoined(prefix, ["reg", "query", key, "/s"]);

                if (result.Ok)
                {
                    entries.AddRange(QueryEntries(result.Stdout));
                }
            }
        }

        return entries;
    }

    private static IEnumerable<UninstallEntry> QueryEntries(string output)
    {
        string? key = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n'))
        {
            var text = line.TrimEnd('\r');

            if (text.StartsWith("HKEY_", StringComparison.Ordinal))
            {
                if (Entry(KeyRoot(key), KeyPath(key), values) is { } closed)
                {
                    yield return closed;
                }

                key = text;
                values.Clear();
            }
            else if (key is not null && QueryPair(text) is { } pair)
            {
                values[pair.Name] = pair.Text;
            }
        }

        if (Entry(KeyRoot(key), KeyPath(key), values) is { } last)
        {
            yield return last;
        }
    }

    private static string? QueryValue(string output, string name) =>
        output.Split('\n')
            .Select(line => QueryPair(line.TrimEnd('\r')))
            .FirstOrDefault(pair => pair is not null
                                    && string.Equals(
                                        pair.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Text;

    private static (string Name, string Text)? QueryPair(string line)
    {
        var fields = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);

        return fields.Length == 3 && fields[1].StartsWith("REG_", StringComparison.Ordinal)
            ? (fields[0], fields[2])
            : null;
    }

    private static string KeyRoot(string? key) =>
        key?.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.Ordinal) == true
            ? "HKLM"
            : key?.StartsWith("HKEY_CURRENT_USER\\", StringComparison.Ordinal) == true
                ? "HKCU"
                : "";

    private static string? KeyPath(string? key) => key switch
    {
        { } machine when machine.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.Ordinal) =>
            machine["HKEY_LOCAL_MACHINE\\".Length..],
        { } user when user.StartsWith("HKEY_CURRENT_USER\\", StringComparison.Ordinal) =>
            user["HKEY_CURRENT_USER\\".Length..],
        _ => null,
    };

    private static IEnumerable<UninstallEntry> Entries(string path, string root)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        string? key = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('['))
            {
                if (Entry(root, key, values) is { } closed)
                {
                    yield return closed;
                }

                key = Section(line);
                values.Clear();
            }
            else if (key is not null && Value(line) is { } pair)
            {
                values[pair.Name] = pair.Text;
            }
        }

        if (Entry(root, key, values) is { } last)
        {
            yield return last;
        }
    }

    private static UninstallEntry? Entry(
        string root, string? key, IReadOnlyDictionary<string, string> values)
    {
        if (key is null || !IsUninstall(key))
        {
            return null;
        }

        var command = Present(values, "QuietUninstallString") ?? Present(values, "UninstallString");

        return command is null
            ? null
            : new UninstallEntry(
                $@"{root}\{key}",
                Present(values, "DisplayName") ?? key[(key.LastIndexOf('\\') + 1)..],
                command);
    }

    private static string? Present(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    private static bool IsUninstall(string key) =>
        Uninstall.FirstOrDefault(
                under => key.StartsWith(under, StringComparison.OrdinalIgnoreCase))
            is { } branch
        && key[branch.Length..] is { Length: > 0 } leaf
        && !leaf.Contains('\\');

    private static string? Section(string line) =>
        line.LastIndexOf(']') is var end && end > 1 ? Unescape(line[1..end]) : null;

    private static (string Name, string Text)? Value(string line)
    {
        if (Quoted(line, 0) is not { } name
            || name.After >= line.Length
            || line[name.After] != '='
            || Quoted(line, Untyped(line, name.After + 1)) is not { } text)
        {
            return null;
        }

        return (name.Text, text.Text);
    }

    private static int Untyped(string line, int at)
    {
        if (!line.AsSpan(at).StartsWith("str"))
        {
            return at;
        }

        var colon = line.IndexOf(':', at);

        return colon < 0 ? at : colon + 1;
    }

    private static (string Text, int After)? Quoted(string line, int at)
    {
        if (at >= line.Length || line[at] != '"')
        {
            return null;
        }

        var text = new StringBuilder();

        for (var read = at + 1; read < line.Length; read++)
        {
            if (line[read] == '"')
            {
                return (text.ToString(), read + 1);
            }

            if (line[read] == '\\' && read + 1 < line.Length)
            {
                read++;
            }

            text.Append(line[read]);
        }

        return null;
    }

    private static string Unescape(string text)
    {
        if (!text.Contains('\\'))
        {
            return text;
        }

        var plain = new StringBuilder(text.Length);

        for (var read = 0; read < text.Length; read++)
        {
            if (text[read] == '\\' && read + 1 < text.Length)
            {
                read++;
            }

            plain.Append(text[read]);
        }

        return plain.ToString();
    }
}
