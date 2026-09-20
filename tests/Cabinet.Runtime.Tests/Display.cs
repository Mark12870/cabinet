using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests;

internal sealed class Display : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    private readonly Process? compositor;
    private readonly Process? anchor;

    private Display(string name, Process? compositor, Process? anchor)
    {
        Name = name;
        this.compositor = compositor;
        this.anchor = anchor;
    }

    public string Name { get; }

    public static Display Session() =>
        new(Environment.GetEnvironmentVariable("DISPLAY") ?? ":0", null, null);

    public static Display Start()
    {
        var runtime = RuntimeTestEnvironment.RuntimeDirectory;
        Directory.CreateDirectory(runtime);
        File.SetUnixFileMode(
            runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var socket = $"cabinet-wl-{Environment.ProcessId}-{Interlocked.Increment(ref taken)}";
        var log = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, $"{socket}.log");
        File.Delete(log);

        var info = new ProcessStartInfo("weston")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);
        info.Environment.Remove("DISPLAY");
        info.Environment.Remove("WAYLAND_DISPLAY");

        foreach (var argument in new[]
                 {
                     "--backend=headless",
                     "--width=1920",
                     "--height=1080",
                     "--refresh-rate=60000",
                     "--xwayland",
                     $"--socket={socket}",
                     $"--log={log}",
                 })
        {
            info.ArgumentList.Add(argument);
        }

        var compositor = Process.Start(info)
            ?? throw new InvalidOperationException("could not start weston");

        var name = Announced(compositor, log);
        return new Display(name, compositor, Anchor(name));
    }

    public void Configure(ProcessStartInfo info)
    {
        Host.Configure(info);
        info.Environment["DISPLAY"] = Name;
        info.Environment.Remove("WAYLAND_DISPLAY");
    }

    public void Dispose()
    {
        End(anchor);

        if (compositor is null)
        {
            return;
        }

        End(compositor);
        Released();
    }

    private static Process Anchor(string name)
    {
        var info = new ProcessStartInfo("xprop")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);
        info.Environment["DISPLAY"] = name;
        info.ArgumentList.Add("-root");
        info.ArgumentList.Add("-spy");

        return Process.Start(info)
            ?? throw new InvalidOperationException("could not hold the display open");
    }

    private static void End(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                Terminate(process.Id);

                if (!process.WaitForExit(Grace))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(Grace);
                }
            }
        }
        catch (SystemException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void Terminate(int pid)
    {
        var info = new ProcessStartInfo("kill")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.ArgumentList.Add("-TERM");
        info.ArgumentList.Add(pid.ToString());

        using var sent = Process.Start(info);
        sent?.WaitForExit(Grace);
    }

    private static string Listening(string name)
    {
        var socket = Path.Combine("/tmp/.X11-unix", "X" + name.TrimStart(':'));
        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline && !File.Exists(socket))
        {
            Thread.Sleep(100);
        }

        return name;
    }

    private void Released()
    {
        var socket = Path.Combine("/tmp/.X11-unix", "X" + Name.TrimStart(':'));
        var deadline = DateTime.UtcNow + Grace;

        while (DateTime.UtcNow < deadline && File.Exists(socket))
        {
            Thread.Sleep(100);
        }
    }

    private static string Announced(Process compositor, string log)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            var found = Regex.Match(Read(log), @"xserver listening on display (:\d+)");

            if (found.Success)
            {
                return Listening(found.Groups[1].Value);
            }

            Thread.Sleep(250);
        }

        var said = Read(log);
        compositor.Kill(entireProcessTree: true);
        compositor.WaitForExit(Patience);
        compositor.Dispose();

        throw new InvalidOperationException(
            $"weston did not announce an X display within {Patience.TotalSeconds:F0}s: {said}");
    }

    private static string Read(string log)
    {
        try
        {
            using var handle = new FileStream(
                log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(handle);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static int taken;
}
