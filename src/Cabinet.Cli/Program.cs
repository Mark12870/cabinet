using Cabinet.Core;

namespace Cabinet.Cli;

/// <summary>
/// Argument parsing and rendering only. Everything it calls lives in Cabinet.Core, so
/// a GUI can do the same work without going through a subprocess.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Cabinet — Windows VST plugins in per-plugin Wine prefixes

        Usage:
          cabinet setup                        export yabridge and the shim to the host
          cabinet enrol <daw-flatpak-id>       prepare a Flatpak DAW (prints the override)
          cabinet new <name>                   create a Wine prefix
          cabinet install <name> <installer>   run a Windows installer in that prefix
          cabinet list                         list prefixes
          cabinet sync                         hand the prefixes to yabridgectl
          cabinet run <name> <cmd> [args...]   run a command in a prefix (winecfg, regedit)
          cabinet doctor                       check the setup end to end

        Options:
          --json                               machine-readable output where it applies
        """;

    private static int Main(string[] args)
    {
        var json = args.Contains("--json");
        var positional = args.Where(a => a != "--json").ToArray();

        if (positional.Length == 0 || positional[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return positional.Length == 0 ? 2 : 0;
        }

        try
        {
            return Dispatch(positional, json);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"cabinet: {exception.Message}");
            return 1;
        }
    }

    private static int Dispatch(string[] args, bool json)
    {
        var layout = Layout.FromEnvironment();
        var runner = new ProcessRunner();

        return args[0] switch
        {
            "setup" => Setup(layout, runner),
            "enrol" or "enroll" => Enrol(layout, Require(args, 1, "a DAW flatpak id")),
            "new" => New(layout, runner, Require(args, 1, "a prefix name")),
            "install" => Install(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "an installer path")),
            "list" => List(layout, runner, json),
            "sync" => Sync(layout, runner),
            "run" => Run(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "a command"), args.Skip(3).ToArray()),
            "doctor" => RunDoctor(layout, json),
            _ => Unknown(args[0]),
        };
    }

    private static int Setup(Layout layout, IProcessRunner runner)
    {
        var report = Core.Setup.Run(layout);
        new Yabridgectl(layout, runner).SetPath();

        Console.WriteLine($"yabridge      {report.YabridgeDir}");
        Console.WriteLine($"shim          {report.ShimPath}");
        Console.WriteLine($"sockets       {report.SocketDir}");
        Console.WriteLine($"prefixes      {layout.PrefixesDir}");
        Console.WriteLine();

        if (report.EnvironmentDWritten)
        {
            Console.WriteLine($"Wrote {report.EnvironmentDFile} for natively installed DAWs.");
            Console.WriteLine("systemd reads it at login, so log out and back in before using one.");
        }

        Console.WriteLine("For a Flatpak DAW, run `cabinet enrol <daw-flatpak-id>` instead.");
        return 0;
    }

    private static int Enrol(Layout layout, string dawId)
    {
        var link = layout.DawYabridgeLink(dawId);
        var dataHome = layout.DawDataHome(dawId);

        if (!Directory.Exists(dataHome))
        {
            Console.Error.WriteLine($"cabinet: {dataHome} does not exist — is {dawId} installed?");
            return 1;
        }

        // The chainloader resolves yabridge through the DAW's own XDG_DATA_HOME, which
        // for a Flatpak DAW is this directory and nowhere else.
        if (Path.Exists(link))
        {
            File.Delete(link);
        }

        File.CreateSymbolicLink(link, layout.YabridgeDir);
        Console.WriteLine($"Linked {link} -> {layout.YabridgeDir}");
        Console.WriteLine();
        Console.WriteLine("Now run this yourself:");
        Console.WriteLine();
        Console.WriteLine("  " + Enrolment.OverrideCommand(dawId, layout));
        Console.WriteLine();
        Console.WriteLine("It is not applied automatically: --talk-name=org.freedesktop.Flatpak");
        Console.WriteLine($"lets {dawId} run commands on the host, which is yours to decide.");
        return 0;
    }

    private static int New(Layout layout, IProcessRunner runner, string name)
    {
        var prefix = new Prefixes(layout, runner).Create(name);
        Console.WriteLine($"{prefix.Name}  {prefix.Path}");
        Console.WriteLine($"Install plugins into {layout.PrefixVst3Dir(name)}, then `cabinet sync`.");
        return 0;
    }

    private static int Install(Layout layout, IProcessRunner runner, string name, string installer)
    {
        var prefixes = new Prefixes(layout, runner);
        prefixes.Create(name);

        var result = prefixes.Install(name, installer);
        Console.Write(result.Stdout);
        Console.Error.Write(result.Stderr);

        if (!result.Ok)
        {
            return result.ExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("Installer finished. Run `cabinet sync` to bridge what it installed.");
        return 0;
    }

    private static int List(Layout layout, IProcessRunner runner, bool json)
    {
        var prefixes = new Prefixes(layout, runner).List();

        if (json)
        {
            Console.WriteLine(Json.Prefixes(prefixes));
            return 0;
        }

        if (prefixes.Count == 0)
        {
            Console.WriteLine("No prefixes yet. Create one with `cabinet new <name>`.");
            return 0;
        }

        foreach (var prefix in prefixes)
        {
            Console.WriteLine($"{(prefix.Initialised ? "ok  " : "bare")}  {prefix.Name,-20}  {prefix.Path}");
        }

        return 0;
    }

    private static int Sync(Layout layout, IProcessRunner runner)
    {
        var prefixes = new Prefixes(layout, runner).List();
        var result = new Yabridgectl(layout, runner).SyncPrefixes(prefixes);

        Console.Write(result.Stdout);
        Console.Error.Write(result.Stderr);
        return result.ExitCode;
    }

    private static int Run(
        Layout layout, IProcessRunner runner, string name, string command, string[] arguments)
    {
        var result = new Prefixes(layout, runner).Run(name, command, arguments);
        Console.Write(result.Stdout);
        Console.Error.Write(result.Stderr);
        return result.ExitCode;
    }

    private static int RunDoctor(Layout layout, bool json)
    {
        var checks = new Doctor(layout).Run();

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

        return checks.Any(check => check.Status == Status.Fail) ? 1 : 0;
    }

    private static string Require(string[] args, int index, string what)
    {
        if (args.Length <= index)
        {
            throw new ArgumentException($"expected {what}");
        }

        return args[index];
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"cabinet: unknown command '{command}'");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
