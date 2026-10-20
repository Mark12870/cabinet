using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Cabinet.Probes;

internal sealed unsafe partial class EditorInteraction
{
    private const int Settle = 20;
    private const int Draw = 20;
    private const double Enough = 0.002;
    private const int Columns = 5;
    private const int Rows = 4;
    private const double Inset = 0.15;
    private const double StillLimit = 60;
    private const double Changed = 0.02;
    private const int Hung = 15;
    private const int Locate = 24;
    private const int Blobs = 4;
    private const int Speck = 50;

    private static readonly double[] Spread = [0.25, 0.5, 0.75];
    private static readonly int[] Swing = [10, 20, 30, 40];

    private readonly long started = Stopwatch.GetTimestamp();
    private readonly Carla host;
    private readonly Desktop desktop;
    private readonly string shots;
    private readonly string log;
    private double worst;
    private int blind;
    private bool closed;

    private EditorInteraction(Carla host, string shots, string log)
    {
        this.host = host;
        this.shots = shots;
        this.log = log;
        desktop = new Desktop(TimeSpan.FromSeconds(10), Note);
    }

    public static int Run(string prefix, string plugin, string format, string log, string[] rest)
    {
        var shots = rest[0];
        var click = rest.Length > 1 ? Words(rest[1]) : [];
        var aiming = rest.Length <= 2 || rest[2] != "no-aim";
        var control = rest.Length > 3
            ? Words(rest[3]).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray()
            : [];
        var press = rest.Length > 4 ? Words(rest[4]) : [];
        var typing = (Libc.Take("CABINET_PROBE_TYPE") ?? "").Split('\n')
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t', 2))
            .ToList();
        var still = double.Parse(Libc.Take("CABINET_PROBE_STILL") ?? "0", CultureInfo.InvariantCulture);

        Libc.Export("YABRIDGE_DEBUG_LEVEL", "1+editor");
        Libc.Export("YABRIDGE_DEBUG_FILE", log);

        Directory.CreateDirectory(shots);
        var host = Carla.Standalone(prefix);
        host.Rack(prefix);

        if (!host.EngineInit("Dummy", "cabinet-editor-interaction"))
        {
            Console.WriteLine("ENGINE=failed " + host.LastError());
            return 1;
        }

        return new EditorInteraction(host, shots, log).Probe(plugin, format, click, aiming, control, press, typing, still);
    }

