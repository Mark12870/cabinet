using System.Globalization;

namespace Cabinet.Ci;

internal static class ScenarioReport
{
    private static readonly string[] Steps = ["prepare", "display", "install", "settle", "dispose"];
    private static readonly string[] Phases = ["runner", "prefix", "download", "installer", "dxvk", "bridge"];

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

    public static int Summarise(string trx)
    {
        var named = Scenarios.Entries();
        var outcomes = Merged(trx);
        var rows = new List<(string Entry, string Format, string Outcome)>();

        foreach (var (scenario, entry) in named)
        {
            var formats = Scenarios.Field(entry, "Formats").Split(',')
                .Select(format => format.Trim())
                .Where(format => format.Length > 0);

            foreach (var format in formats)
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

        var lines = new List<string> { "### Plugin scenario results", "" };

        if (outcomes.Count == 0)
        {
            lines.AddRange([$"No scenario results in `{trx}`.", ""]);
        }

        lines.AddRange(["| Entry | Version | Format | Result |", "| --- | --- | --- | --- |"]);
        lines.AddRange(rows.Select(row =>
        {
            var version = Scenarios.Field(row.Entry, "Version") is { Length: > 0 } found ? found : "rolling";
            return $"| `{Scenarios.Stem(row.Entry)}` | {version} | {row.Format} | {row.Outcome} |";
        }));
        lines.Add("");
        lines.AddRange(Timings(trx, named));
        var report = string.Join('\n', lines) + "\n";

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

    private static IReadOnlyList<string> Attempts(string trx)
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

        foreach (var (scenario, _, result) in Scenarios.Results(trx))
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

    private static IReadOnlyList<string> Timings(string trx, IReadOnlyDictionary<string, string> named)
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

        var rows = new List<(double Total, string Entry, Dictionary<string, double> Taken, double? Tests)>();

        foreach (var (scenario, entry) in named)
        {
            var taken = Taken(Path.Combine(scenarios, Scenarios.Stem(entry), "artefacts", "timing.txt"));

            if (taken.Count == 0 && !tested.ContainsKey(scenario))
            {
                continue;
            }

            double? tests = tested.TryGetValue(scenario, out var value) ? value : null;
            var total = Steps.Sum(step => taken.GetValueOrDefault(step)) + (tests ?? 0);
            rows.Add((total, entry, taken, tests));
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var columns = Steps.Concat(Phases).ToList();
        var lines = new List<string>
        {
            "### Where the scenarios' time went",
            "",
            "| Entry | " + string.Join(" | ", columns.Select(Capitalised)) + " | Tests | Total |",
            "| --- |" + string.Concat(Enumerable.Repeat(" ---: |", columns.Count + 2)),
        };
        lines.AddRange(rows.OrderByDescending(row => row.Total).Select(row =>
            $"| `{Scenarios.Stem(row.Entry)}` | "
            + string.Join(" | ", columns.Select(step => Clock(row.Taken.TryGetValue(step, out var spent) ? spent : null)))
            + $" | {Clock(row.Tests)} | {Clock(row.Total)} |"));
        lines.AddRange(["", $"All scenarios together: {Clock(rows.Sum(row => row.Total))}.", ""]);
        return lines;
    }

    private static string Capitalised(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}
