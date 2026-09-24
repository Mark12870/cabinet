using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

internal sealed record ProbeResult(int ExitCode, string Output, string Error, string Trace)
{
    public string Said =>
        $"{Output}\nstderr:\n{Error}\nexit: {ExitCode}\nyabridge:\n{Trace}";
}

internal static class EditorProbe
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    public static ProbeResult Run(
        string script,
        string name,
        string plugin,
        string format,
        params string[] extra)
    {
        var session = RuntimeTestEnvironment.SocketDirectory;
        var trace = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, $"{name}-editor.log");

        return RunIn(RuntimeTestEnvironment.Home, session, trace, script, plugin, format, extra);
    }

    public static ProbeResult RunIn(
        string home,
        string session,
        string trace,
        string script,
        string plugin,
        string format,
        params string[] extra)
    {
        try
        {
            using var display = Display.Start();
            return Drive(display, home, script, trace, plugin, format, extra, session);
        }
        finally
        {
            Host.KillAll(Host.App, session);
            Host.Discard(session);
        }
    }

    public static string CarlaPrefix() => Path.Combine(
        RuntimeTestEnvironment.Home, ".var", "app", Host.App, "data", "carla-tests", "prefix");

    public static string Artefacts(string name)
    {
        var directory = Path.Combine(
            RuntimeTestEnvironment.TemporaryDirectory, "interaction", name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ProbeResult Drive(
        Display display,
        string home,
        string script,
        string log,
        string plugin,
        string format,
        IReadOnlyList<string> extra,
        string session)
    {
        var yabridge = Path.Combine(Host.Location(), "files", "lib", "yabridge");
        File.Delete(log);
        Directory.CreateDirectory(session);

        var info = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        display.Configure(info);
        info.Environment["HOME"] = home;
        info.Environment["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share");
        info.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");
        info.Environment["XDG_CACHE_HOME"] = Path.Combine(home, ".cache");

        info.Environment["WINELOADER"] = Path.Combine(yabridge, "cabinet-wine");
        var wrapper = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "editor-bin");
        info.Environment["PATH"] = $"{Wrapper(wrapper, display, home, session)}:{yabridge}:/usr/bin:/bin";
        info.Environment["YABRIDGE_TEMP_DIR"] = session;
        info.Environment["YABRIDGE_NO_WATCHDOG"] = "1";
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Probes", script));
        info.ArgumentList.Add(CarlaPrefix());
        info.ArgumentList.Add(plugin);
        info.ArgumentList.Add(format);
        info.ArgumentList.Add(log);

        foreach (var argument in extra)
        {
            info.ArgumentList.Add(argument);
        }

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

        return new ProbeResult(
            process.ExitCode,
            output.Result,
            error.Result,
            File.Exists(log) ? File.ReadAllText(log) : "(no yabridge trace)");
    }

    public static string Wrapper(string directory, Display display, string home, string session)
    {
        Directory.CreateDirectory(directory);
        var wrapper = Path.Combine(directory, "flatpak");

        var installedApp = Path.Combine(
            RuntimeTestEnvironment.FlatpakUserDirectory, "app", Host.App);
        File.WriteAllText(wrapper, $$"""
            #!/usr/bin/env bash
            if [ "$1" = run ]; then
              shift
              exec /usr/bin/flatpak run --nofilesystem=home \
                --filesystem="{{home}}":create \
                --filesystem="{{installedApp}}":ro \
                --filesystem="{{session}}":create \
                --filesystem=/tmp/.X11-unix \
                --env=HOME="{{home}}" \
                --env=DISPLAY="{{display.Name}}" \
                --env=FLATPAK_USER_DIR="{{RuntimeTestEnvironment.FlatpakUserDirectory}}" "$@"
            fi
            exec /usr/bin/flatpak "$@"
            """);
        File.SetUnixFileMode(
            wrapper,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return directory;
    }
}
