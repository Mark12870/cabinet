namespace Cabinet.Core;

public sealed record Prefix(string Name, string Path, bool Initialised);

/// <summary>
/// One Wine prefix per plugin — the "bottle per VST" the project exists for.
/// </summary>
/// <remarks>
/// Nothing here teaches yabridge about prefixes: it finds them itself by walking up
/// from the plugin's <c>.dll</c> for a <c>dosdevices</c> directory. All Cabinet has to
/// do is put each plugin in its own prefix and keep <c>WINEPREFIX</c> pointed at it.
/// </remarks>
public sealed class Prefixes(Layout layout, IProcessRunner runner)
{
    public IReadOnlyList<Prefix> List()
    {
        if (!Directory.Exists(layout.PrefixesDir))
        {
            return [];
        }

        return Directory.EnumerateDirectories(layout.PrefixesDir)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new Prefix(
                Path.GetFileName(path),
                path,
                // What yabridge keys on, so it is also what "initialised" means here.
                Directory.Exists(Path.Combine(path, "dosdevices"))))
            .ToList();
    }

    public Prefix Create(string name)
    {
        var path = layout.PrefixPath(name);
        if (Directory.Exists(Path.Combine(path, "dosdevices")))
        {
            return new Prefix(name, path, true);
        }

        Directory.CreateDirectory(path);
        var result = Wine(name, "wineboot", ["--init"]);
        if (!result.Ok)
        {
            throw new InvalidOperationException($"wineboot failed for '{name}':\n{result.Stderr}");
        }

        Directory.CreateDirectory(layout.PrefixVst3Dir(name));
        return new Prefix(name, path, true);
    }

    /// <summary>Runs a Windows installer inside one prefix.</summary>
    public ProcessResult Install(string name, string installer)
    {
        var full = Path.GetFullPath(installer);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"no such installer: {full}", full);
        }

        return Wine(name, "wine", [full]);
    }

    public ProcessResult Run(string name, string command, IReadOnlyList<string> arguments) =>
        Wine(name, command, arguments);

    /// <summary>
    /// Wine runs in this process's own sandbox — Cabinet <em>is</em> the Wine Flatpak.
    /// The shim exists for the other direction, where the caller is the DAW.
    /// </summary>
    private ProcessResult Wine(string prefix, string command, IReadOnlyList<string> arguments)
    {
        var environment = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = layout.PrefixPath(prefix),
            // org.winehq.Wine bakes WINEPREFIX=/var/data/wine into its metadata, so
            // this is set explicitly every time rather than relied upon to be unset.
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
        };

        return runner.Run(command, arguments, environment);
    }
}
