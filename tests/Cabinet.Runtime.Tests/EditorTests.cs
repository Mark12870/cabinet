using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests;

public sealed class EditorTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData("vst2", ".vst", "ValhallaSupermassive_x64.so")]
    [InlineData("vst2", ".vst", "Sitala.so")]
    [InlineData("vst3", ".vst3", "SINE Player.vst3")]
    public void AnEditorDrawsWhereTheWindowHostingItIs(string format, string root, string plugin)
    {
        var seen = Measure(format, root, plugin);

        Assert.True(
            seen.Wrapper == seen.Wine,
            $"{plugin}'s editor is at {seen.Wine}, not at {seen.Wrapper} where the window hosting "
            + $"it is. It is drawn {seen.Wine.X - seen.Wrapper.X} across and "
            + $"{seen.Wine.Y - seen.Wrapper.Y} down from the frame the DAW shows, so that frame is "
            + "blank. See the yabridge source note in PATCHES.MD.");
    }

    [Theory]
    [InlineData("vst2", ".vst", "ValhallaSupermassive_x64.so")]
    [InlineData("vst2", ".vst", "Sitala.so")]
    [InlineData("vst3", ".vst3", "SINE Player.vst3")]
    public void AnEditorIsWhereWineHasBeenToldItIs(string format, string root, string plugin)
    {
        var seen = Measure(format, root, plugin);

        Assert.True(
            seen.Told == seen.Wine,
            $"Wine places {plugin}'s editor at {seen.Wine} but has been told it is at {seen.Told}. "
            + $"It turns a screen coordinate into a client one with what it was told, so every "
            + $"click lands {seen.Wine.X - seen.Told.X} across and {seen.Wine.Y - seen.Told.Y} "
            + "down from the pointer. See the yabridge source note in PATCHES.MD.");
    }

    private static Seen Measure(string format, string root, string plugin)
    {
        var session = RuntimeTestEnvironment.SocketDirectory;

        try
        {
            var said = RunProbe(format, Path.Combine(
                RuntimeTestEnvironment.Home, root, "cabinet", plugin), session);
            var seen = Regex.Match(
                said,
                @"WRAPPER=\((-?\d+), (-?\d+)\) WINE=\((-?\d+), (-?\d+)\) TOLD=\((-?\d+), (-?\d+)\)");

            Assert.True(seen.Success, $"the editor probe did not report a geometry: {said}");

            int At(int group) => int.Parse(seen.Groups[group].Value);

            return new Seen(
                (At(1), At(2)),
                (At(3), At(4)),
                (At(5), At(6)));
        }
        finally
        {
            Host.KillAll(Host.App, session);
            Host.Discard(session);
        }
    }

    private sealed record Seen(
        (int X, int Y) Wrapper, (int X, int Y) Wine, (int X, int Y) Told);

    public void Dispose() => runtimeLock.Dispose();

    private static string CarlaPrefix() => Path.Combine(
        RuntimeTestEnvironment.Home, ".var", "app", Host.App, "data", "carla-tests", "prefix");

    private static string Flatpak(string session)
    {
        var directory = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "editor-bin");
        Directory.CreateDirectory(directory);
        var wrapper = Path.Combine(directory, "flatpak");

        File.WriteAllText(wrapper, $"""
            #!/usr/bin/env bash
            if [ "$1" = run ]; then
              shift
              exec /usr/bin/flatpak run --nofilesystem=home \
                --filesystem={RuntimeTestEnvironment.Root}:create \
                --filesystem={session}:create \
                --env=HOME={RuntimeTestEnvironment.Home} \
                --env=FLATPAK_USER_DIR={RuntimeTestEnvironment.FlatpakUserDirectory} "$@"
            fi
            exec /usr/bin/flatpak "$@"
            """);
        File.SetUnixFileMode(
            wrapper,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return directory;
    }

    private static string RunProbe(string format, string plugin, string session)
    {
        var yabridge = Path.Combine(Host.Location(), "files", "lib", "yabridge");
        var log = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "editor-geometry.log");
        File.Delete(log);
        Directory.CreateDirectory(session);

        var info = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);

        info.Environment["DISPLAY"] = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
        info.Environment["WINELOADER"] = Path.Combine(yabridge, "cabinet-wine");
        info.Environment["PATH"] = $"{Flatpak(session)}:{yabridge}:/usr/bin:/bin";
        info.Environment["YABRIDGE_TEMP_DIR"] = session;
        info.Environment["YABRIDGE_NO_WATCHDOG"] = "1";
        info.ArgumentList.Add(
            Path.Combine(AppContext.BaseDirectory, "Probes", "editor-geometry.py"));
        info.ArgumentList.Add(CarlaPrefix());
        info.ArgumentList.Add(plugin);
        info.ArgumentList.Add(format);
        info.ArgumentList.Add(log);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("could not start the editor probe");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        Task.WaitAll([output, error], Patience);
        var trace = File.Exists(log) ? File.ReadAllText(log) : "(no yabridge trace)";
        return $"{output.Result}\nstderr:\n{error.Result}\nexit: {process.ExitCode}\nyabridge:\n{trace}";
    }
}
