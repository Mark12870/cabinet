namespace Cabinet.Core;

public static class TextSearch
{
    public static bool Matches(string? search, params IEnumerable<string?> fields)
    {
        var haystack = string.Join(' ', fields);

        return (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
