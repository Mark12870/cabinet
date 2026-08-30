using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

public sealed class TeardownTests
{
    private static readonly string HomeDirectory =
        Environment.GetEnvironmentVariable("HOME")
        ?? throw new InvalidOperationException("HOME is not set");

    [TeardownFact]
    public void TheShimAsksFlatpakToKillTheSandboxWithItsParent()
    {
        var argv = NativeArgv();

        Assert.Equal(["flatpak", "run", "--die-with-parent"], argv.Take(3));
        Assert.DoesNotContain("--watch-bus", argv);
    }

    [TeardownFact]
    public void TheShimWatchesTheBusWhenTheDawIsItselfSandboxed()
    {
        var argv = SandboxedArgv(RequireDaw());

        Assert.Equal(["flatpak-spawn", "--host", "--watch-bus"], argv.Take(3));
        Assert.Contains("--die-with-parent", argv);
        Assert.True(
            Position(argv, "--watch-bus") < Position(argv, "flatpak"),
            $"--watch-bus must reach flatpak-spawn, not flatpak run: {string.Join(' ', argv)}");
    }

    [TeardownFact]
    public void AWineSandboxDiesWithTheProcessThatStartedIt()
    {
        var marker = Marker();

        try
        {
            using var shim = StartShim(marker);
            Assert.True(AppearsWithin(Host.App, Signature(marker)), "the wine sandbox never started");

            shim.Kill(entireProcessTree: false);
            shim.WaitForExit();

            Assert.True(
                GoesAwayWithin(Host.App, Signature(marker)),
                "the wine sandbox outlived the process that started it");
        }
        finally
        {
            Host.KillAll(Host.App, Signature(marker));
        }
    }

    [TeardownFact]
    public void AWineSandboxDiesWithTheSandboxedDawThatStartedIt()
    {
        var daw = RequireDaw();
        var marker = Marker();

        try
        {
            using var outer = StartShimInside(daw, marker);
            Assert.True(AppearsWithin(Host.App, Signature(marker)), "the wine sandbox never started");

            var running = Host.Instances(daw, marker);
            Assert.True(running.Count > 0, $"no {daw} instance was carrying this run");

            foreach (var instance in running)
            {
                Host.Kill(instance);
            }

            Assert.True(
                GoesAwayWithin(Host.App, Signature(marker)),
                "the wine sandbox outlived the sandboxed DAW that started it");

            outer.Kill(entireProcessTree: true);
        }
        finally
        {
            Host.KillAll(daw, marker);
            Host.KillAll(Host.App, Signature(marker));
        }
    }

    private static int Position(IReadOnlyList<string> argv, string value)
    {
        for (var index = 0; index < argv.Count; index++)
        {
            if (argv[index] == value)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"{value} is missing from {string.Join(' ', argv)}");
    }

    private static string Marker() => Random.Shared.Next(100000, 999999).ToString();

    private static string Signature(string marker) => $"cmd /c ping -n {marker}";

    private static IReadOnlyList<string> Payload(string marker) =>
        ["cmd", "/c", "ping", "-n", marker, "127.0.0.1"];

    private static string Prefix()
    {
        var chosen = Environment.GetEnvironmentVariable("CABINET_TEARDOWN_PREFIX");

        if (!string.IsNullOrEmpty(chosen))
        {
            return chosen;
        }

        var prefixes = Path.Combine(
            HomeDirectory, ".var", "app", Host.App, "data", "prefixes");

        var first = Directory.Exists(prefixes)
            ? Directory.EnumerateDirectories(prefixes).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;

        return first ?? throw new InvalidOperationException(
            $"no wine prefix to test with; create one or set CABINET_TEARDOWN_PREFIX ({prefixes})");
    }

    private static string RequireDaw()
    {
        var daw = Environment.GetEnvironmentVariable("CABINET_TEARDOWN_DAW") ?? "fm.reaper.Reaper";

        return Host.Installed(daw)
            ? daw
            : throw new InvalidOperationException(
                $"{daw} is not installed; set CABINET_TEARDOWN_DAW to a sandboxed DAW that is");
    }

    private static Process StartShim(string marker)
    {
        var info = new ProcessStartInfo(Host.Shim())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.Environment["WINEPREFIX"] = Prefix();
        info.Environment["YABRIDGE_TEMP_DIR"] = SocketDirectory();

        foreach (var argument in Payload(marker))
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("could not start the shim");
    }

    private static Process StartShimInside(string daw, string marker)
    {
        var command =
            $"WINEPREFIX={Quote(Prefix())} YABRIDGE_TEMP_DIR={Quote(SocketDirectory())} " +
            $"{Quote(Host.Shim())} {string.Join(' ', Payload(marker).Select(Quote))}";

        var info = new ProcessStartInfo("flatpak")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--command=sh");
        info.ArgumentList.Add(daw);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);

        return Process.Start(info) ?? throw new InvalidOperationException($"could not start {daw}");
    }

    private static IReadOnlyList<string> NativeArgv()
    {
        var log = Path.Combine(Path.GetTempPath(), $"cabinet-argv-{Guid.NewGuid():N}.log");

        try
        {
            var info = new ProcessStartInfo(Host.Shim())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            info.Environment["CABINET_SHIM_LOG"] = log;
            info.Environment["CABINET_APP"] = Unlaunchable;
            info.Environment["WINEPREFIX"] = Prefix();

            using var process = Process.Start(info)
                                ?? throw new InvalidOperationException("could not start the shim");
            process.WaitForExit();

            return ParseArgv(File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
        }
    }

    private static IReadOnlyList<string> SandboxedArgv(string daw)
    {
        var name = $"cabinet-argv-{Guid.NewGuid():N}.log";
        var log = Path.Combine(HomeDirectory, ".var", "app", daw, "data", name);

        try
        {
            var command =
                $"CABINET_SHIM_LOG=\"$XDG_DATA_HOME/{name}\" CABINET_APP={Unlaunchable} " +
                $"WINEPREFIX={Quote(Prefix())} {Quote(Host.Shim())}";

            Host.Run("flatpak", ["run", "--command=sh", daw, "-c", command]);

            return ParseArgv(File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
        }
    }

    private static IReadOnlyList<string> ParseArgv(string logged)
    {
        var line = logged.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                   ?? throw new InvalidOperationException("the shim logged no argv");

        return
        [
            .. line.Trim().TrimStart('[').TrimEnd(']')
                .Split("\", \"")
                .Select(field => field.Trim().Trim('"'))
        ];
    }

    private static bool AppearsWithin(string app, string match) =>
        Settles(() => Host.Instances(app, match).Count > 0, TimeSpan.FromSeconds(90));

    private static bool GoesAwayWithin(string app, string match) =>
        Settles(() => Host.Instances(app, match).Count == 0, TimeSpan.FromSeconds(60));

    private static bool Settles(Func<bool> condition, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return condition();
    }

    private static string SocketDirectory() =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/run/user/1000", "yabridge");

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private const string Unlaunchable = "invalid.cabinet.teardown.probe";
}

public sealed class TeardownFactAttribute : FactAttribute
{
    public TeardownFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CABINET_RUN_TEARDOWN_TESTS") != "1")
        {
            Skip = "set CABINET_RUN_TEARDOWN_TESTS=1 to run runtime tests";
        }
    }
}
