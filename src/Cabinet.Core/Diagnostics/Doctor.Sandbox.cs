namespace Cabinet.Core;

public sealed partial class Doctor
{
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

    internal static IEnumerable<Check> ThirtyTwoBit(IniFile info)
    {
        const string compat = "org.freedesktop.Platform.Compat.i386";
        const string gl = "org.freedesktop.Platform.GL.";
        const string gl32 = "org.freedesktop.Platform.GL32.";

        var runtime = info.Get("Instance", "runtime-extensions");
        if (runtime is null)
        {
            yield break;
        }

        var mounted = ExtensionNames(info.Get("Instance", "app-extensions") ?? "");

        yield return mounted.Contains(compat)
            ? new Check("32-bit libraries", Status.Ok, compat)
            : new Check("32-bit libraries", Status.Fail,
                $"{compat} is not installed, so no Wine that carries a 32-bit loader can start "
                + $"— run `flatpak update {Layout.AppId}`");

        var missing = ExtensionNames(runtime)
            .Where(name => name.StartsWith(gl, StringComparison.Ordinal))
            .Select(name => gl32 + name[gl.Length..])
            .Where(name => !mounted.Contains(name))
            .Distinct()
            .ToList();

        yield return missing.Count == 0
            ? new Check("32-bit graphics", Status.Ok, "matches the graphics driver")
            : new Check("32-bit graphics", Status.Warn,
                "32-bit plugins cannot draw their editors — run "
                + string.Join(" and ", missing.Select(name => $"`flatpak install flathub {name}`")));
    }

    private static HashSet<string> ExtensionNames(string extensions) =>
        extensions.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(extension => extension.Split('=')[0])
            .ToHashSet(StringComparer.Ordinal);

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
