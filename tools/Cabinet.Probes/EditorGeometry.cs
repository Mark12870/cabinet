using System.Text.RegularExpressions;

namespace Cabinet.Probes;

internal static partial class EditorGeometry
{
    public static int Run(string prefix, string plugin, string format, string log)
    {
        Libc.Export("YABRIDGE_DEBUG_LEVEL", "1+editor");
        Libc.Export("YABRIDGE_DEBUG_FILE", log);

        var desktop = new Desktop();
        var host = Carla.Standalone(prefix);
        host.Rack(prefix);

        if (!host.EngineInit("Dummy", "cabinet-editor-geometry"))
        {
            Console.WriteLine("ENGINE=failed " + host.LastError());
            return 1;
        }

        try
        {
            if (!host.AddPlugin(Carla.BinaryNative, Carla.Type(format), plugin, null, null))
            {
                Console.WriteLine("PLUGIN=failed " + host.LastError());
                return 1;
            }

            Idle(host, 60);
            host.ShowCustomUi(0, true);
            Idle(host, 200);

            var (wrapper, wine, told) = Windows(log);

            if (wrapper is null || wine is null)
            {
                Console.WriteLine("EDITOR=not-traced");
                return 1;
            }

            Console.WriteLine(
                $"WRAPPER={Tuple(desktop.Position(wrapper))} WINE={Tuple(desktop.Position(wine))} TOLD={Tuple(told)}");
            host.ShowCustomUi(0, false);
        }
        finally
        {
            host.EngineClose();
        }

        return 0;
    }

    private static void Idle(Carla host, int turns)
    {
        for (var turn = 0; turn < turns; turn++)
        {
            host.EngineIdle();
            Thread.Sleep(50);
        }
    }

    private static string Tuple((int X, int Y)? at) => at is { } found ? $"({found.X}, {found.Y})" : "None";

    private static (string? Wrapper, string? Wine, (int, int)? Told) Windows(string log)
    {
        var trace = File.ReadAllText(log);

        string? Last(string name) =>
            Regex.Matches(trace, $@"DEBUG: {name}: (\d+)") is { Count: > 0 } found ? found[^1].Groups[1].Value : null;

        var told = Told().Matches(trace);

        return (Last("wrapper_window"), Last("wine_window"),
            told.Count > 0 ? (Desktop.Number(told[^1].Groups[1]), Desktop.Number(told[^1].Groups[2])) : null);
    }

    [GeneratedRegex(@"DEBUG: Translated coords: \d+ : \d+x\d+\+(-?\d+)\+(-?\d+)")]
    public static partial Regex Told();
}
