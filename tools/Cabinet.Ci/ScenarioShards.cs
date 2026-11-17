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
        foreach (var (shard, index) in dealt.Select((shard, index) => (shard, index)))
        {
            shards.Add(new JsonObject
            {
                ["shard"] = index,
                ["classes"] = string.Join(' ', shard.Classes.Select(name => Scenarios.Namespace + name)),
            });
        }

        Console.WriteLine(shards.ToJsonString());

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, Summary(dealt, timed: spent.Count > 0));
        }

        return 0;
    }

    public static string Summary(IReadOnlyList<Shard> dealt, bool timed)
    {
        var headline = timed
            ? $"at most {Minutes(dealt.Max(shard => shard.Load))} of scenario time per shard, run four at once"
            : "no earlier run left timings, so the scenarios were dealt by name";
        var lines = new List<string>
        {
            $"<details><summary>Shard plan: {headline}</summary>",
            "",
            "| Shard | Scenario time | Scenarios | Starts with |",
            "| ---: | ---: | ---: | --- |",
        };
        lines.AddRange(dealt.Select((shard, index) =>
            $"| {index} | {(timed ? Minutes(shard.Load) : "–")} | {shard.Classes.Count} | "
            + $"{string.Join(", ", shard.Classes.Take(2).Select(name => $"`{name}`"))} |"));
        lines.AddRange(["", "</details>", ""]);
        return string.Join('\n', lines) + "\n";
    }

    private static string Minutes(double seconds) => $"{Math.Round(seconds / 60)} min";

    public static IReadOnlyList<Shard> Deal(
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
        return [.. shards.Select(shard => new Shard(shard.Load, shard.Classes))];
    }

    public sealed record Shard(double Load, IReadOnlyList<string> Classes);
}
