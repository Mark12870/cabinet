namespace Cabinet.Core;

public sealed record Runner(string Name, string Wine, bool Bundled)
{
    public bool Usable => Bundled || File.Exists(Wine);

    public bool Multilib =>
        Bundled
        || Directory.Exists(Path.Combine(Root, "lib32"))
        || Directory.Exists(Path.Combine(Root, "lib", "wine", "i386-unix"));

    private string Root => Path.GetDirectoryName(Path.GetDirectoryName(Wine)!)!;
}

public sealed class Runners(Layout layout, IProcessRunner runner)
{
    public IReadOnlyList<Runner> List()
    {
        var runners = new List<Runner> { Bundled };

        if (!Directory.Exists(layout.RunnersDir))
        {
            return runners;
        }

        runners.AddRange(Directory.EnumerateDirectories(layout.RunnersDir)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path))
            .Where(name => name != Layout.BundledRunner)
            .Select(name => new Runner(name, layout.RunnerWine(name), Bundled: false)));

        return runners;
    }

    public Runner Bundled => new(Layout.BundledRunner, Layout.Wine, Bundled: true);

    public Runner Resolve(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == Layout.BundledRunner)
        {
            return Bundled;
        }

        var runner = new Runner(name, layout.RunnerWine(name), Bundled: false);

        if (!runner.Usable)
        {
            throw new InvalidOperationException(
                $"runner '{name}' has no {Path.Combine("bin", "wine")} — "
                + $"unpack one into {layout.RunnerPath(name)}");
        }

        return runner;
    }

    public string Version(Runner selected)
    {
        var result = runner.Run(selected.Wine, ["--version"], new Dictionary<string, string>
        {
            ["WINEPREFIX"] = Path.Combine(layout.RunnersDir, ".probe"),
            ["WINELOADER"] = selected.Wine,
        });

        return result.Ok
            ? result.Stdout.Split('\n').FirstOrDefault()?.Trim() ?? "unknown"
            : "unknown";
    }

    public IReadOnlyList<string> InUseBy(string name) =>
        new Prefixes(layout, runner).List()
            .Where(prefix => prefix.Runner == name)
            .Select(prefix => prefix.Name)
            .ToList();

    public Runner Install(RunnerRelease release)
    {
        var staging = Path.Combine(Path.GetTempPath(), "cabinet-runner");

        try
        {
            var tarball = new RunnerIndex(runner).Download(release, staging);
            return Unpack(tarball, release.Name);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    public Runner Add(string tarball, string? name = null)
    {
        var full = Path.GetFullPath(tarball);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"no such archive: {full}", full);
        }

        return Unpack(full, name ?? DeriveName(full));
    }

    public void Remove(string name)
    {
        if (name == Layout.BundledRunner)
        {
            throw new ArgumentException("the bundled Wine ships in the Flatpak", nameof(name));
        }

        var path = Path.GetFullPath(layout.RunnerPath(name));

        if (Path.GetDirectoryName(path) != layout.RunnersDir)
        {
            throw new ArgumentException($"not a runner name: '{name}'", nameof(name));
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"no such runner: {name}");
        }

        var used = InUseBy(name);
        if (used.Count > 0)
        {
            throw new InvalidOperationException(
                $"{name} is still used by {string.Join(", ", used)} — "
                + $"move them with `cabinet use <prefix> {Layout.BundledRunner}` first");
        }

        Directory.Delete(path, recursive: true);
    }

    public static string DeriveName(string tarball)
    {
        var name = Path.GetFileName(tarball);

        foreach (var suffix in new[] { ".tar.xz", ".tar.gz", ".tar.zst", ".tar.bz2", ".tar" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return name.Replace("-x86_64", "").Replace("-amd64", "");
    }

    private static void ShareBundledRuntimes(string runnerPath)
    {
        var share = Path.Combine(runnerPath, "share", "wine");
        Directory.CreateDirectory(share);

        foreach (var runtime in new[] { "mono", "gecko" })
        {
            var link = Path.Combine(share, runtime);
            var target = Path.Combine(Layout.BundledWineShare, runtime);

            if (!Path.Exists(link) && Directory.Exists(target))
            {
                File.CreateSymbolicLink(link, target);
            }
        }
    }

    private Runner Unpack(string tarball, string name)
    {
        var path = layout.RunnerPath(name);

        if (Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"runner '{name}' is already there — remove it with `cabinet runners rm {name}`");
        }

        Directory.CreateDirectory(path);

        try
        {
            var result = runner.Run(
                "tar", ["-xf", tarball, "--strip-components=1", "-C", path], inherit: true);

            if (!result.Ok)
            {
                throw new InvalidOperationException($"could not unpack {tarball}");
            }

            var unpacked = new Runner(name, layout.RunnerWine(name), Bundled: false);

            if (!unpacked.Usable)
            {
                throw new InvalidOperationException(
                    $"{tarball} has no {Path.Combine("bin", "wine")}, so it is not a Wine build");
            }

            if (!unpacked.Multilib)
            {
                throw new InvalidOperationException(
                    $"{tarball} carries no 32-bit tree, so yabridge's 32-bit host cannot run "
                    + "under it — this is what a wow64 build looks like; take the amd64 one");
            }

            ShareBundledRuntimes(path);
            return unpacked;
        }
        catch
        {
            Directory.Delete(path, recursive: true);
            throw;
        }
    }
}
