using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

public sealed class TeardownTests
{
    private static readonly string HomeDirectory =
        Environment.GetEnvironmentVariable("HOME")
        ?? throw new InvalidOperationException("HOME is not set");

    [TeardownFact]
    public void TheShimStartsAWineSessionThatOutlivesIt()
    {
        var argv = NativeArgv(Prefix());

        Assert.Equal(["flatpak", "run", "--command=/app/lib/yabridge/cabinet-wine"], argv.Take(3));
        Assert.DoesNotContain("--die-with-parent", argv);
        Assert.DoesNotContain("--watch-bus", argv);
    }

    [TeardownFact]
    public void TheShimHopsThroughTheHostWhenTheDawIsItselfSandboxed()
    {
        var argv = SandboxedArgv(RequireDaw());

        Assert.Equal(["flatpak-spawn", "--host", "flatpak"], argv.Take(3));
        Assert.DoesNotContain("--watch-bus", argv);
    }

    [TeardownFact]
    public void OnePrefixIsOneWineSession()
    {
        var prefix = Prefix();

        Assert.Equal(SessionName(NativeArgv(prefix)), SessionName(NativeArgv(prefix)));
        Assert.NotEqual(SessionName(NativeArgv(prefix)), SessionName(NativeArgv(Elsewhere())));
    }

    [TeardownFact]
    public void TwoPluginsFromOnePrefixShareOneWineSandbox()
    {
        var session = Session();
        var first = Marker();
        var second = Marker();

        try
        {
            using var one = StartShim(session, first);
            Assert.True(AppearsWithin(Host.App, session), "the wine sandbox never started");

            using var two = StartShim(session, second);

            Assert.True(
                StaysSingle(Host.App, session),
                "a second plugin from the same prefix started a second wine sandbox");
            Assert.False(one.HasExited, "the first plugin lost its wine session");
            Assert.False(two.HasExited, "the second plugin never joined the wine session");
        }
        finally
        {
            Host.KillAll(Host.App, session);
            Discard(session);
        }
    }

    [TeardownFact]
    public void AJoinedApplicationCanBeTheFirstJobInAWineSession()
    {
        var session = Session();
        var application = Marker();
        var plugin = Marker();

        try
        {
            using var joined = StartJoinedShim(session, application);
            Assert.True(AppearsWithin(Host.App, session), "the joined application never started");

            using var second = StartShim(session, plugin);

            Assert.True(
                StaysSingle(Host.App, session),
                "the plugin started a second wine sandbox after the joined application");
            Assert.False(joined.HasExited, "the joined application lost its wine session");
            Assert.False(second.HasExited, "the plugin never joined the wine session");
        }
        finally
        {
            Host.KillAll(Host.App, session);
            Discard(session);
        }
    }

    [TeardownFact]
    public void AWineSandboxDiesWhenTheShimDies()
    {
        var session = Session();
        var marker = Marker();

        try
        {
            using var shim = StartShim(session, marker);
            Assert.True(AppearsWithin(Host.App, session), "the wine sandbox never started");

            shim.Kill(entireProcessTree: false);
            shim.WaitForExit();

            Assert.True(
                GoesAwayWithin(Host.App, session),
                "the wine sandbox outlived the process that started it");
        }
        finally
        {
            Host.KillAll(Host.App, session);
            Discard(session);
        }
    }

    [TeardownFact]
    public void AWineSandboxDiesWithTheSandboxedDawThatStartedIt()
    {
        var daw = RequireDaw();
        var session = Session();
        var marker = Marker();

        try
        {
            using var outer = StartShimInside(daw, session, marker);
            Assert.True(AppearsWithin(Host.App, session), "the wine sandbox never started");

            var running = Host.Instances(daw, marker);
            Assert.True(running.Count > 0, $"no {daw} instance was carrying this run");

            foreach (var instance in running)
            {
                Host.Kill(instance);
            }

            Assert.True(
                GoesAwayWithin(Host.App, session),
                "the wine sandbox outlived the sandboxed DAW that started it");

            outer.Kill(entireProcessTree: true);
        }
        finally
        {
            Host.KillAll(daw, marker);
            Host.KillAll(Host.App, session);
            Discard(session);
        }
    }

