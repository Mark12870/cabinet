using System.Globalization;
using System.Xml.Linq;

namespace Cabinet.Ci;

internal static class ScenarioReport
{
    private static readonly string[] Steps = ["prepare", "display", "install", "settle", "dispose"];
    private static readonly string[] Phases = ["runner", "prefix", "download", "installer", "dxvk", "bridge"];

    public static int Approve(string trx, string id)
    {
        var entry = Scenarios.Entries().Single(pair => Scenarios.Stem(pair.Value) == id);
        var formats = Scenarios.Formats(entry.Value);
        var outcomes = Merged(trx).Where(pair => pair.Key.Scenario == entry.Key)
            .ToDictionary(pair => pair.Key.Format, pair => pair.Value, StringComparer.Ordinal);

        var results = Attempts(trx).Where(File.Exists)
            .SelectMany(path => XDocument.Load(path).Descendants(Scenarios.Trx + "UnitTestResult"))
            .Where(result => ((string?)result.Attribute("testName") ?? "")
                .StartsWith(Scenarios.Namespace + entry.Key + ".", StringComparison.Ordinal));

        if (Complete(formats, outcomes) && results.All(result => (string?)result.Attribute("outcome") == "Passed"))
        {
            return 0;
        }

        Console.Error.WriteLine($"{id}: every declared format must pass without skips or retries before automatic merging");
        return 1;
    }

    internal static bool Complete(IReadOnlyList<string> formats, IReadOnlyDictionary<string, string> outcomes) =>
        formats.Count > 0 && outcomes.Count == formats.Count
        && formats.Distinct(StringComparer.Ordinal).Count() == formats.Count
        && formats.All(format => format.Length > 0 && outcomes.GetValueOrDefault(format) == "Passed");

    public static int Passed(string trx)
    {
        var outcomes = Merged(trx);

        foreach (var (scenario, entry) in Scenarios.Entries())
        {
            var formats = outcomes.Where(pair => pair.Key.Scenario == scenario).Select(pair => pair.Value).ToList();

            if (formats.Count > 0 && formats.All(outcome => outcome == "Passed"))
            {
                Console.WriteLine(Scenarios.Stem(entry));
            }
        }

        return 0;
    }

