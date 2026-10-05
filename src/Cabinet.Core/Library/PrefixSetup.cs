using System.Text;
using System.Text.Json;

namespace Cabinet.Core;

internal sealed record PrefixSetup(
    string ConfigVersion,
    int Revision,
    string? Software,
    string? Runner,
    bool Dxvk,
    SyncMode Sync,
    IReadOnlyList<string> Winetricks,
    IReadOnlyDictionary<string, string> Env,
    bool Desktop)
{
    public string Label => $"{ConfigVersion}, revision {Revision}";

    public static PrefixSetup From(PrefixConfig config, string? software) => new(
        config.Version,
        config.Revision,
        software,
        config.Runner,
        config.Dxvk,
        config.Sync,
        [.. config.Winetricks.Select(verb => verb.ToLowerInvariant()).Order(StringComparer.Ordinal)],
        config.Env,
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

            var env = root.GetProperty("env").EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.GetString() ?? throw new FormatException(),
                StringComparer.Ordinal);
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
                PrefixSettings.ParseSync(root.GetProperty("sync").GetString() ?? throw new FormatException()),
                verbs,
                env,
                root.GetProperty("desktop").GetBoolean());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
                                     or KeyNotFoundException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
