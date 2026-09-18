namespace Cabinet.Core;

public enum Status
{
    Ok,
    Warn,
    Fail,
}

public sealed record Check(string Name, Status Status, string Detail);

public sealed class Doctor(Layout layout, IProcessRunner runner)
{
    public IReadOnlyList<Check> Run()
    {
        var checks = new List<Check>
        {
            BundledYabridge(),
            YabridgectlCanFindIt(),
            Shim(),
            SocketDirectory(),
            SharedMemory(),
            MemoryLock(),
        };

        checks.AddRange(PrefixRunners());
        checks.AddRange(BrokenRunners());
        checks.AddRange(Unnamed());
        checks.AddRange(InstalledTwice());
        checks.AddRange(Unfinished());
        checks.AddRange(LeftOpen());
        checks.AddRange(PartialDxvk());
        checks.AddRange(Retired());
        checks.AddRange(PluginRunners());
        checks.AddRange(PluginSync());
        checks.AddRange(PluginEnv());
        checks.AddRange(EnrolledDaws());
        checks.AddRange(NativeDaw());
        return checks;
    }

    private IEnumerable<(string Prefix, string Runner)> PrefixesAndRunners()
    {
        var prefixes = new Prefixes(layout, runner);

        return prefixes.Names().Select(name => (name, prefixes.RunnerOf(name)));
    }

    private IEnumerable<Check> PrefixRunners()
    {
        var prefixes = PrefixesAndRunners().ToList();

        if (prefixes.Count == 0)
        {
            yield break;
        }

        var broken = prefixes
            .Where(entry => entry.Runner != Layout.BundledRunner
                            && !File.Exists(layout.RunnerWine(entry.Runner)))
            .Select(entry => $"{entry.Prefix} -> {entry.Runner}")
            .ToList();

        yield return broken.Count == 0
            ? new Check("prefix runners", Status.Ok, "every prefix resolves to a Wine")
            : new Check("prefix runners", Status.Fail,
                $"missing runner for {string.Join(", ", broken)} — install it or move the "
                + "prefix to another Wine");
    }

    private IEnumerable<Check> BrokenRunners()
    {
        var broken = new Runners(layout, runner).List()
            .Where(installed => !installed.Usable)
            .Select(installed => installed.Name)
            .ToList();

        if (broken.Count > 0)
        {
            yield return new Check("broken runners", Status.Warn,
                $"{string.Join(", ", broken)} in {layout.RunnersDir} "
                + $"{(broken.Count == 1 ? "has" : "have")} no {Path.Combine("bin", "wine")}, "
                + "so no prefix can run on "
                + $"{(broken.Count == 1 ? "it — delete it and install it" : "them — delete them and install them")} "
                + "again");
        }
    }

    private IEnumerable<Check> Unfinished()
    {
        var library = new Library(layout, runner);
        var names = Names(library);
        var unfinished = library.Unfinished()
            .Select(left => left.Prefix is { } prefix
                ? $"{names(left.Id)} in {prefix}"
                : names(left.Id))
            .ToList();

        if (unfinished.Count > 0)
        {
            yield return new Check("unfinished installs", Status.Warn,
                $"{string.Join(", ", unfinished)} stopped part-way through installing. "
                + "Installing it again finishes it and clears what the first try left.");
        }
    }

    private IEnumerable<Check> LeftOpen()
    {
        var library = new Library(layout, runner);
        var names = Names(library);
        var open = library.LeftOpen()
            .Select(left => $"{names(left.Id)} in {left.Prefix}")
            .ToList();

        if (open.Count > 0)
        {
            yield return new Check("apps left open", Status.Warn,
                $"Cabinet stopped while {string.Join(", ", open)} was open, so what it installed "
                + "may not be bridged. Open it and close it again, and Cabinet finishes what it "
                + "does when an app closes.");
        }
    }

    private IEnumerable<Check> PartialDxvk()
    {
        var dxvk = new Dxvk(layout, runner);
        var partial = new Prefixes(layout, runner).Names().Where(dxvk.Partial).ToList();

        if (partial.Count > 0)
        {
            yield return new Check("partial DXVK", Status.Warn,
                $"{string.Join(", ", partial)} {(partial.Count == 1 ? "holds" : "hold")} part of "
                + "DXVK but Cabinet does not count it as on. Turn DXVK on to finish it, or take "
                + "it out.");
        }
    }

