namespace Cabinet.Core;

/// <summary>
/// Just enough INI for the two files Cabinet reads: <c>/.flatpak-info</c> and a
/// Flatpak user override. Both are written by flatpak itself, so there is no need
/// to handle quoting, comments or continuations.
/// </summary>
public sealed class IniFile
{
    public static readonly IniFile Empty = new([]);

    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    private IniFile(Dictionary<string, Dictionary<string, string>> sections) => _sections = sections;

    public static IniFile Parse(IEnumerable<string> lines)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        sections[""] = current;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1];
                if (!sections.TryGetValue(name, out var section))
                {
                    section = new Dictionary<string, string>(StringComparer.Ordinal);
                    sections[name] = section;
                }

                current = section;
                continue;
            }

            var split = line.IndexOf('=');
            if (split > 0)
            {
                current[line[..split].Trim()] = line[(split + 1)..].Trim();
            }
        }

        return new IniFile(sections);
    }

    public string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
            ? value
            : null;
}
