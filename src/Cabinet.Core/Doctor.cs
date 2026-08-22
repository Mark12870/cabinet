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
        checks.AddRange(PluginRunners());
        checks.AddRange(EnrolledDaws());
        return checks;
    }

    private IEnumerable<(string Prefix, string Runner)> PrefixesAndRunners()
    {
        if (!Directory.Exists(layout.PrefixesDir))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(layout.PrefixesDir)
                     .OrderBy(d => d, StringComparer.Ordinal))
        {
            var prefix = Path.GetFileName(dir);
            var marker = layout.PrefixRunnerFile(prefix);
            var name = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";

            yield return (prefix, name.Length == 0 ? Layout.BundledRunner : name);
        }
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
                + $"prefix with `cabinet use <prefix> {Layout.BundledRunner}`");
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
            + "to it needs the DAW closed: `cabinet use <prefix> <runner>`.");
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
        var link = layout.SandboxYabridgeLink;
        var chainloader = Path.Combine(link, "libyabridge-chainloader-vst3.so");

        return File.Exists(chainloader)
            ? new Check("yabridgectl path", Status.Ok, $"{link} -> {layout.HostYabridgeDir}")
            : new Check("yabridgectl path", Status.Fail,
                $"{link} does not reach {layout.HostYabridgeDir}");
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
                + "--filesystem=xdg-run/yabridge:create");

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
                + "Put `[Manager]` and `DefaultLimitMEMLOCK=1G` in "
                + "/etc/systemd/user.conf.d/60-memlock.conf (and in system.conf.d/ for a "
                + "DAW started outside the user session), then log out and back in. "
                + "Not limits.conf: pam_limits does not reach a systemd-started app.");
    }

    private IEnumerable<Check> EnrolledDaws()
    {
        var appsDir = Path.Combine(layout.Home, ".var", "app");
        if (!Directory.Exists(appsDir))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(appsDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var dawId = Path.GetFileName(dir);
            if (dawId == Layout.AppId || !Path.Exists(layout.DawYabridgeLink(dawId)))
            {
                continue;
            }

            yield return EnrolledDaw(dawId);
        }
    }

    private Check EnrolledDaw(string dawId)
    {
        var overrides = Path.Combine(
            layout.Home, ".local", "share", "flatpak", "overrides", dawId);

        if (!File.Exists(overrides))
        {
            return new Check($"DAW {dawId}", Status.Fail,
                $"linked but not overridden — run `cabinet enrol {dawId}`");
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
