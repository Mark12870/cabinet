namespace Cabinet.Core;

public sealed class Yabridgectl(Layout layout, IProcessRunner runner)
{
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

    public void Bridge(IReadOnlyList<Prefix> prefixes, Action<string>? onOutput)
    {
        onOutput?.Invoke("Bridging what is installed…");
        var result = SyncPrefixes(prefixes);

        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINELOADER"] = Layout.Wine,
        });
    }
}
