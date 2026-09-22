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

    private static int RunDoctor(Layout layout, IProcessRunner runner, bool json)
    {
        var checks = new Doctor(layout, runner).Run();

        if (json)
        {
            Console.WriteLine(Json.Checks(checks));
        }
        else
        {
            foreach (var check in checks)
            {
                var mark = check.Status switch
                {
                    Status.Ok => "ok  ",
                    Status.Warn => "warn",
                    _ => "FAIL",
                };

                Console.WriteLine($"{mark}  {check.Name,-24}  {check.Detail}");
            }
        }

        return checks.Any(check => check.Status == Status.Fail) ? Exit.Failed : Exit.Ok;
    }

    private static int ShowAbout(Layout layout, IProcessRunner runner, bool json)
    {
        var build = new About(layout, runner).Read();

        if (json)
        {
            Console.WriteLine(Json.Build(build));
            return Exit.Ok;
        }

        Console.WriteLine($"{"version",-16}  {build.Version}");
        Console.WriteLine($"{"installed from",-16}  {Describe(build)}");
        Console.WriteLine($"{"commit",-16}  {build.Commit}");
        Console.WriteLine($"{"yabridge",-16}  {build.Yabridge}");
        Console.WriteLine($"{"wine",-16}  {build.Wine}");
        Console.WriteLine($"{"prefixes",-16}  {layout.PrefixesDir}");
        Console.WriteLine($"{"runners",-16}  {layout.RunnersDir}");
        Console.WriteLine($"{"sockets",-16}  {layout.SocketDir}");
        Console.WriteLine($"{"runtime log",-16}  {layout.RuntimeLogPath}");
        Console.WriteLine($"{"yabridge dir",-16}  {layout.HostYabridgeDir}");

        if (build.Homepage is { } homepage)
        {
            Console.WriteLine($"{"homepage",-16}  {homepage}");
        }

        if (build.BugTracker is { } tracker)
        {
            Console.WriteLine($"{"issues",-16}  {tracker}");
        }

        return Exit.Ok;
    }

    private static string Describe(Build build) => build.Origin switch
    {
        Origin.Published => $"{build.Remote}  ({build.Url}) — published build",
        Origin.Local => $"{build.Remote}  ({build.Url}) — local build",
        _ => $"{build.Remote} — cannot tell whether it is published",
    };
}
