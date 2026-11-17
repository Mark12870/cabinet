using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cabinet.Ci;

internal static class CiTimings
{
    private static readonly TimeSpan SlowStep = TimeSpan.FromSeconds(30);

    public static async Task<int> Report(string repository, int count)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"https://api.github.com/repos/{repository}/") };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("cabinet-ci");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var runs = await Get(client, $"actions/workflows/ci.yml/runs?branch=main&per_page={count}");
            foreach (var run in runs.RootElement.GetProperty("workflow_runs").EnumerateArray())
            {
                using var jobs = await Get(client, $"actions/runs/{run.GetProperty("id").GetInt64()}/jobs?per_page=100");
                Write(run, jobs.RootElement.GetProperty("jobs"));
            }

            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"ci timings failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<JsonDocument> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }

    private static void Write(JsonElement run, JsonElement jobs)
    {
        var title = run.GetProperty("display_title").GetString() ?? "";
        var marker = title.StartsWith("Release ", StringComparison.Ordinal) ? "release " : "";
        Console.WriteLine($"{run.GetProperty("created_at").GetString()} {marker}{Text(run, "conclusion")} "
                          + $"{Minutes(Span(run, "run_started_at", "updated_at"))} {title}");

        foreach (var job in jobs.EnumerateArray().Where(job => Text(job, "conclusion") != "skipped"))
        {
            Console.WriteLine($"  {Minutes(Span(job, "started_at", "completed_at")),6} {job.GetProperty("name").GetString()}");

            var slow = job.GetProperty("steps").EnumerateArray()
                .Select(step => (Name: step.GetProperty("name").GetString(), Took: Span(step, "started_at", "completed_at")))
                .Where(step => step.Took >= SlowStep)
                .OrderByDescending(step => step.Took);
            foreach (var (name, took) in slow)
            {
                Console.WriteLine($"         {Minutes(took),6} {name}");
            }
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.String
            ? element.GetProperty(property).GetString()!
            : "running";

    private static TimeSpan Span(JsonElement element, string start, string end) =>
        element.GetProperty(start).ValueKind == JsonValueKind.String
        && element.GetProperty(end).ValueKind == JsonValueKind.String
            ? element.GetProperty(end).GetDateTimeOffset() - element.GetProperty(start).GetDateTimeOffset()
            : TimeSpan.Zero;

    private static string Minutes(TimeSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m{span.Seconds:00}s");
}
