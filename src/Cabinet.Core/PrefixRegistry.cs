using System.Text;

namespace Cabinet.Core;

public sealed record UninstallEntry(string Key, string Name, string Command);

public sealed class PrefixRegistry(Layout layout)
{
    private static readonly IReadOnlyList<string> Uninstall =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\",
    ];

    public IReadOnlyList<UninstallEntry> Uninstallers(string prefix) =>
    [
        .. Entries(layout.PrefixSystemReg(prefix), "HKLM"),
        .. Entries(layout.PrefixUserReg(prefix), "HKCU"),
    ];

    public string? Lookup(string prefix, string key, string name)
    {
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
