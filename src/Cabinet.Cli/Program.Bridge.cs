using Cabinet.Core;

namespace Cabinet.Cli;

internal static partial class Program
{
    private static Func<int> Enrol(CommandLine line, Layout layout)
    {
        var dawId = line.Word("a DAW flatpak id");

        if (!Enrolment.IsAppId(dawId))
        {
            throw new UsageException(Enrolment.NotAnAppId(dawId));
        }

        return line.Then(() => Enrol(layout, dawId));
    }

    private static int Enrol(Layout layout, string dawId)
    {
        Console.WriteLine("Run this yourself:");
        Console.WriteLine();
        Console.WriteLine("  " + Enrolment.OverrideCommand(dawId, layout));
        Console.WriteLine();
        Console.WriteLine("It is not applied automatically, because it is yours to decide:");
        Console.WriteLine(Enrolment.TrustBoundary(dawId));
        Console.WriteLine();
        Console.WriteLine("Then check the shim loads inside that DAW's runtime, which is older");
        Console.WriteLine("than the one it was built against on some DAWs:");
        Console.WriteLine();
        Console.WriteLine("  " + Enrolment.SelfTestCommand(dawId, layout));
        return Exit.Ok;
    }

    private static int Sync(Layout layout, IProcessRunner runner)
    {
        var prefixes = new Prefixes(layout, runner).List();
        var result = new Yabridgectl(layout, runner).SyncAndPublish(prefixes);

        Console.Write(result.Stdout);
        Console.Error.Write(result.Stderr);

        foreach (var dawId in new Doctor(layout, runner).DawsMissingPermissions())
        {
            Console.Error.WriteLine(
                $"{dawId} needs updated permissions to load Windows plugins — run `cabinet enrol {dawId}`");
        }

        return Exited("yabridgectl", result);
    }
}
