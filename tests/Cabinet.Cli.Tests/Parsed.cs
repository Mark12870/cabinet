using System.Text.Json;

namespace Cabinet.Cli.Tests;

internal static class Parsed
{
    public static IReadOnlyList<string> Ids(string json) =>
        [.. Objects(json).Select(entry => entry.GetProperty("id").GetString()!)];

    public static IReadOnlyList<string> Keys(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name)];

    public static IReadOnlyList<JsonElement> Objects(string json) =>
        [.. JsonDocument.Parse(json).RootElement.EnumerateArray()];
}