    private IEnumerable<Check> Retired()
    {
        var retired = new Library(layout, runner).Retired().Select(entry => entry.Id).ToList();

        if (retired.Count > 0)
        {
            yield return new Check("retired plugins", Status.Warn,
                $"{string.Join(", ", retired)} {(retired.Count == 1 ? "is" : "are")} installed "
                + "but no longer in this build's catalogue. They keep working, and the Library "
                + "lists them under No longer in the catalogue, where they can be removed.");
        }
    }

    private static Func<string, string> Names(Library library)
    {
        var entries = library.Entries()
            .ToDictionary(entry => entry.Id, entry => entry.Name, StringComparer.Ordinal);

        return id => entries.GetValueOrDefault(id, id);
    }

    private IEnumerable<Check> Unnamed()
    {
        var unnamed = new Prefixes(layout, runner).Unnamed();

        if (unnamed.Count > 0)
        {
            yield return new Check("prefix names", Status.Warn,
                $"{string.Join(", ", unnamed.Select(name => $"'{name}'"))} in "
                + $"{layout.PrefixesDir} cannot name a prefix, so Cabinet leaves them out — "
                + "rename them to one word of a path, not starting with a dot");
        }
    }

    private IEnumerable<Check> InstalledTwice()
    {
        var library = new Library(layout, runner);
        var installed = library.Installed();
        var twice = library.InstalledMoreThanOnce()
            .OrderBy(held => held.Key, StringComparer.Ordinal)
            .Select(held =>
                $"{held.Key} is recorded in {string.Join(" and ", held.Value)}, and Cabinet "
                + $"acts only on the one in {installed[held.Key]}")
            .ToList();

        if (twice.Count == 0)
        {
            yield break;
        }

        yield return new Check("installed twice", Status.Warn,
            string.Join("; ", twice)
            + ". Remove it once for each copy and install it again where you want it; a plugin "
            + "is installed in one prefix.");
    }

    private IEnumerable<Check> PluginRunners()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        var drifted = PrefixesAndRunners()
            .SelectMany(
                prefix => library.Recorded(prefix.Prefix),
                (prefix, id) => (prefix.Prefix, prefix.Runner, Id: id))
            .Select(held => (held.Prefix, held.Runner,
                Entry: entries.GetValueOrDefault(held.Id)))
            .Where(held => held.Entry?.Runner is { } wanted
                           && !Library.Answers(held.Runner, wanted))
            .Select(held =>
                $"{held.Prefix} keeps {held.Runner}, where {held.Entry!.Name} asks for "
                + $"Wine {held.Entry.Runner}")
            .ToList();

        if (drifted.Count == 0)
        {
            yield break;
        }

