namespace Cabinet.Core;

public sealed record Prefix(string Name, string Path, bool Initialised);

/// <summary>
/// One Wine prefix per plugin — the "bottle per VST" the project exists for.
/// </summary>
/// <remarks>
/// Nothing registers prefixes with yabridge: it walks up from the plugin's <c>.dll</c>
/// for a <c>dosdevices</c> directory and finds them itself.
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

        if (!Directory.Exists(Path.Combine(path, "dosdevices")))
        {
            Directory.CreateDirectory(path);
            // Inherited: a first init is slow enough that silence reads as a hang.
            var result = Wine(name, "wineboot", ["--init"], inherit: true);
            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"wineboot failed for '{name}' with exit code {result.ExitCode}");
            }
        }

        // Unconditional, so an older prefix gains a location added later.
        foreach (var directory in layout.PrefixPluginDirs(name))
        {
            Directory.CreateDirectory(directory);
        }

        return new Prefix(name, path, true);
    }

    /// <summary>Deletes a prefix and everything installed in it.</summary>
    /// <remarks>
    /// The name is resolved and checked to sit directly under the prefixes directory: this
    /// deletes recursively, and <c>Path.Combine</c> would otherwise let a name containing
    /// <c>..</c> walk out of it.
    /// </remarks>
    public void Delete(string name)
    {
        var path = Path.GetFullPath(layout.PrefixPath(name));

        if (Path.GetDirectoryName(path) != layout.PrefixesDir)
        {
            throw new ArgumentException($"not a prefix name: '{name}'", nameof(name));
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"no such prefix: {name}");
        }

        Directory.Delete(path, recursive: true);
    }

    public ProcessResult Install(string name, string installer, bool inherit = false)
    {
        var full = Path.GetFullPath(installer);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"no such installer: {full}", full);
        }

        return Wine(name, "wine", [full], inherit);
    }

    public ProcessResult Run(
        string name, string command, IReadOnlyList<string> arguments, bool inherit = false) =>
        Wine(name, command, arguments, inherit);

    private ProcessResult Wine(
        string prefix, string command, IReadOnlyList<string> arguments, bool inherit)
    {
        var environment = new Dictionary<string, string>
        {
            // Explicit: org.winehq.Wine bakes WINEPREFIX=/var/data/wine into its metadata.
            ["WINEPREFIX"] = layout.PrefixPath(prefix),
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINELOADER"] = Layout.Wine,
        };

        return runner.Run(command, arguments, environment, inherit);
    }
}
