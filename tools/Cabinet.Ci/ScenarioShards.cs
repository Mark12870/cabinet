using System.Text.Json.Nodes;

namespace Cabinet.Ci;

internal static class ScenarioShards
{
    private const string Helpers = "ImapOtpTests";

    public static int Plan(string previous, int count)
    {
        var spent = Directory.Exists(previous)
            ? Directory.EnumerateFiles(previous, "runtime.trx", SearchOption.AllDirectories)
                .Where(trx => Path.GetFileName(Path.GetDirectoryName(trx)) == "TestResults")
                .SelectMany(trx => ScenarioReport.Spent(trx))
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value), StringComparer.Ordinal)
            : [];

        var dealt = Deal(spent, [.. Scenarios.Entries().Keys], count);
        var shards = new JsonArray();
        foreach (var (classes, index) in dealt.Select((classes, index) => (classes, index)))
        {
            shards.Add(new JsonObject
            {
                ["shard"] = index,
                ["classes"] = string.Join(' ', classes.Select(name => Scenarios.Namespace + name)),
            });
        }

        Console.WriteLine(shards.ToJsonString());

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, Summary(dealt, spent));
        }

        return 0;
    }

    public static string Summary(IReadOnlyList<IReadOnlyList<string>> dealt, IReadOnlyDictionary<string, double> spent)
    {
        var unknown = spent.Count > 0 ? spent.Values.Max() : 0;
        var expected = dealt.Select(classes => classes.Sum(name => spent.GetValueOrDefault(name, name == Helpers ? 0 : unknown))).ToList();
        var lines = new List<string>
        {
            $"<details><summary>Shard plan: the longest shard should take about {Minutes(expected.Max())} of tests</summary>",
            "",
            "| Shard | Expected | Scenarios | Starts with |",
            "| ---: | ---: | ---: | --- |",
        };
        lines.AddRange(dealt.Select((classes, index) =>
            $"| {index} | {Minutes(expected[index])} | {classes.Count} | {string.Join(", ", classes.Take(2).Select(name => $"`{name}`"))} |"));
        lines.AddRange(["", "</details>", ""]);
        return string.Join('\n', lines) + "\n";
    }

    private static string Minutes(double seconds) => $"{Math.Round(seconds / 60)} min";

    public static IReadOnlyList<IReadOnlyList<string>> Deal(
        IReadOnlyDictionary<string, double> spent, IReadOnlyList<string> scenarios, int count)
    {
        var unknown = spent.Count > 0 ? spent.Values.Max() : 0;
        var shards = Enumerable.Range(0, count).Select(_ => (Load: 0.0, Classes: new List<string>())).ToList();

        foreach (var (scenario, weight) in scenarios
                     .Select(scenario => (scenario, weight: spent.GetValueOrDefault(scenario, unknown)))
                     .OrderByDescending(pair => pair.weight)
                     .ThenBy(pair => pair.scenario, StringComparer.Ordinal))
        {
            var lightest = shards.IndexOf(shards.MinBy(shard => (shard.Load, shard.Classes.Count)));
            shards[lightest] = (shards[lightest].Load + weight, shards[lightest].Classes);
            shards[lightest].Classes.Add(scenario);
        }

        shards.MinBy(shard => (shard.Load, shard.Classes.Count)).Classes.Add(Helpers);
        return [.. shards.Select(shard => (IReadOnlyList<string>)shard.Classes)];
    }
}
