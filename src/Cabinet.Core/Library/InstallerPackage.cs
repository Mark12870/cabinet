using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Cabinet.Core;

public sealed record PackagedFile(string Source, string Target);

public sealed record PackagedValue(string Key, string? Name, string? Value, uint? Number);

public sealed record PackagePlan(IReadOnlyList<PackagedFile> Files, IReadOnlyList<PackagedValue> Values);

public sealed partial class InstallerPackage
{
    private const string NameAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._";
    private const char TableMark = '\u4840';
    private const int StringColumn = 0x0800;
    private const int Utf8CodePage = 65001;
    private const uint WideReferences = 0x80000000;

    private static readonly IReadOnlyDictionary<string, string> Folders = new Dictionary<string, string>
    {
        ["TARGETDIR"] = @"C:\",
        ["ROOTDRIVE"] = @"C:\",
        ["WindowsFolder"] = @"C:\windows\",
        ["System64Folder"] = @"C:\windows\system32\",
        ["SystemFolder"] = @"C:\windows\syswow64\",
        ["ProgramFiles64Folder"] = @"C:\Program Files\",
        ["ProgramFilesFolder"] = @"C:\Program Files (x86)\",
        ["CommonFiles64Folder"] = @"C:\Program Files\Common Files\",
        ["CommonFilesFolder"] = @"C:\Program Files (x86)\Common Files\",
        ["CommonAppDataFolder"] = @"C:\ProgramData\",
    };

    private static readonly IReadOnlyDictionary<int, string> Roots = new Dictionary<int, string>
    {
        [-1] = "HKEY_LOCAL_MACHINE",
        [0] = "HKEY_CLASSES_ROOT",
        [1] = "HKEY_CURRENT_USER",
        [2] = "HKEY_LOCAL_MACHINE",
        [3] = "HKEY_USERS",
    };

    private readonly CompoundFile file;
    private readonly Dictionary<string, string> streams;
    private readonly List<string> strings = [];
    private readonly int referenceSize;
    private readonly Dictionary<string, List<(int Number, string Name, int Type)>> schema =
        new(StringComparer.Ordinal);

    private InstallerPackage(byte[] image)
    {
        file = new CompoundFile(image);
        streams = file.Names.ToDictionary(Decoded, name => name, StringComparer.Ordinal);

        var pool = Stream("!_StringPool");
        var data = Stream("!_StringData");
        var codePage = BinaryPrimitives.ReadUInt32LittleEndian(pool);
        var encoding = (codePage & ~WideReferences) == Utf8CodePage ? Encoding.UTF8 : Encoding.Latin1;
        referenceSize = (codePage & WideReferences) != 0 ? 3 : 2;
        strings.Add("");

        for (int at = 4, offset = 0; at + 4 <= pool.Length; at += 4)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(pool.AsSpan(at));

            if (length == 0 && BinaryPrimitives.ReadUInt16LittleEndian(pool.AsSpan(at + 2)) != 0)
            {
                at += 4;
                length = (int)BinaryPrimitives.ReadUInt32LittleEndian(pool.AsSpan(at));
            }

            strings.Add(encoding.GetString(data, offset, length));
            offset += length;
        }

        var columns = Stream("!_Columns");
        var rows = columns.Length / (referenceSize * 2 + 4);
        var tables = Column(columns, 0, rows, referenceSize);
        var numbers = Column(columns, rows * referenceSize, rows, 2);
        var names = Column(columns, rows * (referenceSize + 2), rows, referenceSize);
        var types = Column(columns, rows * (referenceSize * 2 + 2), rows, 2);

