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
        var ours = layout.PrefixesDir + Path.DirectorySeparatorChar;

        return registered.Where(directory =>
            !wanted.Contains(directory) && directory.StartsWith(ours, StringComparison.Ordinal));
    }

    public ProcessResult SyncPrefixes(IReadOnlyList<Prefix> prefixes)
    {
        var wanted = prefixes
            .Where(prefix => prefix.Initialised)
            .SelectMany(prefix => layout.PrefixPluginDirs(prefix.Name))
            .Where(Directory.Exists)
            .ToHashSet(StringComparer.Ordinal);
        ProcessResult? failure = null;

        foreach (var directory in wanted)
        {
            var result = Add(directory);
            if (!result.Ok)
            {
                failure ??= result;
            }
        }

        var registered = Run(["list"]);
        if (!registered.Ok)
        {
            failure ??= registered;
        }
        else foreach (var directory in StaleRegistrations(ParseRegistered(registered.Stdout), wanted))
        {
            var result = Remove(directory);
            if (!result.Ok)
            {
                failure ??= result;
            }
        }

        return failure ?? Sync();
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
            onOutput?.Invoke(line);
        }

        if (!result.Ok)
        {
            throw new InvalidOperationException($"yabridgectl exited with {result.ExitCode}");
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
}
