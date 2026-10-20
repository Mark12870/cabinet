using System.Globalization;
using System.Text.RegularExpressions;

namespace Cabinet.Probes;

internal sealed partial class EditorPopup(Carla host, string shots)
{
    private const int Drawing = 16;

    private const int DrawingLimit = 60;

    private const int PointerLimit = 30;

    private const int WindowLimit = 20;

    private readonly Desktop desktop = new(TimeSpan.FromSeconds(10));

    public static int Run(string prefix, string plugin, string format, string log, string[] rest)
    {
        var shots = rest[0];
        var at = rest[1..].Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        Libc.Export("YABRIDGE_DEBUG_LEVEL", "1+editor");
        Libc.Export("YABRIDGE_DEBUG_FILE", log);
        Directory.CreateDirectory(shots);

        var host = Carla.Standalone(prefix);
        host.Rack(prefix);

        if (!host.EngineInit("Dummy", "cabinet-editor-popup"))
        {
            Console.WriteLine("ENGINE=failed " + host.LastError());
            return 1;
        }

        try
        {
            return new EditorPopup(host, shots).Probe(plugin, format, (at[0], at[1]), (at[2], at[3]));
        }
        finally
        {
            host.EngineClose();
        }
    }

    private int Probe(string plugin, string format, (int X, int Y) open, (int X, int Y) away)
    {
        var byUri = format == "lv2";

        if (!host.AddPlugin(
                Carla.BinaryNative, Carla.Type(format), byUri ? null : plugin, null, byUri ? plugin : null))
        {
            Console.WriteLine("PLUGIN=failed " + host.LastError());
            return 1;
        }

        Idle(20);
        var before = desktop.Visible();
        host.ShowCustomUi(0, true);
        var editor = WaitForWindow(before, managed: true);

        if (editor is null)
        {
            Console.WriteLine("EDITOR=none");
            return 1;
        }

        Idle(20);
        desktop.Run("xdotool", "windowactivate", editor);
        desktop.Run("xdotool", "windowraise", editor);
        Idle(30);

        if (desktop.Capture(editor, shots, "before") is null)
        {
            Console.WriteLine("CAPTURE=failed");
            return 1;
        }

        var geometry = desktop.Geometry(editor);

        if (geometry is null)
        {
            Console.WriteLine("EDITOR=geometry-failed");
            return 1;
        }

        var (left, top, _, _) = geometry.Value;
        var existing = desktop.Visible();

        if (!Click(left + open.X, top + open.Y))
        {
            return 1;
        }

        var popup = WaitForWindow(existing, managed: false);

        if (popup is null)
        {
            Console.WriteLine("POPUP=none");
            return 1;
        }

        Idle(20);
        popup = LargestWindow(existing, managed: false);

        if (popup is null)
        {
            Console.WriteLine("POPUP=disappeared");
            return 1;
        }

        var popupGeometry = desktop.Geometry(popup);

        if (popupGeometry is null || desktop.Capture(editor, shots, "editor") is null)
        {
            Console.WriteLine($"POPUP={popup} CAPTURE=failed");
            return 1;
        }

        var colours = Drawn(popup);

        if (!Click(left + away.X, top + away.Y))
        {
            return 1;
        }

        Idle(20);
        var visible = desktop.Visible();
        var closed = !visible.Contains(popup);
        var alive = visible.Contains(editor) && desktop.Area(editor) > 1;
        var size = $"{popupGeometry.Value.Width}x{popupGeometry.Value.Height}";
        Console.WriteLine(
            $"POPUP={popup} SIZE={size} COLOURS={colours} CLOSED={(closed ? "yes" : "no")} "
            + $"EDITOR={(alive ? "alive" : "dead")}");

        host.ShowCustomUi(0, false);
        return closed && alive ? 0 : 1;
    }

    private bool Click(int x, int y)
    {
        var target = $"x:{x} y:{y}";
        var pointer = "";

        for (var attempt = 0; attempt < PointerLimit && pointer != target; attempt++)
        {
            desktop.MovePointer(x, y);
            Idle(10);
            pointer = Pointer().Match(desktop.Run("xdotool", "getmouselocation").Output).Value;
        }

        if (pointer != target)
        {
            Console.WriteLine($"POINTER=miss EXPECTED={target} ACTUAL={pointer}");
            return false;
        }

        desktop.Run("xdotool", "click", "1");
        return true;
    }

    private int Drawn(string popup)
    {
        var colours = 0;

        for (var attempt = 0; attempt < DrawingLimit && colours < Drawing; attempt++)
        {
            Idle(20);
            colours = desktop.Capture(popup, shots, "popup") is { } image ? desktop.Colours(image) : 0;
        }

        return colours;
    }

    private string? WaitForWindow(HashSet<string> before, bool managed)
    {
        for (var attempt = 0; attempt < WindowLimit; attempt++)
        {
            Idle(10);
            var found = LargestWindow(before, managed);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private string? LargestWindow(HashSet<string> before, bool managed)
    {
        var candidates = desktop.Visible().Except(before)
            .Where(window => !managed || desktop.Managed(window))
            .Select(window => (Window: window, Area: desktop.Area(window)))
            .Where(candidate => candidate.Area > 1)
            .OrderByDescending(candidate => candidate.Area)
            .Select(candidate => candidate.Window);

        return candidates.FirstOrDefault();
    }

    private void Idle(int turns)
    {
        for (var turn = 0; turn < turns; turn++)
        {
            host.EngineIdle();
            Thread.Sleep(50);
        }
    }

    [GeneratedRegex(@"x:-?\d+ y:-?\d+")]
    private static partial Regex Pointer();
}
