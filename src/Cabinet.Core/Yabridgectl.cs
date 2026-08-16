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

    /// <summary>
    /// Pins the yabridge location explicitly.
    /// </summary>
    /// <remarks>
    /// Required, not tidiness: yabridgectl looks in <c>$XDG_DATA_HOME/yabridge</c>, and
    /// inside Cabinet's own Flatpak that resolves to
    /// <c>~/.var/app/io.github.mark12870.cabinet/data</c> rather than the host location
    /// the files were exported to.
    /// </remarks>
    public ProcessResult SetPath() => Run(["set", $"--path={layout.YabridgeDir}"]);

    public ProcessResult Add(string pluginDirectory) => Run(["add", pluginDirectory]);

    public ProcessResult Sync(bool prune = true) =>
        Run(prune ? ["sync", "--prune"] : ["sync"]);

    public ProcessResult Status() => Run(["status"]);

    /// <summary>Registers every initialised prefix's VST3 directory, then syncs.</summary>
    public ProcessResult SyncPrefixes(IReadOnlyList<Prefix> prefixes)
    {
        foreach (var prefix in prefixes.Where(p => p.Initialised))
        {
            var directory = layout.PrefixVst3Dir(prefix.Name);
            if (Directory.Exists(directory))
            {
                Add(directory);
            }
        }

        return Sync();
    }

    private ProcessResult Run(IReadOnlyList<string> arguments)
    {
        if (!File.Exists(Binary))
        {
            throw new InvalidOperationException($"{Binary} is missing — run `cabinet setup`");
        }

        // No WINELOADER here. Cabinet *is* the Wine sandbox, so yabridgectl finds wine
        // on PATH; the shim exists for callers on the other side of the boundary.
        return runner.Run(Binary, arguments, new Dictionary<string, string>
        {
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
        });
    }
}
