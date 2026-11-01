using System.Reflection;
using System.Text.RegularExpressions;
using Xunit.Sdk;
using Xunit.v3;

namespace Cabinet.Runtime.Tests.Scenarios;

public sealed partial class LongestFirst : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : notnull, ITestCollection
    {
        var planned = (Environment.GetEnvironmentVariable("CABINET_RUNTIME_CLASSES") ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        return
        [
            .. testCollections
                .OrderBy(collection => Rank(Class(collection), planned))
                .ThenBy(collection => collection.TestCollectionDisplayName, StringComparer.Ordinal),
        ];
    }

    private static int Rank(string name, List<string> planned) =>
        planned.IndexOf(name) is var at and >= 0
            ? at
            : planned.Count + (Id(name) is { } id ? (InstalledEntry.Find(id).Manager ? 0 : 1) : 2);

    private static string Class(ITestCollection collection) =>
        DisplayName().Match(collection.TestCollectionDisplayName) is { Success: true } found ? found.Groups[1].Value : "";

    private static string? Id(string name) =>
        typeof(LongestFirst).Assembly.GetType(name)
            ?.GetField("Id", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string;

    [GeneratedRegex(@"^Test collection for (\S+)")]
    private static partial Regex DisplayName();
}
