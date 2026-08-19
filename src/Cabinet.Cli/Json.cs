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