    private int Probe(
        string plugin, string format, string[] click, bool aiming, int[] control, string[] press,
        List<string[]> typing, double still)
    {
        string window;
        (int Left, int Top, int Width, int Height) found;
        string baseline;
        double reaction, noise, opening, closing, removing, shutdown;
        string? again;
        string aimed;
        string wine, told;

        try
        {
            var byUri = format == "lv2";

            if (!host.AddPlugin(Carla.BinaryNative, Carla.Type(format), byUri ? null : plugin, null, byUri ? plugin : null))
            {
                Console.WriteLine("PLUGIN=failed " + host.LastError());
                return 1;
            }

            Note("loaded");
            Turn(Settle);
            var before = desktop.Visible();
            opening = Timed(() => host.ShowCustomUi(0, true));
            var opened = EditorWindow(before);
            Note($"editor {opened ?? "None"}");

            if (opened is null)
            {
                Console.WriteLine("EDITOR=none");
                return 1;
            }

            window = opened;
            found = desktop.Geometry(window) ?? throw new InvalidOperationException($"{window} has no geometry");
            Note($"geometry ({found.Left}, {found.Top}, {found.Width}, {found.Height})");
            desktop.Run("xdotool", "mousemove", "0", "0");
            Turn(10);

            if (Capture(window, "baseline") is not { } captured)
            {
                Console.WriteLine($"EDITOR={window} CAPTURE=failed");
                return 1;
            }

            baseline = captured;
            desktop.Run("xdotool", "windowactivate", window);
            desktop.Run("xdotool", "windowraise", window);
            Turn(30);

            var typed = 0.0;

            foreach (var line in typing)
            {
                var at = Words(line[0]);
                var (x, y) = (at[0], at[1]);
                var untyped = Look(window, $"type-{x}-{y}-still");
                desktop.Run("xdotool", "mousemove", "--window", window, x, y, "click", "1");
                Turn(10);
                desktop.Feed(line[1], "xdotool", "type", "--file", "-");
                Turn(10);
                var shown = Look(window, $"type-{x}-{y}");
                var change = untyped is not null && shown is not null ? desktop.Difference(untyped, shown) : 0.0;

                foreach (var frame in new[] { untyped, shown })
                {
                    if (frame is not null)
                    {
                        File.Delete(frame);
                    }
                }

                Note($"typed at {x} {y} answered {Fixed(change)}");
                typed = Math.Max(typed, change);
            }

            foreach (var (x, y) in Pairs(click))
            {
                var unpressed = Capture(window, "unpressed");
                desktop.Run("xdotool", "mousemove", "--window", window, x, y);
                Turn(4);
                desktop.Run("xdotool", "click", "1");
                desktop.Run("xdotool", "mousemove", "0", "0");
                Note(string.Create(CultureInfo.InvariantCulture, $"clicked {x} {y}, answered after {Answered(window, unpressed):F1}s"));
            }

            if (still != 0)
            {
                Note(string.Create(CultureInfo.InvariantCulture, $"still after {Quiet(window, still):F1}s"));
            }

            var resting = Capture(window, "resting");
            Turn(30);
            again = Capture(window, "resting-again");
            noise = resting is not null && again is not null ? desktop.Difference(resting, again) : 0.0;
            Note($"noise {Fixed(noise)}");

            (wine, told) = Origin();
            Note($"wine {wine} told {told}");
            Note("sweeping");
            (aimed, reaction) = aiming ? Aim(window, control) : ("none", 0.0);
            Note($"aimed {aimed}");
            reaction = Math.Max(Math.Max(reaction, Press(window, press)), typed);

            if (aimed == "none" || reaction < Enough + noise)
            {
                reaction = Math.Max(reaction, Sweep(window, again ?? resting ?? baseline, noise));
            }

            Note($"reaction {Fixed(reaction)}");

            desktop.Run("xdotool", "mousemove", "0", "0");
            Turn(30);
            var settled = Capture(window, "settled");
            Turn(30);
            var rested = Capture(window, "settled-again");
            var drift = settled is not null && rested is not null ? desktop.Difference(settled, rested) : 0.0;
            noise = Math.Max(noise, drift);
            Note($"drift {Fixed(drift)}");

            using (new Timer(_ => Windows(), null, TimeSpan.FromSeconds(Hung), Timeout.InfiniteTimeSpan))
            {
                closing = Timed(() => host.ShowCustomUi(0, false));
            }

            Note("closed");
            Turn(Settle);

            var gone = desktop.Visible();
            Timed(() => host.ShowCustomUi(0, true));
            again = EditorWindow(gone);
            Note($"reopened {again ?? "None"}");
            Timed(() => host.ShowCustomUi(0, false));
            Turn(Settle);

            var removal = Stopwatch.GetTimestamp();
            var removed = host.RemovePlugin(0);
            removing = Stopwatch.GetElapsedTime(removal).TotalSeconds;

            if (!removed)
            {
                Console.WriteLine("REMOVE=failed " + host.LastError());
                return 1;
            }

            Note("removed");
        }
        finally
        {
            var closingEngine = Stopwatch.GetTimestamp();
            host.EngineClose();
            shutdown = Stopwatch.GetElapsedTime(closingEngine).TotalSeconds;
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"EDITOR={window} SIZE={found.Width}x{found.Height} COLOURS={desktop.Colours(baseline)} "
            + $"REACTION={reaction:F6} NOISE={noise:F6} IDLE_MS={worst * 1000:F0} "
            + $"OPEN_MS={opening * 1000:F0} CLOSE_MS={closing * 1000:F0} "
            + $"REMOVE_MS={removing * 1000:F0} SHUTDOWN_MS={shutdown * 1000:F0} "
            + $"BLIND={blind} CLOSED={(closed ? "yes" : "no")} "
            + $"REOPEN={(again is not null ? "yes" : "no")} AIM={aimed} WINE={wine} TOLD={told}");

        File.WriteAllText(Path.Combine(shots, "result.txt"), summary + "\n");
        Console.WriteLine(summary);
        return 0;
    }

