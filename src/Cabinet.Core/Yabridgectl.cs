namespace Cabinet.Core;

/// <summary>
/// Drives upstream's <c>yabridgectl</c> through its own subcommands.
/// </summary>
/// <remarks>
/// Deliberately not by editing <c>config.toml</c>: going through the CLI keeps Cabinet
/// out of a file format it does not own, and removes the only reason to take a TOML
/// dependency into a NativeAOT build.
/// </remarks>
public sealed class Yabridgectl(Layout layout, IProcessRunner runner)
{
    private string Binary => Path.Combine(layout.YabridgeDir, "yabridgectl");

    public ProcessResult Add(string pluginDirectory) => Run(["add", pluginDirectory]);

    public ProcessResult Sync(bool prune = true) =>
        Run(prune ? ["sync", "--prune"] : ["sync"]);

    public ProcessResult Status() => Run(["status"]);

    /// <summary>Registers every plugin directory of every initialised prefix, then syncs.</summary>
    public ProcessResult SyncPrefixes(IReadOnlyList<Prefix> prefixes)
    {
        var directories = prefixes
            .Where(prefix => prefix.Initialised)
            .SelectMany(prefix => layout.PrefixPluginDirs(prefix.Name))
            .Where(Directory.Exists);

        foreach (var directory in directories)
        {
            Add(directory);
        }

        return Sync();
    }

    private ProcessResult Run(IReadOnlyList<string> arguments)
    {
        if (!File.Exists(Binary))
        {
            throw new InvalidOperationException($"{Binary} is missing — run `cabinet setup`");
        }

        // Pinned to Wine, not the shim. yabridgectl probes `$WINELOADER --version`, and
        // the shim inherited from the login session would send that back out of the
        // sandbox that is already the one meant to run it.
        return runner.Run(Binary, arguments, new Dictionary<string, string>
        {
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINELOADER"] = Layout.Wine,
        });
    }
}
