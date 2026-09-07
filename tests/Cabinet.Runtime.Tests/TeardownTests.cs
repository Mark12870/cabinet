using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

public sealed class TeardownTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();
    private static readonly string HomeDirectory = RuntimeTestEnvironment.Home;

    [Fact]
    public void TheShimStartsAWineSessionThatOutlivesIt()
    {
        var argv = NativeArgv(Prefix());

        Assert.Equal(["flatpak", "run", "--command=/app/lib/yabridge/cabinet-wine"], argv.Take(3));
        Assert.DoesNotContain("--die-with-parent", argv);
        Assert.DoesNotContain("--watch-bus", argv);
    }

    [Fact]
    public void TheShimHopsThroughTheHostWhenTheDawIsItselfSandboxed()
    {
        var argv = SandboxedArgv(Daw);

        Assert.Equal(["flatpak-spawn", "--host"], argv.Take(2));
        Assert.Contains($"--env=FLATPAK_USER_DIR={RuntimeTestEnvironment.FlatpakUserDirectory}", argv);
        Assert.Contains("flatpak", argv);
        Assert.DoesNotContain("--watch-bus", argv);
    }

    [Fact]
    public void OnePrefixIsOneWineSession()
    {
        var prefix = Prefix();

        Assert.Equal(SessionName(NativeArgv(prefix)), SessionName(NativeArgv(prefix)));
        Assert.NotEqual(SessionName(NativeArgv(prefix)), SessionName(NativeArgv(Elsewhere())));
    }

    [Fact]
    public void TwoPluginsFromOnePrefixShareOneWineSandbox()
    {
        var session = Session("tp");
        const string first = "two-plugins-first";
        const string second = "two-plugins-second";

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

    [Fact]
    public void AJoinedApplicationCanBeTheFirstJobInAWineSession()
    {
        var session = Session("ja");
        const string application = "joined-application";
        const string plugin = "joined-plugin";

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

    [Fact]
    public void AWineSandboxDiesWhenTheShimDies()
    {
        var session = Session("sd");
        const string marker = "shim-dies";

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

    [Fact]
    public void AWineSandboxDiesWithTheSandboxedDawThatStartedIt()
    {
        var session = Session("dw");
        const string marker = "sandboxed-daw";

        try
        {
            using var outer = StartShimInside(Daw, session, marker);
            Assert.True(AppearsWithin(Host.App, session), "the wine sandbox never started");

            var running = Host.Instances(Daw, marker);
            Assert.True(running.Count > 0, $"no {Daw} instance was carrying this run");

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
            Host.KillAll(Daw, marker);
            Host.KillAll(Host.App, session);
            Discard(session);
        }
    }

    private static IReadOnlyList<string> Payload(string marker) => ["cmd", "/k", "rem", marker];

    private static string Session(string name) => Path.Combine(SocketDirectory(), name);

    private static string SessionName(IReadOnlyList<string> argv) =>
        Path.GetFileName(argv.SkipWhile(argument => argument != "--cabinet-inner").Skip(1).First());

    private static string Prefix() =>
        Path.Combine(HomeDirectory, ".var", "app", Host.App, "data", "prefixes", "carla-surge-windows");

    private static string Elsewhere() => Path.Combine(Prefix(), "..", "cabinet-not-a-prefix");

    private static Process StartShim(string session, string marker)
    {
        var info = new ProcessStartInfo(Host.Shim())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);

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

        Host.Configure(info);

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
            $"CABINET_RUNTIME_ROOT={Quote(RuntimeTestEnvironment.Root)} " +
            $"XDG_RUNTIME_DIR={Quote(RuntimeTestEnvironment.RuntimeDirectory)} " +
            $"WINEPREFIX={Quote(Prefix())} YABRIDGE_TEMP_DIR={Quote(session)} " +
            $"{Quote(Host.Shim())} {string.Join(' ', Payload(marker).Select(Quote))}";

        var info = new ProcessStartInfo("flatpak")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);

        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--command=sh");
        info.ArgumentList.Add("--nofilesystem=home");
        info.ArgumentList.Add($"--filesystem={RuntimeTestEnvironment.Root}:create");
        info.ArgumentList.Add($"--env=HOME={HomeDirectory}");
        info.ArgumentList.Add($"--env=XDG_RUNTIME_DIR={RuntimeTestEnvironment.RuntimeDirectory}");
        info.ArgumentList.Add($"--env=FLATPAK_USER_DIR={RuntimeTestEnvironment.FlatpakUserDirectory}");
        info.ArgumentList.Add(daw);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);

        return Process.Start(info) ?? throw new InvalidOperationException($"could not start {daw}");
    }

    private static IReadOnlyList<string> NativeArgv(string prefix)
    {
        var log = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "cabinet-argv.log");
        var session = Session("na");

        try
        {
            var info = new ProcessStartInfo(Host.Shim())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            Host.Configure(info);

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
        const string name = "cabinet-argv.log";
        var log = Path.Combine(HomeDirectory, ".var", "app", daw, "data", name);
        var session = Session("sa");

        try
        {
            var command =
                $"CABINET_SHIM_LOG=\"$XDG_DATA_HOME/{name}\" CABINET_APP={Unlaunchable} " +
                $"CABINET_RUNTIME_ROOT={Quote(RuntimeTestEnvironment.Root)} " +
                $"XDG_RUNTIME_DIR={Quote(RuntimeTestEnvironment.RuntimeDirectory)} " +
                $"WINEPREFIX={Quote(Prefix())} YABRIDGE_TEMP_DIR={Quote(session)} " +
                $"{Quote(Host.Shim())}";

            Host.Run(
                "flatpak",
                [
                    "run",
                    "--command=sh",
                    "--nofilesystem=home",
                    $"--filesystem={RuntimeTestEnvironment.Root}:create",
                    $"--env=HOME={HomeDirectory}",
                    $"--env=XDG_RUNTIME_DIR={RuntimeTestEnvironment.RuntimeDirectory}",
                    $"--env=FLATPAK_USER_DIR={RuntimeTestEnvironment.FlatpakUserDirectory}",
                    daw,
                    "-c",
                    command,
                ]);

            return ParseArgv(File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
            Discard(session);
        }
    }

    private static void Discard(string session)
    {
        try
        {
            Directory.Delete(session, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

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
        Path.Combine(RuntimeTestEnvironment.RuntimeDirectory, "yabridge");

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private const string Daw = "fm.reaper.Reaper";
    private const string Unlaunchable = "invalid.cabinet.teardown.probe";

    public void Dispose() => runtimeLock.Dispose();
}
