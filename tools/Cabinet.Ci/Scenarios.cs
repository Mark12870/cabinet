using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cabinet.Ci;

internal static partial class Scenarios
{
    public const string Namespace = "Cabinet.Runtime.Tests.Scenarios.";

    public static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static string Repo { get; } = FindRepo();

    public static IReadOnlyDictionary<string, string> Entries()
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        var library = Path.Combine(Repo, "data", "library");

        foreach (var scenario in Directory.GetFiles(Path.Combine(Repo, "tests/Cabinet.Runtime.Tests/Scenarios"), "*.cs")
                     .Order(StringComparer.Ordinal))
        {
            if (IdPattern().Match(File.ReadAllText(scenario)) is { Success: true } found)
            {
                named[Path.GetFileNameWithoutExtension(scenario)] = Directory.EnumerateDirectories(library)
                    .Select(vendor => Path.Combine(vendor, found.Groups[1].Value + ".yml"))
                    .First(File.Exists);
            }
        }

        return named;
    }

    public static IEnumerable<(string Scenario, string Format, XElement Result)> Results(string trx)
    {
        if (!File.Exists(trx))
        {
            yield break;
        }

        foreach (var result in XDocument.Load(trx).Descendants(Trx + "UnitTestResult"))
        {
            if (TestPattern().Match((string?)result.Attribute("testName") ?? "") is { Success: true } found)
            {
                yield return (found.Groups[1].Value, found.Groups[2].Value, result);
            }
        }
    }

    public static string Field(string entry, string name) =>
        Regex.Match(File.ReadAllText(entry), $"^{name}: *(.+)$", RegexOptions.Multiline) is { Success: true } found
            ? found.Groups[1].Value.Trim()
            : "";

    public static string Stem(string path) => Path.GetFileNameWithoutExtension(path);

    public static IReadOnlyList<string> Lines(string text)
    {
        var lines = LineBreak().Split(text).ToList();

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cabinet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"no Cabinet.slnx above {Environment.CurrentDirectory}");
    }

    [GeneratedRegex("""const string Id = "([^"]+)";""")]
    private static partial Regex IdPattern();

    [GeneratedRegex("""^Cabinet\.Runtime\.Tests\.Scenarios\.(\w+)\.\w+\(format: "([^"]+)"\)$""")]
    private static partial Regex TestPattern();

    [GeneratedRegex(@"\r\n|[\n\r\v\f\x1c\x1d\x1e\x85\p{Zl}\p{Zp}]")]
    private static partial Regex LineBreak();
}
