using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Credentials
{
    public static IReadOnlyDictionary<string, string> Read() =>
        File.ReadAllLines(Repo.Path("credentials.env"))
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());
}
