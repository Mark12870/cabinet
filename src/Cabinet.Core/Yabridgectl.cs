namespace Cabinet.Core;

public sealed class Yabridgectl(Layout layout, IProcessRunner runner)
{
    private static string Binary => Path.Combine(Layout.BundledYabridgeDir, "yabridgectl");

    public ProcessResult Add(string pluginDirectory) => Run(["add", pluginDirectory]);

    public ProcessResult Remove(string pluginDirectory) => Run(["rm", pluginDirectory]);

    public ProcessResult Sync(bool prune = true) =>
        Run(prune ? ["sync", "--prune"] : ["sync"]);

    public ProcessResult Status() => Run(["status"]);

    public IReadOnlyList<string> Registered() =>
        Run(["list"]).Stdout
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

        foreach (var directory in wanted)
        {
            Add(directory);
        }

        foreach (var directory in StaleRegistrations(Registered(), wanted))
        {
            Remove(directory);
        }

        return Sync();
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
