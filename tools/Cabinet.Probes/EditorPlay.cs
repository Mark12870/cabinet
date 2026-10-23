using System.Diagnostics;
using System.Globalization;

namespace Cabinet.Probes;

internal sealed class EditorPlay
{
    private const double Listen = 3.0;

    private readonly Carla host;
    private readonly Desktop desktop = new();
    private readonly string shots;
    private readonly byte note;
    private readonly string title;
    private readonly IntPtr display = X11.XOpenDisplay(IntPtr.Zero);

    private EditorPlay(Carla host, string shots, byte note, string title)
    {
        this.host = host;
        this.shots = shots;
        this.note = note;
        this.title = title;
    }

    public static int Run(string prefix, string plugin, string format, string log, string[] rest)
    {
        var shots = rest[0];
        var note = byte.Parse(rest[1], CultureInfo.InvariantCulture);
        var steps = rest[2].Split(';').Where(step => step.Trim().Length > 0)
            .Select(step => step.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var title = rest[3];
        var restore = rest.Length > 4;
        var release = double.Parse(
            Environment.GetEnvironmentVariable("CABINET_PROBE_RELEASE") ?? "0", CultureInfo.InvariantCulture);

        Libc.Export("YABRIDGE_DEBUG_LEVEL", "1");
        Libc.Export("YABRIDGE_DEBUG_FILE", log);

        var host = Carla.Standalone(prefix);
        var probe = new EditorPlay(host, shots, note, title);
        host.Rack(prefix);

        if (!host.EngineInit("Dummy", "cabinet-editor-play"))
        {
            Console.WriteLine("ENGINE=failed " + host.LastError());
            return 1;
        }

        if (!host.AddPlugin(Carla.BinaryNative, Carla.Type(format), plugin, null, null))
        {
            Console.WriteLine("PLUGIN=failed " + host.LastError());
            return 1;
        }

        probe.Idle(5);
        host.ShowCustomUi(0, true);
        probe.Idle(15);

        probe.Capture("opened");

        foreach (var step in steps)
        {
            if (step[0] == "wait")
            {
                probe.Idle(double.Parse(step[1], CultureInfo.InvariantCulture));
                continue;
            }

            var origin = probe.Position();
            X11.XWarpPointer(
                probe.display, 0, X11.XDefaultRootWindow(probe.display), 0, 0, 0, 0,
                origin.X + int.Parse(step[1], CultureInfo.InvariantCulture),
                origin.Y + int.Parse(step[2], CultureInfo.InvariantCulture));
            X11.XSync(probe.display, 0);
            probe.Idle(0.5);
            probe.desktop.Run("xdotool", "click", "--repeat", step[0] == "double" ? "2" : "1", "--delay", "120", "1");
            probe.Idle(2);
        }

        probe.Capture("loaded");
        probe.Measure("PLAYED", release);

        if (restore)
        {
            var state = Path.Combine(shots, "state.carxs");
            host.SetOption(0, Carla.OptionUseChunks, true);
            host.SavePluginState(0, state);
            host.RemoveAllPlugins();
            probe.Idle(5);

            if (!host.AddPlugin(Carla.BinaryNative, Carla.Type(format), plugin, null, null))
            {
                Console.WriteLine("RESTORED=failed " + host.LastError());
                return 1;
            }

            host.SetOption(0, Carla.OptionUseChunks, true);

            if (!host.LoadPluginState(0, state))
            {
                Console.WriteLine("RESTORED=failed " + host.LastError());
                return 1;
            }

            probe.Idle(20);
            host.ShowCustomUi(0, true);
            probe.Idle(10);
            probe.Capture("restored");
            probe.Measure("RESTORED", release);
        }

        host.RemoveAllPlugins();
        host.EngineClose();
        return 0;
    }

    private void Measure(string phase, double release)
    {
        var before = Hear(held: false);
        var held = Hear(held: true);
        Idle(release);
        var after = Hear(held: false);
        host.ShowCustomUi(0, false);
        Idle(2);
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{phase} BEFORE={before:F6} HELD={held:F6} AFTER={after:F6}"));
    }

    private void Idle(double seconds)
    {
        var end = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < end)
        {
            host.EngineIdle();
            Thread.Sleep(50);
        }
    }

    private string Window()
    {
        foreach (var candidate in desktop.Run("xdotool", "search", "--onlyvisible", "--name", title).Output
                     .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (desktop.Managed(candidate))
            {
                return candidate;
            }
        }

        return "";
    }

    private (int X, int Y) Position()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (desktop.Position(Window()) is { } found)
            {
                return found;
            }

            Idle(0.5);
        }

        throw new InvalidOperationException("the editor window could not be found to click in");
    }

    private void Capture(string name)
    {
        var raw = Path.Combine(shots, name + ".xwd");
        var window = Window();

        if (desktop.Capture(window, shots, name) is null)
        {
            var target = window.Length == 0 ? "the visible editor window" : $"editor window {window}";
            throw new InvalidOperationException(
                $"could not capture {target} as {Path.Combine(shots, name + ".png")}; "
                + $"inspect {raw} if present and check xwd and ImageMagick XWD/PNG support");
        }
    }

    private double Hear(bool held)
    {
        if (held)
        {
            host.SendMidiNote(0, 0, note, 110);
        }

        var peak = 0.0;
        var end = Stopwatch.GetTimestamp() + (long)(Listen * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < end)
        {
            host.EngineIdle();
            peak = Math.Max(peak, Math.Max(host.OutputPeak(0, true), host.OutputPeak(0, false)));
            Thread.Sleep(20);
        }

        if (held)
        {
            host.SendMidiNote(0, 0, note, 0);
        }

        return peak;
    }
}
