using System.Diagnostics;
using Cabinet.Core;

namespace Cabinet.Cli;

internal static class Program
{
    private const string Usage = """
        Cabinet — Windows VST plugins on Linux, out of the box

        Usage:
          cabinet                              open the window
          cabinet enrol <daw-flatpak-id>       prepare a Flatpak DAW (prints the override)
          cabinet new <name> [runner]          create a Wine prefix, optionally on a runner
          cabinet install <name> <installer>   run a Windows installer in that prefix
          cabinet delete <name>                delete a prefix and everything in it
          cabinet list                         list prefixes
          cabinet use <name> <runner>          move a prefix to a runner and update it
          cabinet dxvk <name>                  install DXVK, the Direct3D some editors want
          cabinet show <name>                  everything a prefix is set to
          cabinet set <name> sync <mode>       system, esync, fsync or ntsync
          cabinet set <name> dxvk <on|off>     install DXVK, or put back what it replaced
          cabinet set <name> env KEY=VALUE     a variable for this prefix (KEY= removes it)
          cabinet set <name> desktop <on|off>   enable or disable a Wine desktop of its own
          cabinet winetricks <name> [verb...]  open Winetricks, or install the verbs given
          cabinet library                      plugins Cabinet knows how to install
          cabinet library show <id>            what a plugin is, and what installing costs
          cabinet library install <id> [prefix] [file]
                                               install one; demo entries download without a file
          cabinet library remove <id>          uninstall one, links, prefix and all
          cabinet library launch <id>          open a manager, bridging what it installs
          cabinet library stop <id>            close a manager Cabinet opened
          cabinet library open <link>          hand a sign-in link to the manager that registered it
          cabinet library log <id>             Cabinet and shared yabridge logs for an installed plugin
          cabinet runners                      list installed Wine runners
          cabinet runners available            list Wine versions you can install
          cabinet runners install <version>    download and unpack one
          cabinet runners add <archive>        unpack a Wine build you already have
          cabinet runners rm <runner>          delete a runner no prefix uses
          cabinet sync                         bridge again what changed outside Cabinet
          cabinet run <name> <cmd> [args...]   run a command in a prefix (winecfg, regedit)
          cabinet doctor                       check the setup end to end
          cabinet about                        which Cabinet this is, and what it bundles

        Options:
          --json                               machine-readable output where it applies

        Narrowing `cabinet library`:
          --search <text>                      every word must appear somewhere in the entry
          --category <name>                    Synth, Effect, Manager, Sampler, Drums, Bundle
          --developer <name>                   who makes it
          --kind <windows|linux>               under Wine, or native
          --installed, --not-installed         only what is here, or only what is not
        """;

    private static int Main(string[] args) =>
        Invoke(args, Layout.FromEnvironment, new ProcessRunner());

    internal static int Invoke(string[] args, Func<Layout> environment, IProcessRunner runner)
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
            return positional.Length == 0
                ? LaunchGui()
                : Dispatch(positional, json, environment(), runner);
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

