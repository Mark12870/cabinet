using System.Diagnostics;
using Cabinet.Core;

namespace Cabinet.Cli;

internal static class Program
{
    private const string Usage = """
        Cabinet — Windows VST plugins in per-plugin Wine prefixes

        Usage:
          cabinet                              open the window
          cabinet enrol <daw-flatpak-id>       prepare a Flatpak DAW (prints the override)
          cabinet new <name> [runner]          create a Wine prefix, optionally on a runner
          cabinet install <name> <installer>   run a Windows installer in that prefix
          cabinet delete <name>                delete a prefix and everything in it
          cabinet list                         list prefixes
          cabinet use <name> <runner>          point a prefix at a runner
          cabinet dxvk <name>                  install DXVK, which JUCE editors need
          cabinet show <name>                  everything a prefix is set to
          cabinet set <name> sync <mode>       system, esync, fsync or ntsync
          cabinet set <name> dxvk <on|off>     install DXVK, or put back what it replaced
          cabinet set <name> env KEY=VALUE     a variable for this prefix (KEY= removes it)
          cabinet runners                      list installed Wine runners
          cabinet runners available            list Wine versions you can install
          cabinet runners install <version>    download and unpack one
          cabinet runners add <archive>        unpack a Wine build you already have
          cabinet runners rm <runner>          delete a runner no prefix uses
          cabinet sync                         hand the prefixes to yabridgectl
          cabinet run <name> <cmd> [args...]   run a command in a prefix (winecfg, regedit)
          cabinet doctor                       check the setup end to end
          cabinet about                        which Cabinet this is, and what it bundles

        Options:
          --json                               machine-readable output where it applies
        """;

    private static int Main(string[] args)
    {
        var json = args.Contains("--json");
        var positional = args.Where(a => a != "--json").ToArray();

        if (positional.Length > 0 && positional[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            return positional.Length == 0 ? LaunchGui() : Dispatch(positional, json);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"cabinet: {exception.Message}");
            return 1;
        }
    }

    private static int LaunchGui()
    {
        using var gui = Process.Start(Layout.Gui)
            ?? throw new InvalidOperationException($"could not start {Layout.Gui}");

        gui.WaitForExit();
        return gui.ExitCode;
    }

