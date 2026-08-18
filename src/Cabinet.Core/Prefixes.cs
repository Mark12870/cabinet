namespace Cabinet.Core;

public sealed record Prefix(
    string Name, string Path, bool Initialised, string Runner, string? Dxvk);

public sealed class Prefixes(Layout layout, IProcessRunner runner)
{
    private readonly Runners runners = new(layout, runner);
    private readonly Dxvk dxvk = new(layout, runner);

    public IReadOnlyList<Prefix> List()
    {
        if (!Directory.Exists(layout.PrefixesDir))
        {
            return [];
        }

        return Directory.EnumerateDirectories(layout.PrefixesDir)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path))
            .Select(name => new Prefix(
                name,
                layout.PrefixPath(name),
                Directory.Exists(Path.Combine(layout.PrefixPath(name), "dosdevices")),
                RunnerOf(name),
                dxvk.InstalledIn(name)))
            .ToList();
    }

    public string RunnerOf(string name)
    {
        var marker = layout.PrefixRunnerFile(name);

        return File.Exists(marker)
            ? File.ReadAllText(marker).Trim() is { Length: > 0 } recorded
                ? recorded
                : Layout.BundledRunner
            : Layout.BundledRunner;
    }

    public void SetRunner(string name, string runnerName)
    {
        if (!Directory.Exists(layout.PrefixPath(name)))
        {
            throw new DirectoryNotFoundException($"no such prefix: {name}");
        }

        var resolved = runners.Resolve(runnerName);
        var marker = layout.PrefixRunnerFile(name);

        if (resolved.Bundled)
        {
            File.Delete(marker);
            return;
        }

        File.WriteAllText(marker, resolved.Name + Environment.NewLine);
    }

    public Prefix Create(string name, string? runnerName = null, Action<string>? onOutput = null)
    {
        var path = layout.PrefixPath(name);
        Directory.CreateDirectory(path);

        if (runnerName is not null)
        {
            SetRunner(name, runnerName);
        }

        if (!Directory.Exists(Path.Combine(path, "dosdevices")))
        {
            var result = Wine(name, "wineboot", ["--init"], onOutput, Unattended);
            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"wineboot failed for '{name}' with exit code {result.ExitCode}");
            }
        }

        foreach (var directory in layout.PrefixPluginDirs(name))
        {
            Directory.CreateDirectory(directory);
        }

        return new Prefix(name, path, true, RunnerOf(name), dxvk.InstalledIn(name));
    }

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

    public ProcessResult Install(string name, string installer, Action<string>? onOutput = null)
    {
        var full = Path.GetFullPath(installer);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"no such installer: {full}", full);
        }

        return Wine(name, "wine", [full], onOutput);
    }

    public ProcessResult Run(
        string name, string command, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null) =>
        Wine(name, command, arguments, onOutput);

    private const string Unattended = "mscoree=d;mshtml=d";

    private ProcessResult Wine(
        string prefix,
        string command,
        IReadOnlyList<string> arguments,
        Action<string>? onOutput,
        string? dllOverrides = null)
    {
        var selected = runners.Resolve(RunnerOf(prefix));

        var environment = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = layout.PrefixPath(prefix),
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINELOADER"] = selected.Wine,
            ["WAYLAND_DISPLAY"] = "",
        };

        if (dllOverrides is not null)
        {
            environment["WINEDLLOVERRIDES"] = dllOverrides;
        }

        return runner.Run(Executable(selected, command), arguments, environment, onOutput);
    }

    private static string Executable(Runner selected, string command) =>
        selected.Bundled || command.Contains('/')
            ? command
            : Path.Combine(Path.GetDirectoryName(selected.Wine)!, command);
}