    private static int Dispatch(string[] args, bool json, Layout layout, IProcessRunner runner)
    {
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
            "winetricks" => RunWinetricks(layout, runner, Require(args, 1, "a prefix name"),
                args.Skip(2).ToArray()),
            "install" => Install(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "an installer path")),
            "delete" => Delete(layout, runner, Require(args, 1, "a prefix name")),
            "list" => List(layout, runner, json),
            "sync" => Sync(layout, runner),
            "run" => Run(layout, runner, Require(args, 1, "a prefix name"),
                Require(args, 2, "a command"), args.Skip(3).ToArray()),
            "doctor" => RunDoctor(layout, runner, json),
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
        Console.WriteLine($"Install plugins with `cabinet install {prefix.Name} <installer>`.");
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
        new Prefixes(layout, runner).MoveToRunner(name, runnerName, Console.WriteLine);
        Console.WriteLine($"{name} now runs on {runnerName}.");
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
            "sync" => SetSync(layout, runner, name, Require(args, 1, "a sync mode")),
            "dxvk" => SetDxvk(layout, runner, name, Require(args, 1, "on or off")),
            "env" => SetVariable(layout, name, Require(args, 1, "KEY=VALUE")),
            "desktop" => SetDesktop(layout, runner, name, Require(args, 1, "on or off")),
            var unknown => Unknown($"set {name} {unknown}"),
        };

    private static int RunWinetricks(
        Layout layout, IProcessRunner runner, string name, string[] verbs)
    {
        var winetricks = new Winetricks(layout, runner);
        var result = verbs.Length == 0
            ? winetricks.Open(name, Console.WriteLine)
            : winetricks.Apply(name, verbs, Console.WriteLine);
        new Prefixes(layout, runner).Bridge(Console.WriteLine);

        return result.Ok ? 0 : result.ExitCode;
    }

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

        switch (word.Trim().ToLowerInvariant())
        {
            case "on":
                desktop.Set(name, Console.WriteLine);
                break;
            case "off":
                desktop.Unset(name, Console.WriteLine);
                break;
            default:
                throw new ArgumentException($"not on or off: '{word}'");
        }

        return 0;
    }

    private static int SetSync(
        Layout layout, IProcessRunner runner, string name, string word)
    {
        var mode = PrefixSettings.ParseSync(word);
        new Prefixes(layout, runner).SetSync(name, mode);

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
        Console.WriteLine($"{"desktop",-16}  {(prefix.Desktop ? "on" : "off")}");

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
        return new Prefixes(layout, runner).Install(name, installer, Console.WriteLine).ExitCode;
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
                + $"  {(prefix.Desktop ? "desktop" : ""),-9}"
                + $"  {prefix.Path}");
        }

        return 0;
    }

    private static int Library(Layout layout, IProcessRunner runner, string[] args, bool json)
    {
        var (filter, rest) = Narrowing(args);

        return rest.FirstOrDefault() switch
        {
            null => ListLibrary(layout, runner, filter, json),
            "show" => ShowFromLibrary(layout, runner, Require(rest, 1, "a plugin id"), json),
            "install" => InstallFromLibrary(layout, runner, rest.Skip(1).ToArray()),
            "remove" => RemoveFromLibrary(layout, runner, Require(rest, 1, "a plugin id")),
            "launch" => LaunchFromLibrary(layout, runner, Require(rest, 1, "a plugin id")),
            "stop" => StopFromLibrary(layout, runner, Require(rest, 1, "a plugin id")),
            "open" => OpenFromLibrary(layout, runner, Require(rest, 1, "a link")),
            "log" => LogFromLibrary(layout, runner, Require(rest, 1, "a plugin id")),
            var unknown => Unknown($"library {unknown}"),
        };
    }

    private static (LibraryFilter Filter, string[] Remaining) Narrowing(string[] args)
    {
        string? search = null;
        string? category = null;
        string? developer = null;
        PluginKind? kind = null;
        bool? installed = null;
        var rest = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--search":
                    search = Follows(args, ref index);
                    break;
                case "--category":
                    category = Follows(args, ref index);
                    break;
                case "--developer":
                    developer = Follows(args, ref index);
                    break;
                case "--kind":
                    kind = Kind(Follows(args, ref index));
                    break;
                case "--installed":
                    installed = true;
                    break;
                case "--not-installed":
                    installed = false;
                    break;
                default:
                    rest.Add(args[index]);
                    break;
            }
        }

        return (new LibraryFilter(search, category, developer, kind, installed), [.. rest]);
    }

    private static string Follows(string[] args, ref int index)
    {
        var flag = args[index];

        return ++index < args.Length
            ? args[index]
            : throw new InvalidOperationException($"{flag} needs something after it");
    }

    private static PluginKind Kind(string word) => word.ToLowerInvariant() switch
    {
        "windows" => PluginKind.Windows,
        "linux" or "native" => PluginKind.Native,
        _ => throw new InvalidOperationException($"--kind takes windows or linux, not {word}"),
    };

    private static int ListLibrary(
        Layout layout, IProcessRunner runner, LibraryFilter filter, bool json)
    {
        var library = new Library(layout, runner);
        var all = library.Entries();
        var installed = library.Installed();

        var entries = all
            .Where(entry => filter.Matches(entry, installed.ContainsKey(entry.Id)))
            .ToList();
        var retired = library.Retired().Where(entry => filter.Matches(entry, true)).ToList();

        if (json)
        {
            Console.WriteLine(Json.Library(
                [.. entries, .. retired],
                installed,
                retired.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal)));
            return 0;
        }

        if (all.Count == 0 && retired.Count == 0)
        {
            Console.WriteLine("This build shipped no library.");
            return 0;
        }

        if (entries.Count == 0 && retired.Count == 0)
        {
            Console.WriteLine("Nothing in the library matches that.");
            return 0;
        }

        var width = entries.Concat(retired).Max(entry => entry.Id.Length);

        foreach (var entry in entries)
        {
            var cost = entry.Licence == "Commercial" ? "paid" : "free";
            var mark = installed.ContainsKey(entry.Id) ? "ok" : "  ";

            Console.WriteLine(
                $"{mark}  {entry.Id.PadRight(width)}  {KindWord(entry),-8}  {cost,-5}  "
                + $"{entry.Category,-10}  {entry.Name}");
        }

        if (entries.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Install one with `cabinet library install <id>`.");
        }

        if (retired.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("No longer in the catalogue:");

            foreach (var entry in retired)
            {
                Console.WriteLine(
                    $"ok  {entry.Id.PadRight(width)}  {KindWord(entry),-8}  "
                    + (installed[entry.Id] is { } prefix ? $"in {prefix}" : ""));
            }

            Console.WriteLine();
            Console.WriteLine("Remove one with `cabinet library remove <id>`.");
        }

        return 0;
    }

    private static string KindWord(LibraryEntry entry) =>
        entry.Kind == PluginKind.Native ? "linux" : "windows";

    private static int ShowFromLibrary(
        Layout layout, IProcessRunner runner, string id, bool json)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);
        var installed = library.Installed();

        if (json)
        {
            Console.WriteLine(Json.Library([entry], installed, new HashSet<string>()));
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
            ? BringYourOwn(entry)
            : $"`{Command(entry)}` installs it."));
        return 0;

        static void Field(string name, string? value)
        {
            if (value is not null)
            {
                Console.WriteLine($"  {name,-10}  {value}");
            }
        }
    }

    private static string Bridged(LibraryEntry entry) =>
        string.Join("  ·  ", ["under Wine, bridged", .. entry.Requirements()]);

    private static string Command(LibraryEntry entry, string? prefix = null) =>
        entry.DemoUrl is not null
            ? $"cabinet library install {entry.Id}"
            : OwnCommand(entry, prefix);

    private static string OwnCommand(LibraryEntry entry, string? prefix = null) =>
        (entry.Source, entry.Kind) switch
        {
            (PluginSource.Byo, PluginKind.Native) => $"cabinet library install {entry.Id} <file>",
            (PluginSource.Byo, _) =>
                $"cabinet library install {entry.Id} {prefix ?? "<prefix>"} <installer.exe>",
            _ => $"cabinet library install {entry.Id}",
        };

    private static string BringYourOwn(LibraryEntry entry, string? prefix = null) =>
        entry.DemoUrl is not null
            ? $"{entry.Name} offers a demo — install it with `{Command(entry, prefix)}`, or "
              + (entry.Account is { } account
                  ? $"log in at {account}, download your installer, then "
                    + $"`{OwnCommand(entry, prefix)}`"
                  : $"pass the installer you already have: `{OwnCommand(entry, prefix)}`")
            : $"{entry.Name} cannot be downloaded — "
              + (entry.Account is { } accountPage
                  ? $"log in at {accountPage}, download it, then `{OwnCommand(entry, prefix)}`"
                  : $"pass the installer you already have: `{OwnCommand(entry, prefix)}`");

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
        var prefix = native ? null : Optional(args, 1);
        var file = Optional(args, native ? 1 : 2);

        if (entry.Source == PluginSource.Byo && file is null && entry.DemoUrl is null)
        {
            Console.Error.WriteLine($"cabinet: {BringYourOwn(entry, prefix)}");
            return 1;
        }

        library.Install(entry, prefix, file, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine(entry.Kind == PluginKind.Native
            ? $"{entry.Name} is installed. Your DAW loads it directly — rescan to find it."
            : $"{entry.Name} is installed and bridged.");
        return 0;
    }

    private static int RemoveFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var removal = library.RemovalOf(library.Removable(id));

        return removal.Kind switch
        {
            RemovalKind.Native => RemoveNative(library, removal),
            RemovalKind.TakesPrefix => RemoveManager(library, removal),
            _ => RemoveWindows(library, removal),
        };
    }

    private static int RemoveNative(Library library, Removal removal)
    {
        var entry = removal.Entry;

        Console.Write(entry.Data is { } data
            ? $"Remove {entry.Name}, the links your DAW scans, and ~/{data} with the presets "
              + "in it? [y/N] "
            : $"Remove {entry.Name} and the links your DAW scans? [y/N] ");

        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        library.Remove(removal, onOutput: Console.WriteLine);
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

        library.Launch(entry, Console.WriteLine);
        return 0;
    }

    private static int StopFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (entry.Launch is null)
        {
            Console.Error.WriteLine(
                $"cabinet: {entry.Name} is a plugin — your DAW opens it, not Cabinet");
            return 1;
        }

        var outcome = library.Stop(entry, onOutput: Console.WriteLine);

        return outcome.Result == StopResult.LeftRunning ? 1 : 0;
    }

    private static int OpenFromLibrary(Layout layout, IProcessRunner runner, string link)
    {
        new Library(layout, runner).Open(link, Console.WriteLine);
        return 0;
    }

    private static int LogFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (!library.Installed().ContainsKey(id))
        {
            Console.Error.WriteLine($"cabinet: {entry.Name} is not installed");
            return 1;
        }

        if (library.LaunchLog(entry) is not { } written)
        {
            Console.Error.WriteLine($"cabinet: no logs exist for {entry.Name}");
            return 1;
        }

        Console.Write(written);
        return 0;
    }

    private static int RemoveManager(Library library, Removal removal)
    {
        var (entry, prefix) = (removal.Entry, removal.Prefix);

        Console.WriteLine(
            $"{entry.Name}'s own uninstaller leaves everything it downloaded in prefix "
            + $"'{prefix}', so it is the prefix or nothing.");

        if (removal.Sharing.Count > 0)
        {
            Console.WriteLine(
                $"Prefix '{prefix}' also holds {string.Join(" and ", removal.Sharing)}, "
                + "which go with it.");
        }

        Console.Write($"Delete '{prefix}' and every library {entry.Name} put in it? [y/N] ");

        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        library.Remove(removal, takePrefix: true, onOutput: Console.WriteLine);
        return 0;
    }

    private static int RemoveWindows(Library library, Removal removal)
    {
        var (entry, prefix) = (removal.Entry, removal.Prefix);

        if (removal.Kind == RemovalKind.PluginOrPrefix)
        {
            Console.WriteLine(
                $"{entry.Name} is the only plugin Cabinet installed in prefix '{prefix}'.");
            Console.Write("Delete the prefix and everything in it? [y/N] ");

            if (Yes())
            {
                library.Remove(removal, takePrefix: true, onOutput: Console.WriteLine);
                return 0;
            }
        }
        else
        {
            Console.WriteLine(
                $"Prefix '{prefix}' also holds {string.Join(" and ", removal.Sharing)}, "
                + "so it stays.");
        }

        Console.Write($"Run {entry.Name}'s own uninstaller? It may open a window. [y/N] ");

        if (!Yes())
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        var possible = library.PossibleUninstallers(removal);
        var chosen = possible.Count > 1 ? Which(entry, possible) : null;

        if (possible.Count > 1 && chosen is null)
        {
            Console.WriteLine("Left alone.");
            return 1;
        }

        library.Remove(removal, uninstaller: chosen, onOutput: Console.WriteLine);
        return 0;
    }

    private static UninstallEntry? Which(LibraryEntry entry, IReadOnlyList<UninstallEntry> possible)
    {
        Console.WriteLine($"Any of these could be {entry.Name}'s uninstaller:");

        for (var index = 0; index < possible.Count; index++)
        {
            Console.WriteLine($"  {index + 1}  {possible[index].Name}");
        }

        Console.Write($"Which one runs? [1-{possible.Count}, or Enter to leave it alone] ");

        return int.TryParse(Console.ReadLine()?.Trim(), out var number)
               && number >= 1
               && number <= possible.Count
            ? possible[number - 1]
            : null;
    }

    private static bool Yes() =>
        string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

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

        return result.ExitCode;
    }

    private static int Run(
        Layout layout, IProcessRunner runner, string name, string command, string[] arguments)
    {
        var prefixes = new Prefixes(layout, runner);
        var result = prefixes.Run(name, command, arguments, Console.WriteLine, inheritStdin: true);
        prefixes.Bridge(Console.Error.WriteLine);
        return result.ExitCode;
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
