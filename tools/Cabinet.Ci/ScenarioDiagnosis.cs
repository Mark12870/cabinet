using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cabinet.Ci;

internal static partial class ScenarioDiagnosis
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "openai/gpt-oss-120b";
    private const int EvidenceLength = 12000;
    private const int Part = 3000;
    private const int Answer = 3000;
    private const int Requests = 6;
    private const int Tries = 3;
    private const double LongestWait = 90;
    private const int Tail = 40;
    private const int Excerpt = 12;

    private static readonly string[] Secrets =
        ["EMAIL", "PASSWORD", "USERNAME", "DROPBOX", "NATIVE_ACCESS", "WAVES", "GROQ_API_KEY"];

    private static readonly string[] Fields =
        ["Name", "Kind", "Version", "Formats", "Source", "Url", "DemoUrl", "Script", "Runner", "Prefix"];

    private const string Prompt =
        """
        You diagnose a failed plugin scenario of Cabinet, a Flatpak that installs Windows audio
        plugins into one Wine prefix per vendor and bridges them to Linux DAWs with yabridge; native
        Linux entries skip Wine. A scenario installs one catalogue entry from its download into a fresh
        home, bridges it, renders audio through Carla, and runs probes: the editor probe opens the
        editor, requires more than one colour and clicks and drags in it to see it respond.
        Causes, in the order to suspect them: Cabinet or its probes (an exception from a probe, a
        failed C# assertion), a moved or changed vendor download or installer, the Wine runner or the
        prefix, a flaky network or display. Wine's err:ole, RpcSs, wbemprox and win32k.sys lines appear in
        passing runs too and prove nothing on their own.
        Say only what went wrong and exactly where: the step that failed, the file and line or the
        component, and the lines that show it, quoted. Do not suggest a fix or a next step. Say so when
        the evidence is not enough. Answer in at most fifteen lines of Markdown with no heading.
        """;

    public static async Task<int> Diagnose(string artifact)
    {
        var failed = Failures(Path.Combine(artifact, "TestResults", "runtime.trx"));

        if (failed.Count == 0)
        {
            Console.WriteLine(
                "No scenario failed on its last attempt, so the run failed outside the scenarios. Its log says where.");
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("cabinet-scenario-diagnosis");

        foreach (var (cause, index) in Causes(artifact, failed).Select((cause, index) => (cause, index)))
        {
            Console.WriteLine($"### {cause.Title}\n");
            Console.WriteLine(index < Requests
                ? await Ask(http, cause.Question)
                : $"_Not diagnosed: more than {Requests} separate causes, which points at Cabinet rather than a vendor._");
            Console.WriteLine($"\n{cause.Recorded}\n");
        }

        return 0;
    }

    private static Dictionary<string, Dictionary<string, XElement>> Failures(string trx)
    {
        var failed = new Dictionary<string, Dictionary<string, XElement>>(StringComparer.Ordinal);

        foreach (var (scenario, format, result) in Scenarios.Results(trx))
        {
            if ((string?)result.Attribute("outcome") == "Failed")
            {
                if (!failed.TryGetValue(scenario, out var formats))
                {
                    failed[scenario] = formats = new Dictionary<string, XElement>(StringComparer.Ordinal);
                }

                formats[format] = result;
            }
        }

        return failed;
    }

    private static List<Cause> Causes(string artifact, Dictionary<string, Dictionary<string, XElement>> failed)
    {
        var named = Scenarios.Entries();
        var grouped = new Dictionary<string, Cause>(StringComparer.Ordinal);
        var order = new List<Cause>();

        foreach (var (scenario, formats) in failed)
        {
            var entry = named[scenario];
            var found = Evidence(entry, formats, Path.Combine(artifact, "scenarios", Scenarios.Stem(entry), "artefacts"));
            var label = $"`{Scenarios.Stem(entry)}` ({string.Join(", ", formats.Keys)})";
            var exception = Signature(found);
            var key = exception.Length > 0 ? exception : label;

            if (!grouped.TryGetValue(key, out var cause))
            {
                grouped[key] = cause = new Cause(exception, found);
                order.Add(cause);
            }

            cause.Labels.Add(label);
            cause.Tests.AddRange(formats.Values.Select(result =>
            {
                var name = (string?)result.Attribute("testName") ?? "";
                return name.StartsWith(Scenarios.Namespace, StringComparison.Ordinal) ? name[Scenarios.Namespace.Length..] : name;
            }));
        }

        return [.. order.OrderByDescending(cause => cause.Labels.Count)];
    }

    private static string Evidence(string entry, Dictionary<string, XElement> formats, string artefacts)
    {
        var parts = new List<string>();

        foreach (var (format, result) in formats)
        {
            parts.Add($"{format} error:\n{Quiet(Text(result, "Output", "ErrorInfo", "Message"))}");
            parts.Add($"{format} stack:\n{Quiet(Text(result, "Output", "ErrorInfo", "StackTrace"), 10)}");
            parts.AddRange(Probes(artefacts, format).Take(2).Select(log =>
                $"{Path.GetRelativePath(artefacts, log)}:\n{Quiet(File.ReadAllText(log).Split("yabridge:")[0])}"));
        }

        var install = Path.Combine(artefacts, "install.log");

        if (File.Exists(install))
        {
            parts.Add($"install.log:\n{Quiet(File.ReadAllText(install), 25)}");
        }

        var fields = Scenarios.Lines(File.ReadAllText(entry)).Where(line => Fields.Contains(line.Split(':')[0]));
        parts.Add($"Entry {Path.GetFileName(entry)}:\n" + string.Join('\n', fields));

        var evidence = Redact(string.Join("\n\n", parts));
        return evidence.Length > EvidenceLength ? evidence[..EvidenceLength] : evidence;
    }

    private static string Text(XElement result, params string[] path)
    {
        XElement? node = result;

        foreach (var tag in path)
        {
            node = node.Element(Scenarios.Trx + tag);

            if (node is null)
            {
                return "";
            }
        }

        return node.Value;
    }

    private static IEnumerable<string> Probes(string artefacts, string format) =>
        Directory.Exists(artefacts)
            ? Directory.GetDirectories(artefacts)
                .SelectMany(Directory.GetDirectories)
                .Where(directory => Path.GetFileName(directory).StartsWith(format, StringComparison.OrdinalIgnoreCase))
                .Select(directory => Path.Combine(directory, "probe.log"))
                .Where(File.Exists)
                .OrderBy(log => Path.GetRelativePath(artefacts, log).Split('/'), PathParts.Instance)
            : [];

    private static string Quiet(string text, int lines = Tail)
    {
        var kept = Scenarios.Lines(text).Where(line => line.Trim().Length > 0 && !Noise().IsMatch(line)).ToList();
        var quiet = string.Join('\n', kept.Skip(Math.Max(0, kept.Count - lines)));
        return quiet.Length > Part ? quiet[^Part..] : quiet;
    }

    private static string Redact(string text)
    {
        foreach (var name in Secrets)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: >= 4 } value)
            {
                text = text.Replace(value, $"<{name}>", StringComparison.Ordinal);
            }
        }

        text = Query().Replace(text, "$1?<query>");
        return Token().Replace(text, "<token>");
    }

    private static string Signature(string evidence) =>
        ExceptionLine().Matches(evidence) is { Count: > 0 } found ? found[^1].Groups[1].Value.Trim() : "";

    private static async Task<string> Ask(HttpClient http, string evidence)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["reasoning_effort"] = "medium",
            ["max_completion_tokens"] = Answer,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Prompt },
                new JsonObject { ["role"] = "user", ["content"] = evidence }),
        }.ToJsonString();

        for (var attempt = 1; attempt <= Tries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
                request.Headers.Authorization = new("Bearer", Environment.GetEnvironmentVariable("GROQ_API_KEY"));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request);
                var said = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonNode.Parse(said)!["choices"]![0]!["message"]!["content"]!.GetValue<string>().Trim();
                }

                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == Tries)
                {
                    return $"_The diagnosis could not be fetched: Groq answered {(int)response.StatusCode} {Redact(said)}_";
                }

                var wait = response.Headers.TryGetValues("retry-after", out var values)
                    ? double.Parse(values.First(), CultureInfo.InvariantCulture)
                    : 60;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, LongestWait)));
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                return $"_The diagnosis could not be fetched: {error.Message}_";
            }
        }

        return "";
    }

    private sealed class Cause(string exception, string evidence)
    {
        public List<string> Labels { get; } = [];

        public List<string> Tests { get; } = [];

        public string Title => Labels.Count == 1
            ? Labels[0]
            : $"One cause in {Labels.Count} entries: {string.Join(", ", Labels)}";

        public string Question => Labels.Count == 1
            ? evidence
            : $"The same exception ended {Labels.Count} entries: {string.Join(", ", Labels)}.\n"
              + $"Exception: {exception}\nEvidence from the first of them:\n\n{evidence}";

        public string Recorded
        {
            get
            {
                var lines = Scenarios.Lines(evidence).Select(line => line.TrimEnd()).ToList();
                var last = lines.FindLastIndex(line => line.Trim() == exception);
                var end = last >= 0 ? last + 1 : Excerpt;
                var start = Math.Max(0, end - Excerpt);
                var quoted = string.Join('\n', lines.Skip(start).Take(Math.Min(end, lines.Count) - start))
                    .Replace("```", "'''", StringComparison.Ordinal);
                var tests = string.Join('\n', Tests.Select(test => $"- `{test}`"));
                return $"<details><summary>As recorded</summary>\n\n{tests}\n\n```text\n{quoted}\n```\n</details>";
            }
        }
    }

    private sealed class PathParts : IComparer<string[]>
    {
        public static readonly PathParts Instance = new();

        public int Compare(string[]? x, string[]? y)
        {
            for (var i = 0; i < Math.Min(x!.Length, y!.Length); i++)
            {
                if (string.CompareOrdinal(x[i], y[i]) is var order and not 0)
                {
                    return order;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }

    [GeneratedRegex("""(https?://[^\s?"']+)\?[^\s"']*""")]
    private static partial Regex Query();

    [GeneratedRegex("[A-Za-z0-9+_=-]{40,}")]
    private static partial Regex Token();

    [GeneratedRegex(@"fixme:|DEBUG:|\[host (->|<-) plugin\]|\[carla\] TODO|Unhandled X11 event")]
    private static partial Regex Noise();

    [GeneratedRegex(@"^\s*([\w.]*(?:Error|Exception)\b.*)$", RegexOptions.Multiline)]
    private static partial Regex ExceptionLine();
}
