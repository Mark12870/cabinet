namespace Cabinet.Core;

public sealed record Prefix(
    string Name, string Path, bool Initialised, string Runner, string? Dxvk, SyncMode Sync,
    string? Desktop);

public sealed class Prefixes(Layout layout, IProcessRunner runner)
{
    private readonly Runners runners = new(layout, runner);
    private readonly Dxvk dxvk = new(layout, runner);
    private readonly VirtualDesktop desktop = new(layout, runner);
    private readonly PrefixSettings settings = new(layout);

    public static readonly IReadOnlyList<string> Blanked = ["WAYLAND_DISPLAY"];

    public const string JoinMode = "--cabinet-join";
    public const string SessionMode = "--cabinet-session";
    public const string SessionLiveWord = "live";

    public IReadOnlyList<Prefix> List()
    {
        if (!Directory.Exists(layout.PrefixesDir))
        {
            return [];
        }

        return Directory.EnumerateDirectories(layout.PrefixesDir)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path))
            .Select(Describe)
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

        ContainProfile(name);

        foreach (var directory in layout.PrefixPluginDirs(name))
        {
            Directory.CreateDirectory(directory);
        }

        return Describe(name);
    }

    public void ContainProfile(string name)
    {
        var users = Path.Combine(layout.PrefixPath(name), "drive_c", "users");

        if (!Directory.Exists(users))
        {
            return;
        }

        var inside = Path.GetFullPath(layout.PrefixPath(name)) + Path.DirectorySeparatorChar;

        foreach (var profile in Directory.EnumerateDirectories(users))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(profile))
            {
                if (new DirectoryInfo(entry).LinkTarget is not { } target
                    || Resolve(entry, target).StartsWith(inside, StringComparison.Ordinal))
                {
                    continue;
                }

                File.Delete(entry);
                Directory.CreateDirectory(entry);
            }
        }
    }

    private static string Resolve(string link, string target) =>
        Path.GetFullPath(
            Path.IsPathRooted(target)
                ? target
                : Path.Combine(Path.GetDirectoryName(link)!, target));

    private Prefix Describe(string name) =>
        new(
            name,
            layout.PrefixPath(name),
            Directory.Exists(Path.Combine(layout.PrefixPath(name), "dosdevices")),
            RunnerOf(name),
            dxvk.InstalledIn(name),
            settings.Sync(name),
            desktop.SizeIn(name));

    public void Delete(string name, Action<string>? onOutput = null)
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
        onOutput?.Invoke($"Deleted {path}");
        new Yabridgectl(layout, runner).Bridge(List(), onOutput);
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
        Action<string>? onOutput = null, string? logTo = null) =>
        Wine(name, command, arguments, onOutput, logTo: logTo);

    public bool SessionLive(string name) =>
        Shim(name, [SessionMode], null, null)
            .Stdout.Contains(SessionLiveWord, StringComparison.Ordinal);

    public ProcessResult RunJoined(
        string name, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null, string? logTo = null) =>
        Shim(name, [JoinMode, .. arguments], onOutput, logTo);

    private ProcessResult Shim(
        string name, IReadOnlyList<string> arguments, Action<string>? onOutput, string? logTo) =>
        runner.Run(
            layout.ShimPath,
            arguments,
            WineVariables(name, runners.Resolve(RunnerOf(name)), null),
            onOutput,
            logTo: logTo);

    public IReadOnlyDictionary<string, string> Variables(string name)
    {
        var selected = runners.Resolve(RunnerOf(name));
        var environment = WineVariables(name, selected, null);
        environment["CABINET_PREFIX"] = layout.PrefixPath(name);
        environment["WINE"] = selected.Wine;

        return environment;
    }

    private const string Unattended = "mscoree=d;mshtml=d";

    private ProcessResult Wine(
        string prefix,
        string command,
        IReadOnlyList<string> arguments,
        Action<string>? onOutput,
        string? dllOverrides = null,
        string? logTo = null)
    {
        var selected = runners.Resolve(RunnerOf(prefix));

        return runner.Run(
            Executable(selected, command),
            arguments,
            WineVariables(prefix, selected, dllOverrides),
            onOutput,
            logTo: logTo);
    }

    private Dictionary<string, string> WineVariables(
        string prefix, Runner selected, string? dllOverrides)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in settings.Variables(prefix))
        {
            environment[key] = value;
        }

        foreach (var (key, value) in PrefixSettings.SyncVariables(settings.Sync(prefix)))
        {
            environment[key] = value;
        }

        environment["WINEPREFIX"] = layout.PrefixPath(prefix);
        environment["YABRIDGE_TEMP_DIR"] = layout.SocketDir;
        environment["WINELOADER"] = selected.Wine;

        foreach (var name in Blanked)
        {
            environment[name] = "";
        }

        if (dllOverrides is not null)
        {
            environment["WINEDLLOVERRIDES"] = dllOverrides;
        }

        return environment;
    }

    private static string Executable(Runner selected, string command) =>
        selected.Bundled || command.Contains('/')
            ? command
            : Path.Combine(Path.GetDirectoryName(selected.Wine)!, command);
}