        yield return new Check("plugin runners", Status.Warn,
            string.Join("; ", drifted)
            + ". A plugin's entry pins the Wine its editor was tried on, and moving a prefix "
            + "to it needs the DAW closed.");
    }

    private IEnumerable<Check> PluginSync()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var settings = new PrefixSettings(layout);

        var drifted = PrefixesAndRunners()
            .SelectMany(
                prefix => library.Recorded(prefix.Prefix),
                (prefix, id) => (prefix.Prefix, Mode: settings.Sync(prefix.Prefix),
                    Entry: entries.GetValueOrDefault(id)))
            .Where(held => held.Entry is { Sync: not SyncMode.System } entry
                           && entry.Sync != held.Mode)
            .Select(held =>
                $"{held.Prefix} runs on {PrefixSettings.Word(held.Mode)}, where "
                + $"{held.Entry!.Name} asks for {PrefixSettings.Word(held.Entry.Sync)}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (drifted.Count == 0)
        {
            yield break;
        }

        yield return new Check("plugin sync", Status.Warn,
            string.Join("; ", drifted)
            + ". A prefix that already existed when the plugin was installed keeps the sync "
            + "mode it was made with, until you change it.");
    }

    private IEnumerable<Check> PluginEnv()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var settings = new PrefixSettings(layout);

        var missing = PrefixesAndRunners()
            .SelectMany(
                prefix => library.Recorded(prefix.Prefix),
                (prefix, id) => (prefix.Prefix, Held: settings.Variables(prefix.Prefix),
                    Entry: entries.GetValueOrDefault(id)))
            .Where(held => held.Entry is not null)
            .SelectMany(
                held => held.Entry!.Env.Where(
                    wanted => held.Held.GetValueOrDefault(wanted.Key) != wanted.Value),
                (held, wanted) =>
                    $"{held.Prefix} does not set {wanted.Key}={wanted.Value}, which "
                    + $"{held.Entry!.Name} asks for")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (missing.Count == 0)
        {
            yield break;
        }

        yield return new Check("plugin env", Status.Warn,
            string.Join("; ", missing)
            + ". A prefix that already existed when the plugin was installed keeps the "
            + "environment it was made with, until you set those variables on it.");
    }

    private Check BundledYabridge()
    {
        var host = Path.Combine(layout.HostYabridgeDir, "yabridge-host.exe");
        var library = Path.Combine(layout.HostYabridgeDir, "libyabridge-vst3.so");

        if (!File.Exists(host) || !File.Exists(library))
        {
            return new Check("yabridge readable", Status.Fail,
                $"{layout.HostYabridgeDir} is incomplete — reinstall Cabinet");
        }

        return new Check("yabridge readable", Status.Ok, layout.HostYabridgeDir);
    }

    private Check YabridgectlCanFindIt()
    {
        var link = layout.BridgeYabridgeLink;
        var chainloader = Path.Combine(link, "libyabridge-chainloader-vst3.so");

        return File.Exists(chainloader)
            ? new Check("yabridgectl path", Status.Ok,
                $"{link} -> {layout.BundledYabridgeDir}")
            : new Check("yabridgectl path", Status.Fail,
                $"{link} does not reach {layout.BundledYabridgeDir}");
    }

    private Check Shim()
    {
        if (!File.Exists(layout.ShimPath))
        {
            return new Check("shim readable", Status.Fail,
                $"{layout.ShimPath} is missing — reinstall Cabinet");
        }

        var executable = (File.GetUnixFileMode(layout.ShimPath) & UnixFileMode.UserExecute) != 0;
        return executable
            ? new Check("shim readable", Status.Ok, layout.ShimPath)
            : new Check("shim readable", Status.Fail, $"{layout.ShimPath} is not executable");
    }

    private Check SocketDirectory() =>
        Directory.Exists(layout.SocketDir)
            ? new Check("socket directory", Status.Ok, layout.SocketDir)
            : new Check("socket directory", Status.Fail,
                $"{layout.SocketDir} is missing — Cabinet lacks "
                + "--filesystem=xdg-run");

    private Check SharedMemory()
    {
        var devices = Layout.FlatpakInfo.Get("Context", "devices");
        if (devices is null)
        {
            return new Check("/dev/shm shared", Status.Warn,
                "not running inside a Flatpak, so nothing to check");
        }

        return devices.Split(';').Contains("shm")
            ? new Check("/dev/shm shared", Status.Ok, "--device=shm")
            : new Check("/dev/shm shared", Status.Fail,
                "Cabinet lacks --device=shm; audio buffers cannot cross the boundary");
    }

    private static Check MemoryLock()
    {
        var limit = ReadMemlockLimit();
        if (limit is null)
        {
            return new Check("memlock limit", Status.Warn, "could not read /proc/self/limits");
        }

        const long comfortable = 64L * 1024 * 1024;
        return limit >= comfortable
            ? new Check("memlock limit", Status.Ok, $"{limit / 1024 / 1024} MB")
            : new Check("memlock limit", Status.Warn,
                $"{limit / 1024 / 1024} MB — yabridge may not lock its audio buffers. "
                + "Put `[Manager]` and `DefaultLimitMEMLOCK=1G` in both "
                + "/etc/systemd/system.conf.d/60-memlock.conf and "
                + "/etc/systemd/user.conf.d/60-memlock.conf, then reboot. "
                + "Not limits.conf: pam_limits does not reach a systemd-started app.");
    }

    public IReadOnlyList<string> DawsMissingPermissions() =>
        EnrolledDawIds().Where(dawId => EnrolledDaw(dawId).Status != Status.Ok).ToList();

    private IEnumerable<Check> EnrolledDaws() => EnrolledDawIds().Select(EnrolledDaw);

    private IEnumerable<string> EnrolledDawIds()
    {
        if (!Directory.Exists(layout.AppsDir))
        {
            return [];
        }

        return Directory.EnumerateDirectories(layout.AppsDir)
            .OrderBy(d => d, StringComparer.Ordinal)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(dawId => dawId != Layout.AppId && Path.Exists(layout.DawYabridgeLink(dawId)));
    }

    private IEnumerable<Check> NativeDaw()
    {
        var entries = Layout.BridgedScanDirectories
            .Select(directory => (Link: layout.CabinetScanDir(directory),
                Target: layout.BridgeOutputDir(directory)))
            .ToList();
        var unlinked = entries
            .Where(entry => new DirectoryInfo(entry.Link).LinkTarget != entry.Target)
            .Select(entry => entry.Link)
            .ToList();
        var owned = unlinked
            .Where(link => Path.Exists(link) || new FileInfo(link).LinkTarget is not null)
            .ToList();

        if (owned.Count > 0)
        {
            yield return new Check("native DAWs", Status.Fail,
                $"already owned by something else: {string.Join(", ", owned)} "
                + "— move it aside and bridge what is installed again");
            yield break;
        }

        if (unlinked.Count > 0)
        {
            yield return new Check("native DAWs", Status.Fail,
                $"missing Cabinet scan paths: {string.Join(", ", unlinked)} "
                + "— bridge what is installed again");
            yield break;
        }

        var hasPlugins = entries.Any(entry => Directory.Exists(entry.Target)
            && Directory.EnumerateFiles(entry.Target, "*", SearchOption.AllDirectories)
                .Any(file => Path.GetExtension(file) is ".so" or ".clap"));

        yield return hasPlugins
            ? new Check("native DAWs", Status.Ok,
                $"Cabinet plugins are under {string.Join(", ", entries.Select(e => e.Link))}; "
                + $"independent yabridge remains at {layout.NativeYabridgeDir}")
            : new Check("native DAWs", Status.Fail,
                "no Cabinet native plugins have been published — bridge what is installed again");
    }

    private Check EnrolledDaw(string dawId)
    {
        var overrides = layout.FlatpakOverride(dawId);

        if (!File.Exists(overrides))
        {
            return new Check($"DAW {dawId}", Status.Fail,
                $"linked but not overridden — enrol {dawId} again");
        }

        var ini = IniFile.Parse(File.ReadAllLines(overrides));
        var missing = new List<string>();

        if (!(ini.Get("Context", "devices")?.Split(';').Contains("shm") ?? false))
        {
            missing.Add("--device=shm");
        }

        var filesystems = ini.Get("Context", "filesystems");

        if (!(filesystems?.Contains("xdg-run/yabridge") ?? false))
        {
            missing.Add("--filesystem=xdg-run/yabridge:create");
        }

        if (!(filesystems?.Contains(layout.HostAppFiles) ?? false))
        {
            missing.Add($"--filesystem={layout.HostAppFiles}:ro");
        }

        if (!(filesystems?.Contains(layout.PrefixesDir) ?? false))
        {
            missing.Add($"--filesystem={layout.PrefixesDir}:ro");
        }

        if (!(filesystems?.Contains(layout.NativeDir) ?? false))
        {
            missing.Add($"--filesystem={layout.NativeDir}:ro");
        }

        if (!(filesystems?.Contains(layout.BridgeHome) ?? false))
        {
            missing.Add($"--filesystem={layout.BridgeHome}:ro");
        }

        if (ini.Get("Session Bus Policy", "org.freedesktop.Flatpak") != "talk")
        {
            missing.Add("--talk-name=org.freedesktop.Flatpak");
        }

        if (ini.Get("Environment", "WINELOADER") != layout.ShimPath)
        {
            missing.Add($"--env=WINELOADER={layout.ShimPath}");
        }

        if (ini.Get("Environment", "YABRIDGE_TEMP_DIR") != layout.SocketDir)
        {
            missing.Add($"--env=YABRIDGE_TEMP_DIR={layout.SocketDir}");
        }

        if (ini.Get("Environment", "YABRIDGE_DEBUG_FILE") != layout.RuntimeLogPath)
        {
            missing.Add($"--env=YABRIDGE_DEBUG_FILE={layout.RuntimeLogPath}");
        }

        return missing.Count == 0
            ? new Check($"DAW {dawId}", Status.Ok, "enrolled")
            : new Check($"DAW {dawId}", Status.Fail, "missing " + string.Join(", ", missing));
    }

    private static long? ReadMemlockLimit()
    {
        if (!File.Exists("/proc/self/limits"))
        {
            return null;
        }

        foreach (var line in File.ReadLines("/proc/self/limits"))
        {
            if (!line.StartsWith("Max locked memory", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line["Max locked memory".Length..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return fields.Length > 0 && long.TryParse(fields[0], out var soft) ? soft : null;
        }

        return null;
    }
}
