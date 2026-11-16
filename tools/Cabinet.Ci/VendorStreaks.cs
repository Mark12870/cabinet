using System.Globalization;
using System.Text.Json.Nodes;

namespace Cabinet.Ci;

internal sealed record Streak(DateOnly Since, int Runs, string Reason);

internal static class VendorStreaks
{
    public const int Limit = 3;

    public static int Update(string previous, string output, IReadOnlyList<string> artifacts)
    {
        var ids = Scenarios.Entries().ToDictionary(pair => pair.Key, pair => Scenarios.Stem(pair.Value), StringComparer.Ordinal);
        var streaks = Next(
            Read(previous).Where(pair => ids.ContainsValue(pair.Key)).ToDictionary(StringComparer.Ordinal),
            Ran(ids, artifacts),
            Outages(artifacts),
            DateOnly.FromDateTime(DateTime.UtcNow));
        File.WriteAllText(output, Write(streaks));
        Console.Write(Report(streaks));

        foreach (var (id, streak) in streaks.Where(pair => pair.Value.Runs >= Limit))
        {
            Console.Error.WriteLine(
                $"::error title=Vendor unavailable::{id}'s vendor has been unavailable for {streak.Runs} runs: {streak.Reason}");
        }

        return streaks.Values.Any(streak => streak.Runs >= Limit) ? 1 : 0;
    }

    internal static SortedDictionary<string, Streak> Next(
        IReadOnlyDictionary<string, Streak> previous,
        IReadOnlySet<string> ran,
        IReadOnlyDictionary<string, string> outages,
        DateOnly today)
    {
        var next = new SortedDictionary<string, Streak>(StringComparer.Ordinal);

        foreach (var (id, streak) in previous.Where(pair => !ran.Contains(pair.Key)))
        {
            next[id] = streak;
        }

        foreach (var (id, reason) in outages)
        {
            next[id] = previous.TryGetValue(id, out var streak)
                ? new Streak(streak.Since, streak.Runs + 1, reason)
                : new Streak(today, 1, reason);
        }

        return next;
    }

    internal static string Report(IReadOnlyDictionary<string, Streak> streaks)
    {
        if (streaks.Count == 0)
        {
            return "### Vendors unavailable\n\nEvery vendor answered.\n";
        }

        var lines = new List<string>
        {
            "### Vendors unavailable",
            "",
            $"A vendor unavailable for {Limit} runs in a row fails the run.",
            "",
            "| Entry | Runs | Since | Last reason |",
            "| --- | --- | --- | --- |",
        };
        lines.AddRange(streaks.Select(pair =>
            $"| `{pair.Key}` | {pair.Value.Runs} | {pair.Value.Since:yyyy-MM-dd} | {pair.Value.Reason.Replace('|', '/')} |"));
        return string.Join('\n', lines) + "\n";
    }

    internal static Dictionary<string, Streak> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonNode.Parse(File.ReadAllText(path))!.AsObject().ToDictionary(
            pair => pair.Key,
            pair => new Streak(
                DateOnly.ParseExact((string)pair.Value!["since"]!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                (int)pair.Value!["runs"]!,
                (string)pair.Value!["reason"]!),
            StringComparer.Ordinal);
    }

    internal static string Write(IReadOnlyDictionary<string, Streak> streaks)
    {
        var written = new JsonObject();

        foreach (var (id, streak) in streaks)
        {
            written[id] = new JsonObject
            {
                ["since"] = streak.Since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["runs"] = streak.Runs,
                ["reason"] = streak.Reason,
            };
        }

        return written.ToJsonString() + "\n";
    }

    private static HashSet<string> Ran(IReadOnlyDictionary<string, string> ids, IReadOnlyList<string> artifacts) =>
        artifacts
            .Select(artifact => Path.Combine(artifact, "TestResults", "runtime.trx"))
            .SelectMany(trx => ScenarioReport.Attempts(trx).Append(trx).Distinct(StringComparer.Ordinal))
            .SelectMany(Scenarios.Results)
            .Select(result => ids[result.Scenario])
            .ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> Outages(IReadOnlyList<string> artifacts) =>
        artifacts
            .Select(artifact => Path.Combine(artifact, "scenarios"))
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(scenario => (Id: Path.GetFileName(scenario), File: Path.Combine(scenario, "artefacts", VendorOutage.Record)))
            .Where(found => File.Exists(found.File))
            .ToDictionary(found => found.Id, found => File.ReadAllText(found.File).Trim(), StringComparer.Ordinal);
}
