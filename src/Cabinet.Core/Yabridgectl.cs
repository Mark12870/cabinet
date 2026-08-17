namespace Cabinet.Core;

/// <summary>
/// Drives upstream's <c>yabridgectl</c> through its own subcommands.
/// </summary>
/// <remarks>
/// Through the CLI rather than by editing <c>config.toml</c>, which keeps a TOML
/// dependency out of a NativeAOT build.
/// </remarks>
public sealed class Yabridgectl(Layout layout, IProcessRunner runner)
{
    // The in-sandbox copy: yabridgectl runs here, not on the host.
    private static string Binary => Path.Combine(Layout.BundledYabridgeDir, "yabridgectl");

    public ProcessResult Add(string pluginDirectory) => Run(["add", pluginDirectory]);

    public ProcessResult Remove(string pluginDirectory) => Run(["rm", pluginDirectory]);

    public ProcessResult Sync(bool prune = true) =>
        Run(prune ? ["sync", "--prune"] : ["sync"]);

    public ProcessResult Status() => Run(["status"]);

    /// <summary>Every plugin directory yabridgectl currently scans.</summary>
    public IReadOnlyList<string> Registered() =>
        Run(["list"]).Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            // TrimEnd rather than TrimEntries: a trailing space is legal in a path.
            .Select(line => line.TrimEnd('\r'))
            .ToList();

    /// <summary>
    /// Registered directories that Cabinet owns and no longer wants, because the prefix or
    /// the directory under it has been deleted.
    /// </summary>
    /// <remarks>
    /// Scoped to <see cref="Layout.PrefixesDir"/>: a location the user added to yabridgectl
    /// by hand is theirs, and unregistering it would be Cabinet reaching outside itself.
    /// </remarks>
    public IEnumerable<string> StaleRegistrations(
        IEnumerable<string> registered, IReadOnlySet<string> wanted)
    {
        // Trailing separator, or a sibling like `prefixes-backup` would count as ours.
        var ours = layout.PrefixesDir + Path.DirectorySeparatorChar;

        return registered.Where(directory =>
            !wanted.Contains(directory) && directory.StartsWith(ours, StringComparison.Ordinal));
    }

    /// <summary>Registers every plugin directory of every initialised prefix, then syncs.</summary>
    /// <remarks>
    /// Reconciles rather than appends. yabridgectl skips a registered directory that has
    /// gone missing without complaining, so without the removal pass a deleted prefix stays
    /// on its list forever — and reusing that prefix's name resurrects it as a duplicate.
    /// </remarks>
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

        // Pinned to Wine, not the shim: yabridgectl probes `$WINELOADER --version`, and
        // an inherited shim would bounce that back out of the sandbox meant to run it.
        return runner.Run(Binary, arguments, new Dictionary<string, string>
        {
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINELOADER"] = Layout.Wine,
        });
    }
}