    private static int Dispatch(string[] args, bool json)
    {
        var layout = Layout.FromEnvironment();
        var runner = new ProcessRunner();

        Bootstrap.Ensure(layout);

        return args[0] switch
        {
            "enrol" or "enroll" => Enrol(layout, Require(args, 1, "a DAW flatpak id")),
            "new" => New(layout, runner, Require(args, 1, "a prefix name"),
                args.Length > 2 ? args[2] : null),
            "runners" => Runners(layout, runner, args.Skip(1).ToArray()),
            "use" => Use(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "a runner name")),
            "dxvk" => InstallDxvk(layout, runner, Require(args, 1, "a prefix name")),
            "show" => Show(layout, runner, Require(args, 1, "a prefix name")),
            "set" => Set(layout, runner, Require(args, 1, "a prefix name"),
                args.Skip(2).ToArray()),
            "install" => Install(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "an installer path")),
            "delete" => Delete(layout, runner, Require(args, 1, "a prefix name")),
            "list" => List(layout, runner, json),
            "sync" => Sync(layout, runner),
            "run" => Run(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "a command"), args.Skip(3).ToArray()),
            "doctor" => RunDoctor(layout, json),
            "about" => ShowAbout(layout, runner, json),
            _ => Unknown(args[0]),
        };
    }

    private static int Enrol(Layout layout, string dawId)
    {
        var link = Enrolment.Link(dawId, layout);

        Console.WriteLine($"Linked {link} -> {layout.HostYabridgeDir}");
        Console.WriteLine();
        Console.WriteLine("Now run this yourself:");
        Console.WriteLine();
        Console.WriteLine("  " + Enrolment.OverrideCommand(dawId, layout));
        Console.WriteLine();
        Console.WriteLine("It is not applied automatically: --talk-name=org.freedesktop.Flatpak");
        Console.WriteLine($"lets {dawId} run commands on the host, which is yours to decide.");
        Console.WriteLine();
        Console.WriteLine("Then check the shim loads inside that DAW's runtime, which is older");
        Console.WriteLine("than the one it was built against on some DAWs:");
        Console.WriteLine();
        Console.WriteLine("  " + Enrolment.SelfTestCommand(dawId, layout));
        return 0;
    }

    private static int New(
        Layout layout, IProcessRunner runner, string name, string? runnerName)
    {
        var prefix = new Prefixes(layout, runner).Create(name, runnerName, Console.WriteLine);
        Console.WriteLine($"{prefix.Name}  {prefix.Path}  ({prefix.Runner})");
        Console.WriteLine($"Install plugins into {layout.PrefixVst3Dir(name)}, then `cabinet sync`.");
        return 0;
    }

    private static int Runners(Layout layout, IProcessRunner runner, string[] args) =>
        args.FirstOrDefault() switch
        {
            null => ListRunners(layout, runner),
            "available" => AvailableRunners(runner),
            "install" => InstallRunner(layout, runner, Require(args, 1, "a Wine version")),
            "add" => AddRunner(layout, runner, Require(args, 1, "an archive path")),
            "rm" => RemoveRunner(layout, runner, Require(args, 1, "a runner name")),
            var unknown => Unknown($"runners {unknown}"),
        };

    private static int ListRunners(Layout layout, IProcessRunner runner)
    {
        var runners = new Runners(layout, runner);

        foreach (var found in runners.List())
        {
            var by = string.Join(", ", runners.InUseBy(found.Name));

            Console.WriteLine(
                $"{(found.Usable ? "ok  " : "FAIL")}  {found.Name,-30}  "
                + $"{(found.Multilib ? "32+64" : "64   ")}  {runners.Version(found),-34}  {by}");
        }

        return 0;
    }

    private static int AvailableRunners(IProcessRunner runner)
    {
        var families = new RunnerIndex(runner).Available().GroupBy(release => release.Family);

        foreach (var family in families)
        {
            Console.WriteLine($"{family.Key.Label} — {family.Key.Description}");

            foreach (var release in family)
            {
                Console.WriteLine($"  {release.Version,-10}  {release.Name}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static int InstallRunner(Layout layout, IProcessRunner runner, string version)
    {
        var release = new RunnerIndex(runner).Find(version);
        var installed = new Runners(layout, runner).Install(release, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"{installed.Name}  {installed.Wine}");
        Console.WriteLine($"Put a prefix on it with `cabinet use <prefix> {installed.Name}`.");
        return 0;
    }

    private static int AddRunner(Layout layout, IProcessRunner runner, string archive)
    {
        var added = new Runners(layout, runner).Add(archive, onOutput: Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"{added.Name}  {added.Wine}");
        Console.WriteLine($"Put a prefix on it with `cabinet use <prefix> {added.Name}`.");
        return 0;
    }

    private static int RemoveRunner(Layout layout, IProcessRunner runner, string name)
    {
        new Runners(layout, runner).Remove(name);
        Console.WriteLine($"Deleted {layout.RunnerPath(name)}");
        return 0;
    }

    private static int Use(Layout layout, IProcessRunner runner, string name, string runnerName)
    {
        new Prefixes(layout, runner).SetRunner(name, runnerName);
        Console.WriteLine($"{name} now runs on {runnerName}.");
        Console.WriteLine($"Run `cabinet run {name} wineboot -u` to update the prefix for it.");
        return 0;
    }

    private static int InstallDxvk(Layout layout, IProcessRunner runner, string name)
    {
        new Dxvk(layout, runner).Install(name, Console.WriteLine);

        Console.WriteLine("Reopen the plugin in your DAW; its editor should redraw as you use it.");
        return 0;
    }

    private static int RemoveDxvk(Layout layout, IProcessRunner runner, string name)
    {
        new Dxvk(layout, runner).Remove(name, Console.WriteLine);
        return 0;
    }

    private static int Set(Layout layout, IProcessRunner runner, string name, string[] args) =>
        args.FirstOrDefault() switch
        {
            "sync" => SetSync(layout, name, Require(args, 1, "a sync mode")),
            "dxvk" => SetDxvk(layout, runner, name, Require(args, 1, "on or off")),
            "env" => SetVariable(layout, name, Require(args, 1, "KEY=VALUE")),
            var unknown => Unknown($"set {name} {unknown}"),
        };

    private static int SetDxvk(Layout layout, IProcessRunner runner, string name, string word) =>
        word.Trim().ToLowerInvariant() switch
        {
            "on" => InstallDxvk(layout, runner, name),
            "off" => RemoveDxvk(layout, runner, name),
            _ => throw new ArgumentException($"not on or off: '{word}'"),
        };

    private static int SetSync(Layout layout, string name, string word)
    {
        var mode = PrefixSettings.ParseSync(word);
        new PrefixSettings(layout).SetSync(name, mode);

        Console.WriteLine($"{name} now waits on {PrefixSettings.Word(mode)}.");
        return 0;
    }

    private static int SetVariable(Layout layout, string name, string assignment)
    {
        var (key, value) = Assignment(assignment);
        new PrefixSettings(layout).SetVariable(name, key, value);

        Console.WriteLine(value is null
            ? $"{key} removed from {name}."
            : $"{key}={value} in {name}.");
        return 0;
    }

    private static (string Key, string? Value) Assignment(string text)
    {
        var at = text.IndexOf('=');

        if (at <= 0)
        {
            throw new ArgumentException($"expected <name>=<value>, got '{text}'");
        }

        var value = text[(at + 1)..];
        return (text[..at], value.Length == 0 ? null : value);
    }

    private static int Show(Layout layout, IProcessRunner runner, string name)
    {
        var prefix = new Prefixes(layout, runner).List()
            .FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new ArgumentException($"no such prefix '{name}'");

        Console.WriteLine($"{"name",-16}  {prefix.Name}");
        Console.WriteLine($"{"path",-16}  {prefix.Path}");
        Console.WriteLine($"{"state",-16}  {(prefix.Initialised ? "initialised" : "bare")}");
        Console.WriteLine($"{"runner",-16}  {prefix.Runner}");
        Console.WriteLine($"{"dxvk",-16}  {prefix.Dxvk ?? "off"}");
        Console.WriteLine($"{"sync",-16}  {PrefixSettings.Word(prefix.Sync)}");

        Describe("env", new PrefixSettings(layout).Variables(name));

        return 0;
    }

    private static void Describe(string label, IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0)
        {
            Console.WriteLine($"{label,-16}  none");
            return;
        }

        foreach (var (key, value) in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{label,-16}  {key}={value}");
        }
    }

    private static int Install(Layout layout, IProcessRunner runner, string name, string installer)
    {
        var prefixes = new Prefixes(layout, runner);
        prefixes.Create(name);

        var result = prefixes.Install(name, installer, Console.WriteLine);
        if (!result.Ok)
        {
            return result.ExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("Installer finished. Run `cabinet sync` to bridge what it installed.");
        return 0;
    }

    private static int Delete(Layout layout, IProcessRunner runner, string name)
    {
        var prefixes = new Prefixes(layout, runner);
        var prefix = prefixes.List().FirstOrDefault(candidate => candidate.Name == name);

        if (prefix is null)
        {
            Console.Error.WriteLine($"cabinet: no such prefix '{name}'");
            return 1;
        }

        Console.Write($"Delete '{prefix.Name}' and every plugin installed in it? [y/N] ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        prefixes.Delete(prefix.Name);
        Console.WriteLine($"Deleted {prefix.Path}");
        Console.WriteLine("Run `cabinet sync` to unbridge what it held.");
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
            Console.WriteLine(
                $"{(prefix.Initialised ? "ok  " : "bare")}  {prefix.Name,-20}  "
                + $"{prefix.Runner,-24}  {(prefix.Dxvk is null ? "" : "dxvk " + prefix.Dxvk),-11}"
                + $"  {prefix.Path}");
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
        return new Prefixes(layout, runner)
            .Run(name, command, arguments, Console.WriteLine).ExitCode;
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

    private static int ShowAbout(Layout layout, IProcessRunner runner, bool json)
    {
        var build = new About(layout, runner).Read();

        if (json)
        {
            Console.WriteLine(Json.Build(build));
            return 0;
        }

        Console.WriteLine($"{"version",-16}  {build.Version}");
        Console.WriteLine($"{"installed from",-16}  {Describe(build)}");
        Console.WriteLine($"{"commit",-16}  {build.Commit}");
        Console.WriteLine($"{"yabridge",-16}  {build.Yabridge}");
        Console.WriteLine($"{"wine",-16}  {build.Wine}");
        Console.WriteLine($"{"prefixes",-16}  {layout.PrefixesDir}");
        Console.WriteLine($"{"runners",-16}  {layout.RunnersDir}");
        Console.WriteLine($"{"sockets",-16}  {layout.SocketDir}");
        Console.WriteLine($"{"yabridge dir",-16}  {layout.HostYabridgeDir}");

        if (build.Homepage is { } homepage)
        {
            Console.WriteLine($"{"homepage",-16}  {homepage}");
        }

        if (build.BugTracker is { } tracker)
        {
            Console.WriteLine($"{"issues",-16}  {tracker}");
        }

        return 0;
    }

    private static string Describe(Build build) => build.Origin switch
    {
        Origin.Published => $"{build.Remote}  ({build.Url}) — published build",
        Origin.Local => $"{build.Remote}  ({build.Url}) — local build",
        _ => $"{build.Remote} — cannot tell whether it is published",
    };

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
