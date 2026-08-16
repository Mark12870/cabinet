namespace Cabinet.Core;

public enum Status
{
    Ok,
    Warn,
    Fail,
}

public sealed record Check(string Name, Status Status, string Detail);

/// <summary>
/// The checks that actually break this setup, in the order they break it.
/// </summary>
/// <remarks>
/// Every item here corresponds to something that failed during bring-up. A DAW gives
/// almost no diagnostics when a plugin fails to scan, so the point of doctor is to say
/// which side of the boundary is wrong before you go looking in a plugin log.
/// </remarks>
public sealed class Doctor(Layout layout)
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

        checks.AddRange(EnrolledDaws());
        return checks;
    }

    /// <summary>
    /// Cabinet copies nothing onto the host, so this checks the install tree itself is
    /// where <c>/.flatpak-info</c> said and still holds what a DAW has to load.
    /// </summary>
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

    /// <summary>
    /// yabridgectl searches its own <c>$XDG_DATA_HOME/yabridge</c> and nowhere Cabinet
    /// keeps anything. Without the link every <c>cabinet sync</c> fails, and the message
    /// it fails with does not say why.
    /// </summary>
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

        // yabridge's winegcc wrapper falls back to plain `wine` when $WINELOADER is
        // not an executable file, which on a host without Wine means silence.
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

    /// <summary>
    /// yabridge's audio buffers are <c>shm_open()</c>. Flatpak's <c>--device=all</c>
    /// does <em>not</em> include <c>/dev/shm</c>; without <c>--device=shm</c> each
    /// sandbox gets a private one and no buffer is ever shared.
    /// </summary>
    private Check SharedMemory()
    {
        var devices = FlatpakInfo.Value.Get("Context", "devices");
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

    /// <summary>
    /// The remedy names a systemd drop-in and not <c>limits.conf</c> on purpose:
    /// <c>pam_limits</c> does not reach anything the systemd user manager starts, which
    /// is every Flatpak DAW launched from a desktop. A <c>limits.d</c> entry looks
    /// applied and changes nothing.
    /// </summary>
    private static Check MemoryLock()
    {
        var limit = ReadMemlockLimit();
        if (limit is null)
        {
            return new Check("memlock limit", Status.Warn, "could not read /proc/self/limits");
        }

        // yabridge warns below roughly this; it locks its audio buffers into RAM.
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

    /// <summary>
    /// Verifies each enrolled DAW by reading its user override file directly. Cheaper
    /// and less privileged than asking flatpak, which Cabinet cannot do from inside
    /// its own sandbox anyway.
    /// </summary>
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

        // Without these the DAW cannot read the chainloader, nor the plugin the bundle
        // symlinks to: flatpak masks both ~/.local/share/flatpak and another app's
        // ~/.var/app even under --filesystem=home.
        if (!(filesystems?.Contains(layout.HostAppFiles) ?? false))
        {
            missing.Add($"--filesystem={layout.HostAppFiles}:ro");
        }

        if (!(filesystems?.Contains(layout.PrefixesDir) ?? false))
        {
            missing.Add($"--filesystem={layout.PrefixesDir}:ro");
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

    private static readonly Lazy<IniFile> FlatpakInfo = new(() =>
        File.Exists("/.flatpak-info")
            ? IniFile.Parse(File.ReadAllLines("/.flatpak-info"))
            : IniFile.Empty);

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