    public static int Summarise(IReadOnlyList<string> trxs)
    {
        var named = Scenarios.Entries();
        var outcomes = trxs.SelectMany(trx => Merged(trx))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
        var rows = new List<(string Entry, string Format, string Outcome)>();

        foreach (var (scenario, entry) in named)
        {
            foreach (var format in Scenarios.Formats(entry))
            {
                var outcome = outcomes.GetValueOrDefault((scenario, format), "Not run");
                rows.Add((entry, format, outcome));

                if (outcome == "Failed")
                {
                    Console.WriteLine(
                        $"::error file={Path.GetRelativePath(Scenarios.Repo, entry)},title=Plugin scenario failed::"
                        + $"{Scenarios.Field(entry, "Name")} {format} failed its end-to-end scenario");
                }
            }
        }

        var report = string.Join('\n', Report(rows, trxs, named)) + "\n";

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, report);
        }
        else
        {
            Console.Write(report);
        }

        return 0;
    }

    private static List<string> Report(
        List<(string Entry, string Format, string Outcome)> rows, IReadOnlyList<string> trxs,
        IReadOnlyDictionary<string, string> named)
    {
        var run = rows.Where(row => row.Outcome != "Not run").ToList();
        var passed = run.Count(row => row.Outcome.StartsWith("Passed", StringComparison.Ordinal));
        var counts = new[] { "Failed", "Passed on rerun", "Skipped", "Not run" }
            .Select(outcome => (outcome, count: rows.Count(row => row.Outcome == outcome)))
            .Where(pair => pair.count > 0)
            .Select(pair => $"{pair.count} {pair.outcome.ToLowerInvariant()}");

        var lines = new List<string>
        {
            "### Plugin scenarios",
            "",
            $"**{passed} of {run.Count} passed**" + string.Concat(counts.Select(count => $" · {count}")),
            "",
        };

        if (run.Count == 0)
        {
            lines.AddRange([$"No scenario results in {string.Join(", ", trxs.Select(trx => $"`{trx}`"))}.", ""]);
        }

        foreach (var outcome in new[] { "Failed", "Passed on rerun", "Skipped" })
        {
            var listed = rows.Where(row => row.Outcome == outcome).ToList();

            if (listed.Count > 0)
            {
                lines.AddRange([$"**{outcome}:** " + string.Join(", ", listed.Select(row => $"`{Scenarios.Stem(row.Entry)}` {row.Format}")), ""]);
            }
        }

        lines.AddRange([$"<details><summary>All {rows.Count} results</summary>", "", "| Entry | Version | Format | Result |", "| --- | --- | --- | --- |"]);
        lines.AddRange(rows.Select(row =>
        {
            var version = Scenarios.Field(row.Entry, "Version") is { Length: > 0 } found ? found : "rolling";
            return $"| `{Scenarios.Stem(row.Entry)}` | {version} | {row.Format} | {row.Outcome} |";
        }));
        lines.AddRange(["", "</details>", ""]);
        lines.AddRange(Timings(trxs, named));
        return lines;
    }

    internal static IReadOnlyList<string> Attempts(string trx)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(trx))!;
        var retries = Path.Combine(directory, "Retries");
        var retried = Directory.Exists(retries)
            ? Directory.GetDirectories(retries)
                .SelectMany(Directory.GetDirectories)
                .Select(attempt => Path.Combine(attempt, Path.GetFileName(trx)))
                .Where(File.Exists)
                .OrderBy(path => int.Parse(Path.GetFileName(Path.GetDirectoryName(path)!), CultureInfo.InvariantCulture))
                .ToList()
            : [];

        return retried.Count > 0 ? retried : [trx];
    }

    private static Dictionary<(string Scenario, string Format), string> Merged(string trx)
    {
        var outcomes = new Dictionary<(string, string), string>();

        foreach (var attempt in Attempts(trx))
        {
            foreach (var (key, outcome) in Outcomes(attempt))
            {
                var rerun = outcomes.GetValueOrDefault(key) == "Failed" && outcome == "Passed";
                outcomes[key] = rerun ? "Passed on rerun" : outcome;
            }
        }

        return outcomes;
    }

    private static Dictionary<(string, string), string> Outcomes(string trx)
    {
        var outcomes = new Dictionary<(string, string), string>();

        foreach (var (scenario, format, result) in Scenarios.Results(trx))
        {
            if (outcomes.GetValueOrDefault((scenario, format)) != "Failed")
            {
                outcomes[(scenario, format)] = (string?)result.Attribute("outcome") ?? "Unknown";
            }
        }

        return outcomes;
    }

    private static Dictionary<string, double> Durations(string trx)
    {
        var spent = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (scenario, _, result) in Scenarios.Results(trx).DistinctBy(found => found.Result))
        {
            if ((string?)result.Attribute("duration") is { Length: > 0 } duration)
            {
                spent[scenario] = spent.GetValueOrDefault(scenario) + Seconds(duration);
            }
        }

        return spent;
    }

    private static double Seconds(string duration)
    {
        var parts = duration.Split(':');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
               + int.Parse(parts[1], CultureInfo.InvariantCulture) * 60
               + double.Parse(parts[2], CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, double> Taken(string timing)
    {
        var taken = new Dictionary<string, double>(StringComparer.Ordinal);

        if (!File.Exists(timing))
        {
            return taken;
        }

        foreach (var line in Scenarios.Lines(File.ReadAllText(timing)))
        {
            var space = line.IndexOf(' ');
            var step = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..];
            taken[step] = taken.GetValueOrDefault(step) + double.Parse(value, CultureInfo.InvariantCulture);
        }

        return taken;
    }

    private static string Clock(double? value)
    {
        if (value is not { } seconds)
        {
            return "–";
        }

        var rounded = (long)Math.Round(seconds, MidpointRounding.ToEven);
        var (minutes, rest) = (rounded / 60, rounded % 60);
        return minutes > 0 ? $"{minutes}m {rest:00}s" : $"{rest}s";
    }

    public static IReadOnlyDictionary<string, double> Spent(string trx) =>
        Rows(trx, Scenarios.Entries()).ToDictionary(row => row.Scenario, row => row.Total, StringComparer.Ordinal);

    private static List<(string Scenario, double Total, string Entry, Dictionary<string, double> Taken, double? Tests)> Rows(
        string trx, IReadOnlyDictionary<string, string> named)
    {
        var scenarios = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(trx))!)!, "scenarios");
        var tested = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var attempt in Attempts(trx))
        {
            foreach (var (scenario, spent) in Durations(attempt))
            {
                tested[scenario] = tested.GetValueOrDefault(scenario) + spent;
            }
        }

        var rows = new List<(string, double, string, Dictionary<string, double>, double?)>();

        foreach (var (scenario, entry) in named)
        {
            var taken = Taken(Path.Combine(scenarios, Scenarios.Stem(entry), "artefacts", "timing.txt"));

            if (taken.Count == 0 && !tested.ContainsKey(scenario))
            {
                continue;
            }

            double? tests = tested.TryGetValue(scenario, out var value) ? value : null;
            var total = Steps.Sum(step => taken.GetValueOrDefault(step)) + (tests ?? 0);
            rows.Add((scenario, total, entry, taken, tests));
        }

        return rows;
    }

    private static IReadOnlyList<string> Timings(IReadOnlyList<string> trxs, IReadOnlyDictionary<string, string> named)
    {
        var rows = trxs.SelectMany(trx => Rows(trx, named)).ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var columns = Steps.Concat(Phases).ToList();
        var lines = new List<string>
        {
            "<details><summary>Where the time went</summary>",
            "",
            "| Entry | " + string.Join(" | ", columns.Select(Capitalised)) + " | Tests | Total |",
            "| --- |" + string.Concat(Enumerable.Repeat(" ---: |", columns.Count + 2)),
        };
        lines.AddRange(rows.OrderByDescending(row => row.Total).Select(row =>
            $"| `{Scenarios.Stem(row.Entry)}` | "
            + string.Join(" | ", columns.Select(step => Clock(row.Taken.TryGetValue(step, out var spent) ? spent : null)))
            + $" | {Clock(row.Tests)} | {Clock(row.Total)} |"));
        lines.AddRange(["", $"All scenarios together: {Clock(rows.Sum(row => row.Total))}.", "", "</details>", ""]);
        return lines;
    }

    private static string Capitalised(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}