    private static string Marker() => Random.Shared.Next(100000, 999999).ToString();

    private static IReadOnlyList<string> Payload(string marker) => ["cmd", "/k", "rem", marker];

    private static string Session() =>
        Path.Combine(SocketDirectory(), $"teardown-{Guid.NewGuid():N}");

    private static string SessionName(IReadOnlyList<string> argv) =>
        Path.GetFileName(argv.SkipWhile(argument => argument != "--cabinet-inner").Skip(1).First());

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

    private static string Elsewhere() => Path.Combine(Prefix(), "..", "cabinet-not-a-prefix");

    private static string RequireDaw()
    {
        var daw = Environment.GetEnvironmentVariable("CABINET_TEARDOWN_DAW") ?? "fm.reaper.Reaper";

        return Host.Installed(daw)
            ? daw
            : throw new InvalidOperationException(
                $"{daw} is not installed; set CABINET_TEARDOWN_DAW to a sandboxed DAW that is");
    }

    private static Process StartShim(string session, string marker)
    {
        var info = new ProcessStartInfo(Host.Shim())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.Environment["WINEPREFIX"] = Prefix();
        info.Environment["YABRIDGE_TEMP_DIR"] = session;

        foreach (var argument in Payload(marker))
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("could not start the shim");
    }

    private static Process StartJoinedShim(string session, string marker)
    {
        var info = new ProcessStartInfo(Host.Shim())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.Environment["WINEPREFIX"] = Prefix();
        info.Environment["YABRIDGE_TEMP_DIR"] = session;
        info.ArgumentList.Add("--cabinet-join");

        foreach (var argument in Payload(marker))
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("could not start the joined shim");
    }

    private static Process StartShimInside(string daw, string session, string marker)
    {
        var command =
            $"WINEPREFIX={Quote(Prefix())} YABRIDGE_TEMP_DIR={Quote(session)} " +
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

    private static IReadOnlyList<string> NativeArgv(string prefix)
    {
        var log = Path.Combine(Path.GetTempPath(), $"cabinet-argv-{Guid.NewGuid():N}.log");
        var session = Session();

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
            info.Environment["WINEPREFIX"] = prefix;
            info.Environment["YABRIDGE_TEMP_DIR"] = session;

            using var process = Process.Start(info)
                                ?? throw new InvalidOperationException("could not start the shim");
            process.WaitForExit();

            return ParseArgv(File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
            Discard(session);
        }
    }

    private static IReadOnlyList<string> SandboxedArgv(string daw)
    {
        var name = $"cabinet-argv-{Guid.NewGuid():N}.log";
        var log = Path.Combine(HomeDirectory, ".var", "app", daw, "data", name);
        var session = Session();

        try
        {
            var command =
                $"CABINET_SHIM_LOG=\"$XDG_DATA_HOME/{name}\" CABINET_APP={Unlaunchable} " +
                $"WINEPREFIX={Quote(Prefix())} YABRIDGE_TEMP_DIR={Quote(session)} " +
                $"{Quote(Host.Shim())}";

            Host.Run("flatpak", ["run", "--command=sh", daw, "-c", command]);

            return ParseArgv(File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
            Discard(session);
        }
    }

    private static void Discard(string session) =>
        Directory.Delete(session, recursive: true);

    private static IReadOnlyList<string> ParseArgv(string logged)
    {
        var line = logged.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
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
        Settles(() => Host.Instances(app, match).Count == 0, TimeSpan.FromSeconds(90));

    private static bool StaysSingle(string app, string match) =>
        Holds(() => Host.Instances(app, match).Count == 1, TimeSpan.FromSeconds(30));

    private static bool Holds(Func<bool> condition, TimeSpan across)
    {
        var deadline = DateTime.UtcNow + across;

        while (DateTime.UtcNow < deadline)
        {
            if (!condition())
            {
                return false;
            }

            Thread.Sleep(500);
        }

        return condition();
    }

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
