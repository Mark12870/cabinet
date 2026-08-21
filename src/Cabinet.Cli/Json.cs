using System.Text;
using System.Text.Json;
using Cabinet.Core;

namespace Cabinet.Cli;

internal static class Json
{
    public static string Checks(IReadOnlyList<Check> checks) =>
        Write(writer =>
        {
            writer.WriteStartArray();
            foreach (var check in checks)
            {
                writer.WriteStartObject();
                writer.WriteString("name", check.Name);
                writer.WriteString("status", check.Status.ToString().ToLowerInvariant());
                writer.WriteString("detail", check.Detail);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    public static string Build(Build build) =>
        Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("version", build.Version);
            writer.WriteString("remote", build.Remote);
            writer.WriteString("url", build.Url);
            writer.WriteString("commit", build.Commit);
            writer.WriteString("origin", build.Origin.ToString().ToLowerInvariant());
            writer.WriteString("yabridge", build.Yabridge);
            writer.WriteString("wine", build.Wine);
            writer.WriteString("homepage", build.Homepage);
            writer.WriteString("bugtracker", build.BugTracker);
            writer.WriteEndObject();
        });

    public static string Prefixes(IReadOnlyList<Prefix> prefixes) =>
        Write(writer =>
        {
            writer.WriteStartArray();
            foreach (var prefix in prefixes)
            {
                writer.WriteStartObject();
                writer.WriteString("name", prefix.Name);
                writer.WriteString("path", prefix.Path);
                writer.WriteBoolean("initialised", prefix.Initialised);
                writer.WriteString("runner", prefix.Runner);
                writer.WriteString("dxvk", prefix.Dxvk);
                writer.WriteString("sync", PrefixSettings.Word(prefix.Sync));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    public static string Library(
        IReadOnlyList<LibraryEntry> entries,
        IReadOnlyDictionary<string, string?> installed) =>
        Write(writer =>
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("name", entry.Name);
                writer.WriteString("kind", entry.Kind.ToString().ToLowerInvariant());
                writer.WriteString("category", entry.Category);
                writer.WriteString("summary", entry.Summary);
                writer.WriteString("homepage", entry.Homepage);
                writer.WriteString("source", entry.Source.ToString().ToLowerInvariant());
                writer.WriteString("account", entry.Account);
                writer.WriteString("prefix", entry.Kind == PluginKind.Native ? null : entry.Prefix);
                writer.WriteString("runner", entry.Runner);
                writer.WriteBoolean("dxvk", entry.Dxvk);
                writer.WriteString("sync", PrefixSettings.Word(entry.Sync));
                writer.WriteString("script", entry.Script);
                writer.WriteString("data", entry.Data);
                writer.WriteString("developer", entry.Developer);
                writer.WriteString("version", entry.Version);
                writer.WriteString("licence", entry.Licence);
                writer.WriteString("licensing", entry.Licensing);
                Strings(writer, "formats", entry.Formats);
                Strings(writer, "description", entry.Description);
                writer.WriteBoolean("installed", installed.ContainsKey(entry.Id));
                writer.WriteString("installedIn", installed.GetValueOrDefault(entry.Id));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    private static void Strings(
        Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            body(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
