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
