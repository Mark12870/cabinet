using System.Collections.Concurrent;

namespace Cabinet.Core;

public sealed partial class Prefixes
{
    public static readonly IReadOnlySet<string> Blanked = new HashSet<string>(["WAYLAND_DISPLAY"]);

    public const string JoinMode = "--cabinet-join";
    public const string SessionMode = "--cabinet-session";
    public const string PathsMode = "--cabinet-paths";
    public const string SessionLiveWord = "live";

    private static readonly ConcurrentDictionary<string, bool> WineStartedUnderClaim =
        new(StringComparer.Ordinal);

    public ProcessResult Run(
        string name, string command, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null, string? logTo = null, bool interactive = false) =>
        Wine(name, command, arguments, onOutput, logTo: logTo, interactive: interactive);

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

    internal PrefixClaim Claim(string name, string what, TimeSpan settle)
    {
        var path = layout.PrefixPath(name);
        var claim = PrefixClaim.Take(
            Session(name), name, what, apps: true, settle, () => SessionLive(name),
            () => EndWineStartedUnderClaim(name, path));

        if (claim.Holds)
        {
            WineStartedUnderClaim.TryAdd(path, false);
        }

        return claim;
    }

    private void EndWineStartedUnderClaim(string name, string path)
    {
        if (WineStartedUnderClaim.TryRemove(path, out var started) && started)
        {
            Wine(name, "wineserver", ["-k"], null);
            Wine(name, "wineserver", ["-w"], null);
        }
    }

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

    public ProcessResult RunJoined(
        string name, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null, string? logTo = null, bool interactive = false) =>
        Shim(name, [JoinMode, .. arguments], onOutput, logTo, interactive);

    internal ProcessResult RunMaintenance(
        string prefix, string command, IReadOnlyList<string> arguments,
        Action<string>? onOutput = null, string? dllOverrides = null) =>
        LoggedMaintenance(
            logTo => Wine(prefix, command, arguments, null, dllOverrides, logTo), onOutput);

    internal ProcessResult RunRegistry(
        string name, IReadOnlyList<string> arguments, Action<string>? onOutput = null) =>
        LoggedMaintenance(
            logTo => Run(name, "wine", arguments, logTo: logTo), onOutput);

    internal ProcessResult RunRegistryJoined(
        string name, IReadOnlyList<string> arguments, Action<string>? onOutput = null) =>
        LoggedMaintenance(
            logTo => RunJoined(name, arguments, logTo: logTo), onOutput);

    private ProcessResult LoggedMaintenance(Func<string, ProcessResult> run, Action<string>? onOutput)
    {
        using var staging = Staging.Create(layout.TempDir, "maintenance");
        var log = Path.Combine(staging.Path, "maintenance.log");
        var result = run(log);
        var output = File.Exists(log) ? File.ReadAllText(log) : "";

        if (output.Length > 0)
        {
            using var reader = new StringReader(output);

            while (reader.ReadLine() is { } line)
            {
                onOutput?.Invoke(line);
            }
        }

        return new ProcessResult(result.ExitCode, output, "");
    }

    private ProcessResult Shim(
        string name, IReadOnlyList<string> arguments, Action<string>? onOutput, string? logTo,
        bool interactive = false) =>
        runner.Run(
            layout.ShimPath,
            arguments,
            SessionVariables(name, runners.Resolve(RunnerOf(name))),
            onOutput,
            logTo: logTo,
            blankEnvironment: Blanked,
            interactive: interactive);

    public IReadOnlyDictionary<string, string> Variables(string name)
    {
        var selected = runners.Resolve(RunnerOf(name));
        var environment = WineVariables(name, selected, null);
        environment["CABINET_PREFIX"] = layout.PrefixPath(name);
        environment["WINE"] = selected.Wine;

        return environment;
    }

    private ProcessResult Wine(
        string prefix,
        string command,
        IReadOnlyList<string> arguments,
        Action<string>? onOutput,
        string? dllOverrides = null,
        string? logTo = null,
        bool interactive = false)
    {
        if (dllOverrides is null && command != "wineserver" && SessionLive(prefix))
        {
            return RunJoined(
                prefix, command == "wine" ? arguments : [command, .. arguments], onOutput, logTo,
                interactive);
        }

        var selected = runners.Resolve(RunnerOf(prefix));
        var starting = command != "wineserver";
        WineStartedUnderClaim.TryUpdate(layout.PrefixPath(prefix), starting, !starting);
        var native = command.Contains('/') || command == "wineserver";

        return runner.Run(
            Executable(selected, native ? command : "wine"),
            native || command == "wine" ? arguments : [command, .. arguments],
            WineVariables(prefix, selected, dllOverrides),
            onOutput,
            logTo: logTo,
            blankEnvironment: Blanked,
            interactive: interactive);
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
