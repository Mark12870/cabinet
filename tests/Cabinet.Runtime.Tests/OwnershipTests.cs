using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

public sealed class OwnershipTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();

    private const string Name = "wp003-ownership";
    private const string Marker = "wp003-plugin";

    [Fact]
    public void ADawsPluginKeepsCabinetFromChangingItsPrefixAndSurvivesTheAttempt()
    {
        Make();
        using var plugin = StartPlugin();
        Assert.True(Started(), "the plugin's Wine session never started");

        var desktop = Cabinet(["set", Name, "desktop", "on"]);
        var moved = Cabinet(["use", Name, "bundled"]);
        var winetricks = Cabinet(["winetricks", Name, "corefonts"]);

        Assert.Equal(1, desktop.ExitCode);
        Assert.Contains($"A DAW is using plugins from {Name}", desktop.Error);
        Assert.Equal(1, moved.ExitCode);
        Assert.Contains($"A DAW is using plugins from {Name}", moved.Error);
        Assert.Equal(1, winetricks.ExitCode);
        Assert.Contains($"A DAW is using plugins from {Name}", winetricks.Error);
        Assert.False(plugin.HasExited, "the plugin lost its Wine while Cabinet was refused");
        Assert.True(Directory.Exists(Prefix()), "the prefix did not survive a refused change");
    }

    [Fact]
    public void OnceThePluginIsGoneTheSameChangeGoesThrough()
    {
        Make();

        using (var plugin = StartPlugin())
        {
            Assert.True(Started(), "the plugin's Wine session never started");
            Assert.Equal(1, Cabinet(["set", Name, "desktop", "on"]).ExitCode);
            plugin.Close();
        }

        Assert.True(
            Settles(() => !Live(), TimeSpan.FromSeconds(90)),
            "the plugin's Wine session never retired");

        var desktop = Cabinet(["set", Name, "desktop", "on"]);

        Assert.Equal(0, desktop.ExitCode);
        Assert.Contains("desktop of its own", desktop.Output);
    }

    [Fact]
    public void WineInAPrefixIsEndedFromInsideItsOwnSession()
    {
        Make();
        using var held = StartInCabinet(["cmd", "/k", "rem", Marker]);
        Assert.True(Started(), "Cabinet's own Wine session never started");
        var recorded = File.ReadAllText(SessionFile("record"));

        var ended = Cabinet(["run", Name, "wineboot", "-k"]);

        Assert.Equal(0, ended.ExitCode);
        Assert.True(
            held.WaitForExit(TimeSpan.FromSeconds(60)),
            "the job outlived the Wine that was ended inside its own session");
        Assert.Contains($"prefix {Prefix()}", recorded);
        Assert.Contains("runner ", recorded);
    }

    private static string Prefix() =>
        Path.Combine(
            RuntimeTestEnvironment.Home, ".var", "app", Host.App, "data", "prefixes", Name);

    private static bool Live() => File.Exists(SessionFile("socket"));

    private static bool Started() => Settles(Live, TimeSpan.FromSeconds(90));

    private static string SessionFile(string label)
    {
        var info = new ProcessStartInfo(Host.Shim())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);
        info.Environment["WINEPREFIX"] = Prefix();
        info.ArgumentList.Add("--cabinet-paths");

        using var asked = Process.Start(info)
                          ?? throw new InvalidOperationException("could not ask for the paths");
        var printed = asked.StandardOutput.ReadToEnd();
        asked.WaitForExit();

        return printed
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(label + " ", StringComparison.Ordinal))
            .Select(line => line[(label.Length + 1)..])
            .Single();
    }

    private static ProcessResult Cabinet(IReadOnlyList<string> arguments) =>
        Host.Run(
            "flatpak",
            [
                "run",
                "--command=cabinet",
                $"--filesystem={RuntimeTestEnvironment.Root}:create",
                $"--env=HOME={RuntimeTestEnvironment.Home}",
                $"--env=XDG_RUNTIME_DIR={RuntimeTestEnvironment.RuntimeDirectory}",
                Host.App,
                .. arguments,
            ]);

    private sealed class Job(Process client) : IDisposable
    {
        public bool HasExited => client.HasExited;

        public bool WaitForExit(TimeSpan within) => client.WaitForExit((int)within.TotalMilliseconds);

        public void Close()
        {
            if (!client.HasExited)
            {
                client.Kill(entireProcessTree: false);
            }

            client.WaitForExit();
        }

        public void Dispose()
        {
            Close();
            client.Dispose();
        }
    }

    private static Job StartPlugin()
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

        foreach (var argument in (string[])["cmd", "/k", "rem", Marker])
        {
            info.ArgumentList.Add(argument);
        }

        return new Job(
            Process.Start(info) ?? throw new InvalidOperationException("could not start the shim"));
    }

    private static Job StartInCabinet(IReadOnlyList<string> job)
    {
        var info = new ProcessStartInfo("flatpak")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);

        foreach (var argument in (string[])
                 [
                     "run",
                     "--command=/app/lib/yabridge/cabinet-wine",
                     $"--filesystem={RuntimeTestEnvironment.Root}:create",
                     $"--env=HOME={RuntimeTestEnvironment.Home}",
                     $"--env=XDG_RUNTIME_DIR={RuntimeTestEnvironment.RuntimeDirectory}",
                     $"--env=WINEPREFIX={Prefix()}",
                     Host.App,
                     "--cabinet-join",
                     .. job,
                 ])
        {
            info.ArgumentList.Add(argument);
        }

        return new Job(
            Process.Start(info) ?? throw new InvalidOperationException("could not start Cabinet"));
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

    private static void Make()
    {
        Host.KillAll(Host.App, Marker);
        Discard();
        var made = Cabinet(["new", Name]);

        Assert.True(made.ExitCode == 0, made.Error + made.Output);
    }

    private static void Discard()
    {
        foreach (var label in (string[])["socket", "record", "log"])
        {
            File.Delete(SessionFile(label));
        }

        if (Directory.Exists(Prefix()))
        {
            Directory.Delete(Prefix(), recursive: true);
        }
    }

    public void Dispose()
    {
        Host.KillAll(Host.App, Marker);
        Discard();
        runtimeLock.Dispose();
    }
}