        for (var row = 0; row < rows; row++)
        {
            var name = strings[(int)tables[row]];

            if (!schema.TryGetValue(name, out var table))
            {
                schema[name] = table = [];
            }

            table.Add(((int)numbers[row] - 0x8000, strings[(int)names[row]], (int)types[row]));
        }
    }

    public static InstallerPackage Read(string path) => new(File.ReadAllBytes(path));

    public IReadOnlyDictionary<string, string> Properties =>
        Table("Property").ToDictionary(row => row["Property"]!, row => row["Value"] ?? "", StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> CommandLine(string text)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match assignment in Assignment().Matches(text))
        {
            var quoted = assignment.Groups["quoted"];
            properties[assignment.Groups["name"].Value] = quoted.Success
                ? quoted.Value.Replace("\"\"", "\"", StringComparison.Ordinal)
                : assignment.Groups["bare"].Value;
        }

        return properties;
    }

    public PackagePlan Plan(IReadOnlyDictionary<string, string> given, string sourceDir)
    {
        var properties = new Dictionary<string, string>(Folders, StringComparer.Ordinal);

        foreach (var (name, value) in Properties.Concat(given))
        {
            properties[name] = value;
        }

        if (properties.TryGetValue("ADDLOCAL", out var features) && features != "ALL")
        {
            throw new NotSupportedException(
                $"Cabinet installs every feature of a package, not ADDLOCAL={features}");
        }

        var directories = Table("Directory").ToDictionary(row => row["Directory"]!, StringComparer.Ordinal);
        var components = Table("Component").ToDictionary(row => row["Component"]!, StringComparer.Ordinal);
        var targets = new Dictionary<string, string?>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        string? Target(string key)
        {
            if (targets.TryGetValue(key, out var known))
            {
                return known;
            }

            var row = directories[key];
            var parent = row["Directory_Parent"];
            var resolved = properties.TryGetValue(key, out var assigned) && assigned.Length > 0
                ? assigned
                : parent is null || parent == key
                    ? null
                    : Target(parent) is { } above ? Joined(above, LongName(row["DefaultDir"]!, source: false)) : null;

            return targets[key] = resolved;
        }

        string Source(string key)
        {
            if (sources.TryGetValue(key, out var known))
            {
                return known;
            }

            var row = directories[key];
            var parent = row["Directory_Parent"];

            return sources[key] = parent is null || parent == key
                ? sourceDir
                : Joined(Source(parent), LongName(row["DefaultDir"]!, source: true));
        }

        bool Installed(string component) => Holds(components[component]["Condition"], properties);

        string Value(string name) =>
            directories.ContainsKey(name) && Target(name) is { } directory
                ? directory.TrimEnd('\\') + '\\'
                : properties.GetValueOrDefault(name, "");

        var files = new List<PackagedFile>();

        foreach (var row in Table("File").Where(row => Installed(row["Component_"]!)))
        {
            var directory = components[row["Component_"]!]["Directory_"]!;
            var name = LongName(row["FileName"]!, source: false);
            var target = Target(directory)
                ?? throw new InvalidDataException(
                    $"nothing says where {name} goes: the package leaves {directory} unset");

            files.Add(new PackagedFile(Joined(Source(directory), name), Joined(target, name)));
        }

        var values = new List<PackagedValue>();

        foreach (var row in Table("Registry").Where(row => Installed(row["Component_"]!)))
        {
            var key = $@"{Roots[int.Parse(row["Root"]!)]}\{Formatted(row["Key"]!, Value)}";
            var name = row["Name"] is { } named && named is not ("+" or "-" or "*")
                ? Formatted(named, Value)
                : null;

            values.Add(row["Value"] switch
            {
                null => new PackagedValue(key, null, null, null),
                ['#', '#', .. var text] => new PackagedValue(key, name, "#" + Formatted(text, Value), null),
                ['#', .. var number] when uint.TryParse(number, out var parsed) =>
                    new PackagedValue(key, name, null, parsed),
                ['#', ..] or ['[', '~', ']', ..] => throw new NotSupportedException(
                    $"Cabinet writes only text and numbers to the registry, not {row["Value"]} in {key}"),
                var text => new PackagedValue(key, name, Formatted(text, Value), null),
            });
        }

        return new PackagePlan(files, values);
    }

    private static bool Holds(string? condition, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        var equality = Equality().Match(condition);

        if (!equality.Success)
        {
            throw new NotSupportedException(
                $"Cabinet understands only Property = \"Value\" component conditions, not {condition}");
        }

        return properties.GetValueOrDefault(equality.Groups["name"].Value) == equality.Groups["value"].Value;
    }

    private static string Formatted(string text, Func<string, string> value) =>
        Reference().Replace(text, reference => reference.Groups["name"].Value switch
        {
            ['\\', var escaped] => escaped.ToString(),
            ['#' or '!' or '$' or '%' or '~', ..] => throw new NotSupportedException(
                $"Cabinet resolves only [Property] references, not {reference.Value}"),
            var name => value(name),
        });

    private static string LongName(string field, bool source)
    {
        var parts = field.Split(':');
        var chosen = source && parts.Length > 1 ? parts[1] : parts[0];
        return chosen.Split('|')[^1];
    }

    private static string Joined(string directory, string name) =>
        name == "." ? directory : $@"{directory.TrimEnd('\\')}\{name}";

    private List<Dictionary<string, string?>> Table(string name)
    {
        if (!schema.TryGetValue(name, out var described) || !streams.ContainsKey("!" + name))
        {
            return [];
        }

        var columns = described.OrderBy(column => column.Number).ToList();
        var data = Stream("!" + name);

        return Rows(columns, data, referenceSize)
            ?? Rows(columns, data, 5 - referenceSize)
            ?? throw new InvalidDataException($"the package's {name} table does not fit its columns");
    }

    private List<Dictionary<string, string?>>? Rows(
        List<(int Number, string Name, int Type)> columns, byte[] data, int references)
    {
        var widths = columns.Select(column => Width(column.Type, references)).ToList();

        if (data.Length % widths.Sum() != 0)
        {
            return null;
        }

        var count = data.Length / widths.Sum();
        var rows = Enumerable.Range(0, count).Select(_ => new Dictionary<string, string?>(StringComparer.Ordinal)).ToList();
        var offset = 0;

        foreach (var (column, width) in columns.Zip(widths))
        {
            var cells = Column(data, offset, count, width);
            var text = (column.Type & StringColumn) != 0;

            if (text && cells.Any(cell => cell >= strings.Count))
            {
                return null;
            }

            for (var row = 0; row < count; row++)
            {
                rows[row][column.Name] = cells[row] == 0
                    ? null
                    : text
                        ? strings[(int)cells[row]]
                        : (cells[row] - (width == 2 ? 0x8000 : 0x80000000L)).ToString();
            }

            offset += count * width;
        }

        return rows;
    }

    private static int Width(int type, int references) =>
        (type & StringColumn) != 0 ? references : (type & 0xFF) <= 2 ? 2 : 4;

    private static long[] Column(byte[] data, int offset, int count, int width)
    {
        var cells = new long[count];

        for (var row = 0; row < count; row++)
        {
            var at = offset + row * width;
            cells[row] = width switch
            {
                2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)),
                3 => data[at] | data[at + 1] << 8 | data[at + 2] << 16,
                _ => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)),
            };
        }

        return cells;
    }

    private byte[] Stream(string name) =>
        file.Read(streams.TryGetValue(name, out var stored)
            ? stored
            : throw new InvalidDataException($"not a Windows Installer package: it has no {name} table"));

    private static string Decoded(string stored)
    {
        var decoded = new StringBuilder();

        foreach (var character in stored)
        {
            switch (character)
            {
                case >= '\u3800' and < '\u4800':
                    var pair = character - 0x3800;
                    decoded.Append(NameAlphabet[pair & 0x3F]).Append(NameAlphabet[pair >> 6]);
                    break;
                case >= '\u4800' and < TableMark:
                    decoded.Append(NameAlphabet[character - 0x4800]);
                    break;
                case TableMark:
                    decoded.Append('!');
                    break;
                default:
                    decoded.Append(character);
                    break;
            }
        }

        return decoded.ToString();
    }

    [GeneratedRegex("""(?<name>[A-Za-z_][A-Za-z0-9_.]*)=(?:"(?<quoted>(?:[^"]|"")*)"|(?<bare>[^\s"]*))""")]
    private static partial Regex Assignment();

    [GeneratedRegex("""^\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*=\s*"(?<value>[^"]*)"\s*$""")]
    private static partial Regex Equality();

    [GeneratedRegex(@"\[(?<name>\\.|[^\[\]]+)\]")]
    private static partial Regex Reference();
}
