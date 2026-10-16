using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Credentials
{
    public static IReadOnlyDictionary<string, string> Read(params string[] required) =>
        ReadFrom(File.ReadAllLines(Repo.Path("secrets.env")), required);

    public static IReadOnlyDictionary<string, string> ReadFrom(IEnumerable<string> lines, params string[] required)
    {
        var credentials = lines
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());

        var missing = required
            .Where(key => !credentials.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Required credentials are missing or blank: {string.Join(", ", missing)}.");
        }

        return credentials;
    }
}
