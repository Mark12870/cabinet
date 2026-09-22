namespace Cabinet.Core;

public sealed record Prefix(
    string Name, string Path, bool Initialised, string Runner, string? Dxvk, SyncMode Sync,
    bool Desktop);

public sealed partial class Prefixes(Layout layout, IProcessRunner runner)
{
    private readonly Runners runners = new(layout, runner);
    private readonly Dxvk dxvk = new(layout, runner);
    private readonly VirtualDesktop desktop = new(layout, runner);
    private readonly PrefixSettings settings = new(layout);

    private const string Unattended = "mscoree=d;mshtml=d";

    public IReadOnlyList<Prefix> List() => [.. Names().Select(Describe)];

    public IReadOnlyList<string> Names() => [.. Directories().Where(Layout.IsName)];

    public IReadOnlyList<string> Unnamed() =>
        [.. Directories().Where(name => !Layout.IsName(name) && !Staging.Owns(name))];

    private IEnumerable<string> Directories() =>
        Directory.Exists(layout.PrefixesDir)
            ? Directory.EnumerateDirectories(layout.PrefixesDir)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => Path.GetFileName(path))
            : [];

    public string RunnerOf(string name)
    {
        var marker = layout.PrefixRunnerFile(name);

        return File.Exists(marker)
            ? File.ReadAllText(marker).Trim() is var recorded && Layout.IsName(recorded)
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

    public string? NewNameProblem(string name) =>
        name.Length == 0 ? "A new prefix needs a name"
        : !Layout.IsName(name) ? "One word of a path, not starting with a dot"
        : Directory.Exists(layout.PrefixPath(name)) ? $"A prefix named {name} is already there"
        : null;

    public Prefix Create(string name, string? runnerName = null, Action<string>? onOutput = null)
    {
        if (Directory.Exists(layout.PrefixPath(name)))
        {
            throw new InvalidOperationException($"a prefix named {name} is already there");
        }

        return Prepare(name, runnerName, onOutput);
    }

    internal Prefix Prepare(string name, string? runnerName, Action<string>? onOutput)
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
        var path = layout.PrefixPath(name);

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"no such prefix: {name}");
        }

        using var claim = Claim(name, $"delete {name}");

        using (var deleting = Staging.Create(layout.PrefixesDir, "prefix"))
        {
            Directory.Move(path, Path.Combine(deleting.Path, name));
        }

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

        Prepare(name, null, onOutput);
        var result = RunInstaller(name, installer, onOutput);
        Bridge(onOutput);

        return result;
    }

    internal ProcessResult RunInstaller(string name, string installer, Action<string>? onOutput)
    {
        using var claim = Claim(name, $"run an installer in {name}");

        return Wine(name, "wine", [Path.GetFullPath(installer)], onOutput);
    }

    public void SetSync(string name, SyncMode mode, Action<string>? onOutput = null)
    {
        using var claim = Claim(name, $"change how {name} synchronises");
        settings.SetSync(name, mode);
        onOutput?.Invoke($"{name} now waits on {PrefixSettings.Word(mode)}.");
    }
}