    private void Note(string phase) =>
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"[{Stopwatch.GetElapsedTime(started).TotalSeconds,7:F1}s] {phase}"));

    private static string Fixed(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<(string X, string Y)> Pairs(string[] values) =>
        Enumerable.Range(0, values.Length / 2).Select(index => (values[2 * index], values[2 * index + 1]));

    private void Turn(int times = 1)
    {
        for (var turn = 0; turn < times; turn++)
        {
            var idled = Stopwatch.GetTimestamp();
            host.EngineIdle();
            worst = Math.Max(worst, Stopwatch.GetElapsedTime(idled).TotalSeconds);
            Thread.Sleep(50);
        }
    }

    private static double Timed(Action call)
    {
        var begun = Stopwatch.GetTimestamp();
        call();
        return Stopwatch.GetElapsedTime(begun).TotalSeconds;
    }

    private string? Capture(string window, string name) => desktop.Capture(window, shots, name);

    private bool Alive(string window) => desktop.Visible().Contains(window) && desktop.Area(window) > 1;

    private string? Look(string window, string name)
    {
        var shot = Capture(window, name);

        if (shot is null)
        {
            if (!Alive(window))
            {
                closed = true;
                Note($"{window} is gone at {name}: the editor answered by closing");
                return null;
            }

            blind++;
            Note($"blind at {name}: {window} is still mapped but gave no capture");
        }

        return shot;
    }

    private string? EditorWindow(HashSet<string> before)
    {
        for (var attempt = 0; attempt < Draw; attempt++)
        {
            Turn(10);
            var appeared = desktop.Visible().Except(before).ToList();
            var clients = appeared.Where(desktop.Managed).ToList();
            var ranked = (clients.Count > 0 ? clients : appeared)
                .Select(window => (Window: window, Area: desktop.Area(window)))
                .OrderByDescending(candidate => candidate.Area)
                .ToList();

            if (ranked.Count > 0 && desktop.Area(ranked[0].Window) > 1)
            {
                Turn(20);
                return ranked[0].Window;
            }
        }

        return null;
    }

    private IEnumerable<(int X, int Y, string Name)> Points(string window)
    {
        var (left, top, width, height) = desktop.Geometry(window)!.Value;

        for (var column = 0; column < Columns; column++)
        {
            for (var row = 0; row < Rows; row++)
            {
                yield return (
                    left + (int)(width * (Inset + (1 - 2 * Inset) * column / (Columns - 1))),
                    top + (int)(height * (Inset + (1 - 2 * Inset) * row / (Rows - 1))),
                    $"{column}-{row}");
            }
        }
    }

    private void Release() => desktop.Run("xdotool", "mouseup", "1");

    private (double Seen, string Before) Drag(string window, string before, int x, int y, string name)
    {
        desktop.Run("xdotool", "mousemove", Text(x), Text(y), "mousedown", "1");
        Turn(4);
        var seen = 0.0;

        for (var step = 1; step < 5; step++)
        {
            desktop.Run("xdotool", "mousemove", Text(x), Text(y - step * 10));
            Turn(3);
            var shot = Look(window, $"drag-{name}-{step}");

            if (closed)
            {
                break;
            }

            if (shot is not null)
            {
                seen = Math.Max(seen, desktop.Difference(before, shot));
                before = shot;
            }
        }

        desktop.Run("xdotool", "mouseup", "1");
        Turn(6);
        return (seen, before);
    }

    private List<(int X, int Y)> Regions(string before, string after, string still, string again)
    {
        var said = desktop.Run(
            "magick",
            "(", before, after, "-compose", "difference", "-composite",
            "-colorspace", "Gray", "-threshold", Desktop.Fuzz, ")",
            "(", still, again, "-compose", "difference", "-composite",
            "-colorspace", "Gray", "-threshold", Desktop.Fuzz, "-negate", ")",
            "-compose", "multiply", "-composite",
            "-define", "connected-components:verbose=true",
            "-define", $"connected-components:area-threshold={Speck}",
            "-connected-components", "8", "null:").Output;

        return [.. Component().Matches(said)
            .Select(found => (
                Size: int.Parse(found.Groups[3].Value, CultureInfo.InvariantCulture),
                X: double.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture),
                Y: double.Parse(found.Groups[2].Value, CultureInfo.InvariantCulture)))
            .OrderByDescending(blob => blob.Size)
            .ThenByDescending(blob => blob.X)
            .ThenByDescending(blob => blob.Y)
            .Take(Blobs)
            .Select(blob => ((int)Math.Round(blob.X, MidpointRounding.ToEven), (int)Math.Round(blob.Y, MidpointRounding.ToEven)))];
    }

    private IEnumerable<uint> Controls()
    {
        var count = host.ParameterCount(0);

        for (uint index = 0; index < count; index++)
        {
            if (Writable(index))
            {
                yield return index;
            }
        }
    }

    private bool Writable(uint index)
    {
        var data = host.ParameterData(0, index);
        return data->Type == Carla.ParameterInput && (data->Hints & Carla.ParameterIsEnabled) != 0
               && (data->Hints & Carla.ParameterIsReadOnly) == 0;
    }

    private ParameterRanges Ranges(uint index) => *host.ParameterRanges(0, index);

    private IEnumerable<(int X, int Y, string Name, string? Still, uint Index)> Located(string window)
    {
        var (left, top, _, _) = desktop.Geometry(window)!.Value;

        foreach (var index in Controls().Take(Locate))
        {
            var ranges = Ranges(index);
            var current = host.CurrentParameterValue(0, index);
            var far = current - ranges.Min > ranges.Max - current ? ranges.Min : ranges.Max;
            host.SetParameterValue(0, index, far);
            Turn(10);
            var moved = Look(window, $"locate-{index}");
            host.SetParameterValue(0, index, current);
            Turn(10);
            var restored = Look(window, $"locate-{index}-restored");
            Turn(10);
            var still = Look(window, $"locate-{index}-still");

            if (closed)
            {
                yield break;
            }

            var found = moved is not null && restored is not null && still is not null
                ? Regions(restored, moved, restored, still)
                : [];
            Note($"parameter {index} drew [{string.Join(", ", found.Select(blob => $"({blob.X}, {blob.Y})"))}]");

            foreach (var (blob, (x, y)) in found.Index())
            {
                yield return (left + x, top + y, $"parameter-{index}-{blob}", still, index);
            }
        }
    }

    private double Sweep(string window, string before, double noise)
    {
        var reaction = 0.0;
        var ranked = new List<(double Seen, int X, int Y, string Name)>();
        var enough = Enough + noise;

        foreach (var (x, y, name) in Points(window))
        {
            desktop.Run("xdotool", "mousemove", Text(x), Text(y));
            Turn(4);

            if (ranked.Count == 0)
            {
                var under = desktop.Run("xdotool", "getmouselocation", "--shell").Output;
                Note("pointer over " + Spaces().Replace(under, " ").Trim());

                if (Pointer().Match(under) is { Success: true } over && over.Groups[1].Value != window)
                {
                    var said = desktop.Run("xdotool", "getwindowgeometry", over.Groups[1].Value).Output
                               + desktop.Run("xprop", "-id", over.Groups[1].Value, "WM_NAME", "WM_CLASS",
                                   "_NET_WM_WINDOW_TYPE", "WM_TRANSIENT_FOR", "_NET_WM_STATE").Output;
                    Note("covered by " + Spaces().Replace(said, " ").Trim());
                    Capture(over.Groups[1].Value, "covering-window");
                }
            }

            var shot = Look(window, $"hover-{name}");

            if (closed)
            {
                return reaction;
            }

            var seen = shot is not null ? desktop.Difference(before, shot) : 0.0;
            before = shot ?? before;
            reaction = Math.Max(reaction, seen);
            ranked.Add((seen, x, y, name));
            Note($"hover {name} at {x},{y} moved {Fixed(seen)}");

            if (reaction >= enough)
            {
                Note($"hover {name} answered {Fixed(reaction)}");
                return reaction;
            }
        }

        var best = ranked
            .OrderByDescending(point => point.Seen)
            .ThenByDescending(point => point.X)
            .ThenByDescending(point => point.Y)
            .ThenByDescending(point => point.Name, StringComparer.Ordinal)
            .First();
        var (left, top, width, height) = desktop.Geometry(window)!.Value;
        var targets = new List<(int X, int Y, string Name)> { (best.X, best.Y, best.Name) };
        targets.AddRange(Spread.Select((across, index) =>
            (left + (int)(width * across), top + (int)(height * 0.5), $"spread-{index}")));

        foreach (var (x, y, name) in targets)
        {
            Release();
            Note($"click {name} at {x},{y}");
            desktop.Run("xdotool", "mousemove", Text(x), Text(y), "click", "1");
            Turn(10);
            var shot = Look(window, $"click-{name}");

            if (closed)
            {
                return reaction;
            }

            if (shot is not null)
            {
                reaction = Math.Max(reaction, desktop.Difference(before, shot));
                before = shot;
            }

            if (reaction >= enough)
            {
                Note($"click {name} answered {Fixed(reaction)}");
                return reaction;
            }

            (var dragged, before) = Drag(window, before, x, y, name);
            reaction = Math.Max(reaction, dragged);

            if (closed)
            {
                return reaction;
            }

            Note($"drag {name} {Fixed(reaction)}");
            Release();
            Turn(4);

            if (reaction >= enough)
            {
                return reaction;
            }
        }

        return reaction;
    }

    private bool Moved(uint index, float original) =>
        Math.Abs((double)host.CurrentParameterValue(0, index) - original) > 1e-6;

    private (bool Hit, string? Shot) Pressed(string window, uint index, int x, int y, string name)
    {
        var original = host.CurrentParameterValue(0, index);
        Release();
        desktop.Run("xdotool", "mousemove", Text(x), Text(y));
        Turn(4);
        desktop.Run("xdotool", "mousedown", "1");
        Turn(4);
        var hit = false;

        foreach (var (across, down) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
        {
            foreach (var swing in Swing)
            {
                desktop.Run("xdotool", "mousemove", Text(x + across * swing), Text(y + down * swing));
                Turn(3);
                hit = hit || Moved(index, original);
            }

            if (hit)
            {
                break;
            }
        }

        desktop.Run("xdotool", "mouseup", "1");
        Turn(4);
        hit = hit || Moved(index, original);
        var shot = Look(window, $"aim-{name}");
        host.SetParameterValue(0, index, original);
        Turn(6);
        return (hit, shot);
    }

    private IEnumerable<(int X, int Y, string Name, string? Still, uint Index)> Named(string window, int[] control)
    {
        var (left, top, _, _) = desktop.Geometry(window)!.Value;

        for (var at = 0; at + 2 < control.Length; at += 3)
        {
            var still = Look(window, $"control-{control[at]}-still");
            yield return (left + control[at + 1], top + control[at + 2], $"control-{control[at]}", still, (uint)control[at]);
        }
    }

    private void Windows()
    {
        Note($"closing has not returned after {Hung}s; the windows on the display:");
        var display = X11.XOpenDisplay(IntPtr.Zero);
        X11.XQueryTree(display, X11.XDefaultRootWindow(display), out _, out _, out var children, out var count);

        for (var index = 0; index < count; index++)
        {
            var window = children[index];
            X11.XFetchName(display, window, out var name);
            var shot = Capture(Text(window), $"hung-{index}");
            Note($"  0x{window:x} {Marshal.PtrToStringUTF8(name) ?? "None"} {(shot is not null ? "captured" : "not viewable")}");
        }
    }

    private double Answered(string window, string? before)
    {
        var begun = Stopwatch.GetTimestamp();
        var changed = false;
        var previous = before;

        while (Stopwatch.GetElapsedTime(begun).TotalSeconds < StillLimit)
        {
            Turn(20);
            var current = Capture(window, "answer");

            if (current is null || previous is null)
            {
                break;
            }

            changed = changed || desktop.Difference(before!, current) >= Changed;

            if (changed && desktop.Difference(previous, current) == 0)
            {
                break;
            }

            previous = current;
        }

        return Stopwatch.GetElapsedTime(begun).TotalSeconds;
    }

    private double Quiet(string window, double still)
    {
        var begun = Stopwatch.GetTimestamp();
        var calm = begun;
        var anchor = Capture(window, "calm");

        while (anchor is not null && Stopwatch.GetElapsedTime(calm).TotalSeconds < still
                                  && Stopwatch.GetElapsedTime(begun).TotalSeconds < StillLimit)
        {
            Turn(20);
            var current = Capture(window, "answer");

            if (current is null)
            {
                break;
            }

            if (desktop.Difference(anchor, current) >= Enough)
            {
                File.Move(current, anchor, overwrite: true);
                calm = Stopwatch.GetTimestamp();
            }
        }

        return Stopwatch.GetElapsedTime(begun).TotalSeconds;
    }

    private double Press(string window, string[] press)
    {
        var reaction = 0.0;

        foreach (var (x, y) in Pairs(press))
        {
            var still = Look(window, $"press-{x}-{y}-still");
            desktop.Run("xdotool", "mousemove", "--window", window, x, y, "click", "1");
            Turn(30);
            var shot = Look(window, $"press-{x}-{y}");
            var answered = still is not null && shot is not null ? desktop.Difference(still, shot) : 0.0;
            Note($"press {x} {y} answered {Fixed(answered)}");
            reaction = Math.Max(reaction, answered);
        }

        return reaction;
    }

    private (string Name, double Reaction) Aim(string window, int[] control)
    {
        foreach (var (x, y, name, still, index) in Named(window, control).Concat(Located(window)))
        {
            Note($"aim {name} at {x},{y}");

            if (closed)
            {
                break;
            }

            var (hit, shot) = Pressed(window, index, x, y, name);

            if (hit)
            {
                var reaction = shot is not null && still is not null ? desktop.Difference(still, shot) : 0.0;
                Note($"aim {name} moved parameter {index}, reaction {Fixed(reaction)}");
                return (name, reaction);
            }
        }

        return ("none", 0.0);
    }

    private (string Wine, string Told) Origin()
    {
        if (!File.Exists(log))
        {
            return ("none", "none");
        }

        var trace = File.ReadAllText(log);
        var wine = WineWindow().Matches(trace);
        var told = EditorGeometry.Told().Matches(trace);
        var placed = wine.Count > 0 ? desktop.Geometry(wine[^1].Groups[1].Value) : null;

        if (told.Count == 0 || placed is not { } at)
        {
            return ("none", "none");
        }

        return ($"({at.Left},{at.Top})", $"({told[^1].Groups[1].Value},{told[^1].Groups[2].Value})");
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(nuint value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\d+x\d+\+\d+\+\d+ ([0-9.]+),([0-9.]+) (\d+) gray\(255\)")]
    private static partial Regex Component();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"WINDOW=(\d+)")]
    private static partial Regex Pointer();

    [GeneratedRegex(@"DEBUG: wine_window: (\d+)")]
    private static partial Regex WineWindow();
}
