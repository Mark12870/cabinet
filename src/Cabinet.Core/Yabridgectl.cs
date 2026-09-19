using System.Text;

namespace Cabinet.Core;

public sealed class Yabridgectl(Layout layout, IProcessRunner runner)
{
    private static readonly Lock Bridging = new();

    private string Binary => Path.Combine(layout.BundledYabridgeDir, "yabridgectl");

    public ProcessResult Add(string pluginDirectory) => Run(["add", pluginDirectory]);

    public ProcessResult Remove(string pluginDirectory) => Run(["rm", pluginDirectory]);

    public ProcessResult Sync(bool prune = true) =>
        Run(prune ? ["sync", "--prune"] : ["sync"]);

    public ProcessResult Status() => Run(["status"]);

    public string Version()
    {
        if (!File.Exists(Binary))
        {
            return "unknown";
        }

        var result = Run(["--version"]);
        var line = result.Ok ? result.Stdout.Split('\n').FirstOrDefault()?.Trim() : null;

        return line?.Split(' ').LastOrDefault() is { Length: > 0 } version ? version : "unknown";
    }

    public IReadOnlyList<string> Registered() => ParseRegistered(Run(["list"]).Stdout);

    private static IReadOnlyList<string> ParseRegistered(string stdout) =>
        stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToList();

    public IEnumerable<string> StaleRegistrations(
        IEnumerable<string> registered, IReadOnlySet<string> wanted)
    {
        var ours = Canonical(layout.PrefixesDir);
        var wantedCanonical = wanted.Select(Canonical).ToHashSet(StringComparer.Ordinal);

        return registered.Where(directory =>
        {
            var canonical = Canonical(directory);
            var relative = Path.GetRelativePath(ours, canonical);
            return !wantedCanonical.Contains(canonical)
                   && relative != "."
                   && relative != ".."
                   && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                       StringComparison.Ordinal);
        });
    }

    public ProcessResult SyncPrefixes(IReadOnlyList<Prefix> prefixes)
    {
        var wanted = prefixes
            .Where(prefix => prefix.Initialised)
            .SelectMany(prefix => layout.PrefixPluginDirs(prefix.Name))
            .Where(Directory.Exists)
            .Select(Canonical)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .ToList();
        var failures = new List<FailedOperation>();

        var registered = Run(["list"]);
        if (!registered.Ok)
        {
            failures.Add(new("list registrations", null, registered));
        }
        else
        {
            var registrations = ParseRegistered(registered.Stdout);
            var registeredCanonical = registrations
                .Select(Canonical)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var directory in wanted.Where(directory => !registeredCanonical.Contains(directory)))
            {
                var result = Add(directory);
                if (!result.Ok)
                {
                    failures.Add(new("add", directory, result));
                }
            }

            var wantedCanonical = wanted.ToHashSet(StringComparer.Ordinal);
            foreach (var directory in StaleRegistrations(registrations, wantedCanonical))
            {
                var result = Remove(directory);
                if (!result.Ok)
                {
                    failures.Add(new("remove", directory, result));
                }
            }
        }

        return failures.Count > 0 ? Aggregate(failures) : Sync();
    }

    public ProcessResult SyncAndPublish(IReadOnlyList<Prefix> prefixes)
    {
        var result = SyncPrefixes(prefixes);
        if (!result.Ok)
        {
            return result;
        }

        var conflicts = Enrolment.PublishNative(layout);
        return conflicts.Count == 0
            ? result
            : result with
            {
                Stderr = result.Stderr
                         + "Native DAWs cannot see Cabinet: its scan path is already owned by "
                         + $"something else: {string.Join(", ", conflicts)}\n",
            };
    }

    public void Bridge(IReadOnlyList<Prefix> prefixes, Action<string>? onOutput)
    {
        onOutput?.Invoke("Bridging what is installed…");
        ProcessResult result;
        lock (Bridging)
        {
            result = SyncAndPublish(prefixes);
        }

        foreach (var line in (result.Stdout + result.Stderr)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            onOutput?.Invoke(line.TrimEnd('\r'));
        }

        if (!result.Ok)
        {
            throw new InvalidOperationException(onOutput is null
                ? $"yabridgectl exited with {result.ExitCode}:\n"
                  + (result.Stdout + result.Stderr).TrimEnd()
                : $"yabridgectl exited with {result.ExitCode}; what it said is above");
        }
    }

    private ProcessResult Run(IReadOnlyList<string> arguments)
    {
        if (!File.Exists(Binary))
        {
            throw new InvalidOperationException(
                $"{Binary} is missing — run this from inside the Cabinet Flatpak");
        }

        return runner.Run(Binary, arguments, new Dictionary<string, string>
        {
            ["HOME"] = layout.BridgeHome,
            ["XDG_DATA_HOME"] = layout.BridgeDataHome,
            ["XDG_CONFIG_HOME"] = layout.BridgeConfigHome,
            ["CLAP_PATH"] = layout.BridgeClapHome,
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINELOADER"] = Layout.Wine,
        });
    }

    private sealed record FailedOperation(string Name, string? Directory, ProcessResult Result);

    private static ProcessResult Aggregate(IReadOnlyList<FailedOperation> failures)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        foreach (var failure in failures)
        {
            var subject = failure.Directory is null
                ? failure.Name
                : $"{failure.Name} {failure.Directory}";
            Append(stdout, $"{subject} failed with exit code {failure.Result.ExitCode}",
                failure.Result.Stdout);
            Append(stderr, $"{subject} failed with exit code {failure.Result.ExitCode}",
                failure.Result.Stderr);
        }

        return new(failures[0].Result.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void Append(StringBuilder text, string header, string output)
    {
        text.AppendLine(header);
        text.Append(output);
        if (!output.EndsWith('\n'))
        {
            text.AppendLine();
        }
    }

    private static string Canonical(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
