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
