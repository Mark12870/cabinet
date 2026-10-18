using System.Text;
using System.Text.Json;

namespace Cabinet.Core;

internal sealed record PrefixSetup(
    string ConfigVersion,
    int Revision,
    string? Software,
    string? Runner,
    bool Dxvk,
    bool Ilok,
    SyncMode Sync,
    IReadOnlyList<string> Winetricks,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyDictionary<string, string> Registry,
    bool Desktop)
{
    public string Label => $"{ConfigVersion}, revision {Revision}";

    public static PrefixSetup From(PrefixConfig config, string? software) => new(
        config.Version,
        config.Revision,
        software,
        config.Runner,
        config.Dxvk,
        config.Ilok,
        config.Sync,
        [.. config.Winetricks.Select(verb => verb.ToLowerInvariant()).Order(StringComparer.Ordinal)],
        config.Env,
        config.Registry,
        config.Desktop);

    public string Serialise()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 2);
            writer.WriteString("configVersion", ConfigVersion);
            writer.WriteNumber("revision", Revision);
            writer.WriteString("software", Software);
            writer.WriteString("runner", Runner);
            writer.WriteBoolean("dxvk", Dxvk);
            writer.WriteBoolean("ilok", Ilok);
            writer.WriteString("sync", PrefixSettings.Word(Sync));
            writer.WriteStartArray("winetricks");

            foreach (var verb in Winetricks)
            {
                writer.WriteStringValue(verb);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("env");

            foreach (var (key, value) in Env.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("registry");

            foreach (var (path, data) in Registry.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(path, data);
            }

            writer.WriteEndObject();
            writer.WriteBoolean("desktop", Desktop);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".new";

        try
        {
            File.WriteAllText(temporary, Serialise());
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static PrefixSetup? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            if (root.GetProperty("version").GetInt32() != 2)
            {
                return null;
            }

            var env = Pairs(root.GetProperty("env"), StringComparer.Ordinal);
            var registry = root.TryGetProperty("registry", out var values)
                ? Pairs(values, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var verbs = root.GetProperty("winetricks").EnumerateArray()
                .Select(verb => verb.GetString() ?? throw new FormatException())
                .Select(verb => verb.ToLowerInvariant())
                .Order(StringComparer.Ordinal)
                .ToList();

            return new PrefixSetup(
                root.GetProperty("configVersion").GetString() ?? throw new FormatException(),
                root.GetProperty("revision").GetInt32(),
                root.GetProperty("software").GetString(),
                root.GetProperty("runner").GetString(),
                root.GetProperty("dxvk").GetBoolean(),
                root.TryGetProperty("ilok", out var ilok) && ilok.GetBoolean(),
                PrefixSettings.ParseSync(root.GetProperty("sync").GetString() ?? throw new FormatException()),
                verbs,
                env,
                registry,
                root.GetProperty("desktop").GetBoolean());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
                                     or KeyNotFoundException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Pairs(JsonElement element, StringComparer comparer) =>
        element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? throw new FormatException(),
            comparer);
}
