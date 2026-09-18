namespace Cabinet.Core;

public sealed record Prefix(
    string Name, string Path, bool Initialised, string Runner, string? Dxvk, SyncMode Sync,
    bool Desktop);

public sealed class Prefixes(Layout layout, IProcessRunner runner)
{
    private readonly Runners runners = new(layout, runner);
    private readonly Dxvk dxvk = new(layout, runner);
    private readonly VirtualDesktop desktop = new(layout, runner);
    private readonly PrefixSettings settings = new(layout);

    public static readonly IReadOnlySet<string> Blanked = new HashSet<string>(["WAYLAND_DISPLAY"]);

    public const string JoinMode = "--cabinet-join";
    public const string SessionMode = "--cabinet-session";
    public const string PathsMode = "--cabinet-paths";
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

        using var claim = Claim(name, $"give {name} a different Wine");
        var resolved = runners.Resolve(runnerName);
        var marker = layout.PrefixRunnerFile(name);

        if (resolved.Bundled)
        {
            File.Delete(marker);
            return;
        }

        File.WriteAllText(marker, resolved.Name + Environment.NewLine);
    }

    public void MoveToRunner(string name, string runnerName, Action<string>? onOutput = null)
    {
        using var claim = Claim(name, $"give {name} a different Wine");
        SetRunner(name, runnerName);

        var updated = Run(name, "wineboot", ["-u"], onOutput);
        if (!updated.Ok)
        {
            throw new InvalidOperationException($"wineboot exited with {updated.ExitCode}");
        }
    }

    public Prefix Create(string name, string? runnerName = null, Action<string>? onOutput = null)
    {
        var path = layout.PrefixPath(name);
        Directory.CreateDirectory(path);

        using var claim = Claim(name, $"set {name} up");

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
            desktop.EnabledIn(name));

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

        using var claim = Claim(name, $"delete {name}");
        Directory.Delete(path, recursive: true);
        onOutput?.Invoke($"Deleted {path}");
        Bridge(onOutput);
    }

    public void Bridge(Action<string>? onOutput = null) =>
        new Yabridgectl(layout, runner).Bridge(List(), onOutput);

    public ProcessResult Install(string name, string installer, Action<string>? onOutput = null)
    {
        var full = Path.GetFullPath(installer);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"no such installer: {full}", full);
        }

        using var claim = Claim(name, $"run an installer in {name}");

        return Wine(name, "wine", [full], onOutput);
    }

    public ProcessResult Run(
        string name, string command, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null, string? logTo = null, bool inheritStdin = false) =>
        Wine(name, command, arguments, onOutput, logTo: logTo, inheritStdin: inheritStdin);

    public bool SessionLive(string name) =>
        Ask(name, SessionMode).Stdout.Contains(SessionLiveWord, StringComparison.Ordinal);

    public SessionPaths Session(string name) => SessionPaths.Parse(Ask(name, PathsMode).Stdout);

    private ProcessResult Ask(string name, string mode) =>
        runner.Run(
            layout.ShimPath,
            [mode],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["WINEPREFIX"] = layout.PrefixPath(name),
                ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            });

    public PrefixClaim Claim(string name, string what) =>
        Claim(name, what, PrefixClaim.Settle);

    internal PrefixClaim Claim(string name, string what, TimeSpan settle) =>
        PrefixClaim.Take(
            Session(name), name, what, apps: true, settle, () => SessionLive(name));

    public PrefixClaim Guard(string name, string what) =>
        PrefixClaim.Take(
            Session(name), name, what, apps: false, TimeSpan.Zero, () => SessionLive(name));

    public PrefixApps OpenApp(string name, string what) =>
        PrefixClaim.Open(Session(name), name, what);

    public IReadOnlyList<string> LiveSessionsUsing(string runnerName)
    {
        if (!Directory.Exists(layout.SocketDir))
        {
            return [];
        }

        var inside = Path.GetFullPath(layout.RunnerPath(runnerName))
                     + Path.DirectorySeparatorChar;
        var found = new List<string>();

        foreach (var record in Directory.EnumerateFiles(layout.SocketDir, "*.session")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.ChangeExtension(record, ".sock")))
            {
                continue;
            }

            var noted = Noted(record);

            if (!noted.TryGetValue("runner", out var wine)
                || !wine.StartsWith(inside, StringComparison.Ordinal))
            {
                continue;
            }

            found.Add(noted.TryGetValue("prefix", out var path)
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(record));
        }

        return found;
    }

    private static IReadOnlyDictionary<string, string> Noted(string record)
    {
        var noted = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(record))
        {
            var at = line.IndexOf(' ');

            if (at > 0)
            {
                noted[line[..at]] = line[(at + 1)..];
            }
        }

        return noted;
    }

    public void SetSync(string name, SyncMode mode)
    {
        using var claim = Claim(name, $"change how {name} synchronises");
        settings.SetSync(name, mode);
    }

    public ProcessResult RunJoined(
        string name, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null, string? logTo = null, bool inheritStdin = false) =>
        Shim(name, [JoinMode, .. arguments], onOutput, logTo, inheritStdin);

    private ProcessResult Shim(
        string name, IReadOnlyList<string> arguments, Action<string>? onOutput, string? logTo,
        bool inheritStdin = false) =>
        runner.Run(
            layout.ShimPath,
            arguments,
            SessionVariables(name, runners.Resolve(RunnerOf(name))),
            onOutput,
            logTo: logTo,
            blankEnvironment: Blanked,
            inheritStdin: inheritStdin);

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
        string? logTo = null,
        bool inheritStdin = false)
    {
        if (dllOverrides is null && command != "wineserver" && SessionLive(prefix))
        {
            return RunJoined(
                prefix, command == "wine" ? arguments : [command, .. arguments], onOutput, logTo,
                inheritStdin);
        }

        var selected = runners.Resolve(RunnerOf(prefix));

        return runner.Run(
            Executable(selected, command),
            arguments,
            WineVariables(prefix, selected, dllOverrides),
            onOutput,
            logTo: logTo,
            blankEnvironment: Blanked,
            inheritStdin: inheritStdin);
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
        environment["YABRIDGE_DEBUG_FILE"] = layout.RuntimeLogPath;
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

    private Dictionary<string, string> SessionVariables(string prefix, Runner selected)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in settings.Variables(prefix).Keys)
        {
            environment[key] = "";
        }

        foreach (var (key, value) in PrefixSettings.SyncVariables(settings.Sync(prefix)))
        {
            environment[key] = value;
        }

        environment["WINEPREFIX"] = layout.PrefixPath(prefix);
        environment["YABRIDGE_TEMP_DIR"] = layout.SocketDir;
        environment["YABRIDGE_DEBUG_FILE"] = layout.RuntimeLogPath;
        environment["WINELOADER"] = selected.Wine;

        return environment;
    }

    private static string Executable(Runner selected, string command) =>
        selected.Bundled || command.Contains('/')
            ? command
            : Path.Combine(Path.GetDirectoryName(selected.Wine)!, command);
}
