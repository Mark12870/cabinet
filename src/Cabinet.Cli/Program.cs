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
          cabinet dxvk <name>                  install DXVK, the Direct3D some editors want
          cabinet show <name>                  everything a prefix is set to
          cabinet set <name> sync <mode>       system, esync, fsync or ntsync
          cabinet set <name> dxvk <on|off>     install DXVK, or put back what it replaced
          cabinet set <name> env KEY=VALUE     a variable for this prefix (KEY= removes it)
          cabinet set <name> desktop <WxH>     a Wine desktop of its own, or off
          cabinet library                      plugins Cabinet knows how to install
          cabinet library show <id>            what a plugin is, and what installing costs
          cabinet library install <id> [prefix] [file]
                                               install one; demo entries download without a file
          cabinet library remove <id>          uninstall one, links, prefix and all
          cabinet library launch <id>          open a manager, bridging what it installs
          cabinet library log <id>             what the last launch of a manager printed
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
            "library" => Library(layout, runner, args.Skip(1).ToArray(), json),
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
            "desktop" => SetDesktop(layout, runner, name, Require(args, 1, "a size, or off")),
            var unknown => Unknown($"set {name} {unknown}"),
        };

    private static int SetDxvk(Layout layout, IProcessRunner runner, string name, string word) =>
        word.Trim().ToLowerInvariant() switch
        {
            "on" => InstallDxvk(layout, runner, name),
            "off" => RemoveDxvk(layout, runner, name),
            _ => throw new ArgumentException($"not on or off: '{word}'"),
        };

    private static int SetDesktop(
        Layout layout, IProcessRunner runner, string name, string word)
    {
        var desktop = new VirtualDesktop(layout, runner);

        if (word.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            desktop.Unset(name, Console.WriteLine);
        }
        else
        {
            desktop.Set(name, word, Console.WriteLine);
        }

        return 0;
    }

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
        Console.WriteLine($"{"desktop",-16}  {prefix.Desktop ?? "off"}");

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
        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        prefixes.Delete(prefix.Name, Console.WriteLine);
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

    private static int Library(
        Layout layout, IProcessRunner runner, string[] args, bool json) =>
        args.FirstOrDefault() switch
        {
            null => ListLibrary(layout, runner, json),
            "show" => ShowFromLibrary(layout, runner, Require(args, 1, "a plugin id"), json),
            "install" => InstallFromLibrary(layout, runner, args.Skip(1).ToArray()),
            "remove" => RemoveFromLibrary(layout, runner, Require(args, 1, "a plugin id")),
            "launch" => LaunchFromLibrary(layout, runner, Require(args, 1, "a plugin id")),
            "log" => LogFromLibrary(layout, runner, Require(args, 1, "a plugin id")),
            var unknown => Unknown($"library {unknown}"),
        };

    private static int ListLibrary(Layout layout, IProcessRunner runner, bool json)
    {
        var library = new Library(layout, runner);
        var entries = library.Entries();
        var installed = library.Installed();

        if (json)
        {
            Console.WriteLine(Json.Library(entries, installed));
            return 0;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("This build shipped no library.");
            return 0;
        }

        var width = entries.Max(entry => entry.Id.Length);

        foreach (var entry in entries)
        {
            var kind = entry.Kind == PluginKind.Native ? "linux" : "windows";
            var cost = entry.Licence == "Commercial" ? "paid" : "free";
            var mark = installed.ContainsKey(entry.Id) ? "ok" : "  ";

            Console.WriteLine(
                $"{mark}  {entry.Id.PadRight(width)}  {kind,-8}  {cost,-5}  "
                + $"{entry.Category,-10}  {entry.Name}");
        }

        Console.WriteLine();
        Console.WriteLine("Install one with `cabinet library install <id>`.");
        return 0;
    }

    private static int ShowFromLibrary(
        Layout layout, IProcessRunner runner, string id, bool json)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);
        var installed = library.Installed();

        if (json)
        {
            Console.WriteLine(Json.Library([entry], installed));
            return 0;
        }

        Console.WriteLine(entry.Name);
        Console.WriteLine(new string('-', entry.Name.Length));
        Console.WriteLine();

        foreach (var paragraph in entry.Description.Count > 0
                     ? entry.Description
                     : [entry.Summary])
        {
            Console.WriteLine(Wrapped(paragraph));
            Console.WriteLine();
        }

        Field("Developer", entry.Developer);
        Field("Version", entry.Version);
        Field("Category", entry.Category);
        Field("Licence", entry.Licence);
        Field("Account", entry.Account);
        Field("Formats", entry.Formats.Count > 0 ? string.Join(", ", entry.Formats) : null);
        Field("Runs", entry.Kind == PluginKind.Native ? "natively on Linux" : Bridged(entry));
        Field("Presets", entry.Data is { } data ? "~/" + data : null);
        Field("Website", entry.Homepage);
        Field("Installed", installed.TryGetValue(id, out var where)
            ? where is null ? "yes" : $"in prefix {where}"
            : "no");

        if (entry.Licensing is { } licensing)
        {
            Console.WriteLine();
            Console.WriteLine(Wrapped(licensing));
        }

        if (entry.Source == PluginSource.Rolling)
        {
            Console.WriteLine();
            Console.WriteLine(Wrapped(Cabinet.Core.Library.Unverifiable(entry.Url!)));
        }

        Console.WriteLine();
        Console.WriteLine(Wrapped(entry.Source == PluginSource.Byo
            ? Cabinet.Core.Library.BringYourOwn(entry)
            : $"`{Cabinet.Core.Library.Command(entry)}` installs it."));
        return 0;

        static void Field(string name, string? value)
        {
            if (value is not null)
            {
                Console.WriteLine($"  {name,-10}  {value}");
            }
        }
    }

    private static string Bridged(LibraryEntry entry)
    {
        var costs = new List<string> { "under Wine, bridged" };

        if (entry.Runner is { } wine)
        {
            costs.Add($"Wine {wine}");
        }

        if (entry.Dxvk)
        {
            costs.Add("DXVK");
        }

        if (entry.Sync != SyncMode.System)
        {
            costs.Add(PrefixSettings.Word(entry.Sync));
        }

        if (entry.Env.Count > 0)
        {
            costs.Add(string.Join(", ", entry.Env.Keys));
        }

        return string.Join("  ·  ", costs);
    }

    private static string Wrapped(string paragraph)
    {
        var lines = new List<string>();
        var line = "";

        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length + word.Length + 1 > 76)
            {
                lines.Add(line);
                line = "";
            }

            line = line.Length == 0 ? word : $"{line} {word}";
        }

        lines.Add(line);
        return string.Join(Environment.NewLine, lines);
    }

    private static int InstallFromLibrary(Layout layout, IProcessRunner runner, string[] args)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(Require(args, 0, "a plugin id"));

        var native = entry.Kind == PluginKind.Native;

        library.Install(
            entry,
            native ? null : Optional(args, 1),
            Optional(args, native ? 1 : 2),
            Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine(entry.Kind == PluginKind.Native
            ? $"{entry.Name} is installed. Your DAW loads it directly — rescan to find it."
            : $"{entry.Name} is installed and bridged.");
        return 0;
    }

    private static int RemoveFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (!library.Installed().TryGetValue(id, out var prefix))
        {
            Console.Error.WriteLine($"cabinet: {entry.Name} is not installed");
            return 1;
        }

        return entry.Kind == PluginKind.Native
            ? RemoveNative(library, entry)
            : RemoveWindows(library, entry, prefix!);
    }

    private static int RemoveNative(Library library, LibraryEntry entry)
    {
        Console.Write(entry.Data is { } data
            ? $"Remove {entry.Name}, the links your DAW scans, and ~/{data} with the presets "
              + "in it? [y/N] "
            : $"Remove {entry.Name} and the links your DAW scans? [y/N] ");

        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        library.Remove(entry, onOutput: Console.WriteLine);
        return 0;
    }

    private static int LaunchFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (entry.Launch is null)
        {
            Console.Error.WriteLine(
                $"cabinet: {entry.Name} is a plugin — your DAW opens it, not Cabinet");
            return 1;
        }

        if (!library.Installed().TryGetValue(id, out var prefix))
        {
            Console.Error.WriteLine($"cabinet: {entry.Name} is not installed");
            return 1;
        }

        library.Launch(entry, prefix, Console.WriteLine);
        return 0;
    }

    private static int LogFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (!library.Installed().TryGetValue(id, out var prefix))
        {
            Console.Error.WriteLine($"cabinet: {entry.Name} is not installed");
            return 1;
        }

        if (library.LaunchLog(entry, prefix) is not { } written)
        {
            Console.Error.WriteLine($"cabinet: {entry.Name} has not been opened from Cabinet");
            return 1;
        }

        Console.Write(written);
        return 0;
    }

    private static int RemoveManager(Library library, LibraryEntry entry, string prefix)
    {
        Console.WriteLine(
            $"{entry.Name}'s own uninstaller leaves everything it downloaded in prefix "
            + $"'{prefix}', so it is the prefix or nothing.");
        Console.Write($"Delete '{prefix}' and every library {entry.Name} put in it? [y/N] ");

        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        library.Remove(entry, prefix, takePrefix: true, onOutput: Console.WriteLine);
        return 0;
    }

    private static int RemoveWindows(Library library, LibraryEntry entry, string prefix)
    {
        if (entry.Launch is not null)
        {
            return RemoveManager(library, entry, prefix);
        }

        var sharing = library.Sharing(prefix, entry.Id);

        if (sharing.Count == 0)
        {
            Console.WriteLine(
                $"{entry.Name} is the only plugin Cabinet installed in prefix '{prefix}'.");
            Console.Write("Delete the prefix and everything in it? [y/N] ");

            if (Yes())
            {
                library.Remove(entry, prefix, takePrefix: true, onOutput: Console.WriteLine);
                return 0;
            }
        }
        else
        {
            Console.WriteLine(
                $"Prefix '{prefix}' also holds {string.Join(" and ", sharing)}, so it stays.");
        }

        Console.Write($"Run {entry.Name}'s own uninstaller? It may open a window. [y/N] ");

        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        library.Remove(entry, prefix, onOutput: Console.WriteLine);
        return 0;
    }

    private static bool Yes() =>
        string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

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
        var checks = new Doctor(layout, new ProcessRunner()).Run();

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

    private static string? Optional(string[] args, int index) =>
        args.Length > index ? args[index] : null;

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"cabinet: unknown command '{command}'");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
