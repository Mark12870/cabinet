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
          cabinet set <name> desktop <on|off>  enable or disable a Wine desktop of its own
          cabinet winetricks <name> [verb...]  open Winetricks, or install the verbs given
          cabinet library                      plugins Cabinet knows how to install
          cabinet library show <id>            what a plugin is, and what installing costs
          cabinet library install <id> [--prefix <name>] [file]
                                               install one; demo entries download without a file
          cabinet library remove <id>          uninstall one; asks before taking its prefix
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
          --json                               JSON on stdout, from list, library, library show,
                                               doctor and about only
          --                                   no options after this; whatever follows the prefix
                                               of run and winetricks is passed on as it is

        Narrowing `cabinet library`:
          --search <text>                      every word must appear somewhere in the entry
          --category <name>                    Synth, Effect, Manager, Sampler, Drums, Bundle
          --developer <name>                   who makes it
          --kind <windows|linux>               under Wine, or native
          --installed, --not-installed         only what is here, or only what is not

        Exit status:
          0 done, 1 failed, 2 not a valid command, 3 declined at a prompt,
          4 no such prefix, plugin, runner, file or log. `run` exits with its command's status.
          Prompts and errors go to stderr.
        """;

    private static int Main(string[] args) =>
        Invoke(args, Layout.FromEnvironment, new ProcessRunner());

    internal static int Invoke(string[] args, Func<Layout> environment, IProcessRunner runner)
    {
        try
        {
            return args.Length == 0 ? LaunchGui() : Dispatch(CommandLine.Parse(args), environment, runner);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"cabinet: {exception.Message}");

            return exception switch
            {
                UsageException => Exit.Usage,
                KeyNotFoundException or FileNotFoundException or DirectoryNotFoundException => Exit.Absent,
                _ => Exit.Failed,
            };
        }
    }

    private static int LaunchGui()
    {
        using var gui = Process.Start(Layout.Gui)
            ?? throw new InvalidOperationException($"could not start {Layout.Gui}");

        gui.WaitForExit();
        return gui.ExitCode;
    }

    private static int Dispatch(CommandLine line, Func<Layout> environment, IProcessRunner runner)
    {
        if (line.Help)
        {
            Console.WriteLine(Usage);
            return Exit.Ok;
        }

        var layout = environment();
        var command = Parse(line, layout, runner);

        Bootstrap.Ensure(layout);
        return command();
    }

    private static Func<int> Parse(CommandLine line, Layout layout, IProcessRunner runner) =>
        line.Verb() switch
        {
            "enrol" or "enroll" => Enrol(line, layout),
            "new" => New(line, layout, runner),
            "library" => Library(line, layout, runner),
            "runners" => Runners(line, layout, runner),
            "use" => Use(line, layout, runner),
            "dxvk" => One(line, "a prefix name", name => InstallDxvk(layout, runner, name)),
            "show" => One(line, "a prefix name", name => Show(layout, runner, name)),
            "set" => Set(line, layout, runner),
            "winetricks" => RunWinetricks(line, layout, runner),
            "install" => Install(line, layout, runner),
            "delete" => One(line, "a prefix name", name => Delete(layout, runner, name)),
            "list" => line.Then(json => List(layout, runner, json)),
            "sync" => line.Then(() => Sync(layout, runner)),
            "run" => Run(line, layout, runner),
            "doctor" => line.Then(json => RunDoctor(layout, runner, json)),
            "about" => line.Then(json => ShowAbout(layout, runner, json)),
            var unknown => throw line.Unknown(unknown),
        };

    private static Func<int> One(CommandLine line, string what, Func<string, int> act)
    {
        var word = line.Word(what);
        return line.Then(() => act(word));
    }

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

    private static Func<int> New(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var runnerName = line.OptionalWord();

        return line.Then(() =>
        {
            var prefix = new Prefixes(layout, runner).Create(name, runnerName, Console.WriteLine);
            Console.WriteLine($"{prefix.Name}  {prefix.Path}  ({prefix.Runner})");
            Console.WriteLine($"Install plugins with `cabinet install {prefix.Name} <installer>`.");
            return Exit.Ok;
        });
    }

    private static Func<int> Runners(CommandLine line, Layout layout, IProcessRunner runner) =>
        line.Subcommand() switch
        {
            null => line.Then(() => ListRunners(layout, runner)),
            "available" => line.Then(() => AvailableRunners(runner)),
            "install" => One(line, "a Wine version", version => InstallRunner(layout, runner, version)),
            "add" => One(line, "an archive path", archive => AddRunner(layout, runner, archive)),
            "rm" => One(line, "a runner name", name => RemoveRunner(layout, runner, name)),
            var unknown => throw line.Unknown($"runners {unknown}"),
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

        return Exit.Ok;
    }

    private static int AvailableRunners(IProcessRunner runner)
    {
        var families = new RunnerIndex(runner).Available().GroupBy(release => release.Family);

        foreach (var family in families)
        {
            Console.WriteLine($"{family.Key.Label} — {family.Key.Description}");

            foreach (var release in family)
            {
                var pinned = RunnerIndex.IsPinned(release) ? "  pinned" : "";
                Console.WriteLine($"  {release.Version,-10}  {release.Name}{pinned}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(RunnerIndex.Provenance);
        return Exit.Ok;
    }

    private static int InstallRunner(Layout layout, IProcessRunner runner, string version)
    {
        var release = new RunnerIndex(runner).Find(version);
        var installed = new Runners(layout, runner).Install(release, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"{installed.Name}  {installed.Wine}");
        Console.WriteLine($"Put a prefix on it with `cabinet use <prefix> {installed.Name}`.");
        return Exit.Ok;
    }

    private static int AddRunner(Layout layout, IProcessRunner runner, string archive)
    {
        var added = new Runners(layout, runner).Add(archive, onOutput: Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"{added.Name}  {added.Wine}");
        Console.WriteLine($"Put a prefix on it with `cabinet use <prefix> {added.Name}`.");
        return Exit.Ok;
    }

    private static int RemoveRunner(Layout layout, IProcessRunner runner, string name)
    {
        new Runners(layout, runner).Remove(name);
        Console.WriteLine($"Deleted {layout.RunnerPath(name)}");
        return Exit.Ok;
    }

    private static Func<int> Use(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var runnerName = line.Word("a runner name");

        return line.Then(() =>
        {
            new Prefixes(layout, runner).MoveToRunner(name, runnerName, Console.WriteLine);
            Console.WriteLine($"{name} now runs on {runnerName}.");
            return Exit.Ok;
        });
    }

    private static int InstallDxvk(Layout layout, IProcessRunner runner, string name)
    {
        new Dxvk(layout, runner).Install(name, Console.WriteLine);

        Console.WriteLine("Reopen the plugin in your DAW; its editor should redraw as you use it.");
        return Exit.Ok;
    }

    private static int RemoveDxvk(Layout layout, IProcessRunner runner, string name)
    {
        new Dxvk(layout, runner).Remove(name, Console.WriteLine);
        return Exit.Ok;
    }

    private static Func<int> Set(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");

        return line.Subcommand() switch
        {
            "sync" => One(line, "a sync mode", word => SetSync(layout, runner, name, Sync(word))),
            "dxvk" => One(line, "on or off", word => OnOff(word)
                ? InstallDxvk(layout, runner, name)
                : RemoveDxvk(layout, runner, name)),
            "env" => One(line, "KEY=VALUE", assignment => SetVariable(layout, name, Assignment(assignment))),
            "desktop" => One(line, "on or off", word => SetDesktop(layout, runner, name, OnOff(word))),
            null => throw new UsageException("expected sync, dxvk, env or desktop"),
            var unknown => throw line.Unknown($"set {name} {unknown}"),
        };
    }

    private static Func<int> RunWinetricks(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var verbs = line.PassedThrough;

        return line.Then(() =>
        {
            var winetricks = new Winetricks(layout, runner);
            var result = verbs.Count == 0
                ? winetricks.Open(name, Console.WriteLine)
                : winetricks.Apply(name, verbs, Console.WriteLine);
            new Prefixes(layout, runner).Bridge(Console.WriteLine);

            return Exited("winetricks", result);
        });
    }

    private static bool OnOff(string word) => word.Trim().ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => throw new UsageException($"expected on or off, not '{word}'"),
    };

    private static SyncMode Sync(string word)
    {
        try
        {
            return PrefixSettings.ParseSync(word);
        }
        catch (ArgumentException invalid)
        {
            throw new UsageException(invalid.Message);
        }
    }

    private static int SetDesktop(Layout layout, IProcessRunner runner, string name, bool on)
    {
        var desktop = new VirtualDesktop(layout, runner);

        if (on)
        {
            desktop.Set(name, Console.WriteLine);
        }
        else
        {
            desktop.Unset(name, Console.WriteLine);
        }

        return Exit.Ok;
    }

    private static int SetSync(Layout layout, IProcessRunner runner, string name, SyncMode mode)
    {
        new Prefixes(layout, runner).SetSync(name, mode, Console.WriteLine);
        return Exit.Ok;
    }

    private static int SetVariable(Layout layout, string name, (string Key, string? Value) assignment)
    {
        var (key, value) = assignment;
        new PrefixSettings(layout).SetVariable(name, key, value);

        Console.WriteLine(value is null
            ? $"{key} removed from {name}."
            : $"{key}={value} in {name}.");
        return Exit.Ok;
    }

    private static (string Key, string? Value) Assignment(string text)
    {
        var at = text.IndexOf('=');

        if (at <= 0)
        {
            throw new UsageException($"expected <name>=<value>, got '{text}'");
        }

        var value = text[(at + 1)..];
        return (text[..at], value.Length == 0 ? null : value);
    }

    private static int Show(Layout layout, IProcessRunner runner, string name)
    {
        var prefix = new Prefixes(layout, runner).List()
            .FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new KeyNotFoundException($"no such prefix '{name}'");

        Console.WriteLine($"{"name",-16}  {prefix.Name}");
        Console.WriteLine($"{"path",-16}  {prefix.Path}");
        Console.WriteLine($"{"state",-16}  {(prefix.Initialised ? "initialised" : "bare")}");
        Console.WriteLine($"{"runner",-16}  {prefix.Runner}");
        Console.WriteLine($"{"dxvk",-16}  {prefix.Dxvk ?? "off"}");
        Console.WriteLine($"{"sync",-16}  {PrefixSettings.Word(prefix.Sync)}");
        Console.WriteLine($"{"desktop",-16}  {(prefix.Desktop ? "on" : "off")}");

        Describe("env", new PrefixSettings(layout).Variables(name));

        return Exit.Ok;
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

    private static Func<int> Install(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var installer = line.Word("an installer path");

        return line.Then(() => Exited(
            Path.GetFileName(installer),
            new Prefixes(layout, runner).Install(name, installer, Console.WriteLine)));
    }

    private static int Delete(Layout layout, IProcessRunner runner, string name)
    {
        var prefixes = new Prefixes(layout, runner);
        var prefix = prefixes.List().FirstOrDefault(candidate => candidate.Name == name)
                     ?? throw new KeyNotFoundException($"no such prefix '{name}'");

        if (!Confirmed($"Delete '{prefix.Name}' and every plugin installed in it? [y/N] "))
        {
            return LeftAlone();
        }

        prefixes.Delete(prefix.Name, Console.WriteLine);
        return Exit.Ok;
    }

    private static int List(Layout layout, IProcessRunner runner, bool json)
    {
        var prefixes = new Prefixes(layout, runner).List();

        if (json)
        {
            Console.WriteLine(Json.Prefixes(prefixes));
            return Exit.Ok;
        }

        if (prefixes.Count == 0)
        {
            Console.WriteLine("No prefixes yet. Create one with `cabinet new <name>`.");
            return Exit.Ok;
        }

        foreach (var prefix in prefixes)
        {
            Console.WriteLine(
                $"{(prefix.Initialised ? "ok  " : "bare")}  {prefix.Name,-20}  "
                + $"{prefix.Runner,-24}  {(prefix.Dxvk is null ? "" : "dxvk " + prefix.Dxvk),-11}"
                + $"  {(prefix.Desktop ? "desktop" : ""),-9}"
                + $"  {prefix.Path}");
        }

        return Exit.Ok;
    }

    private static Func<int> Library(CommandLine line, Layout layout, IProcessRunner runner) =>
        line.Subcommand() switch
        {
            null => ListLibrary(line, layout, runner),
            "show" => ShowFromLibrary(line, layout, runner),
            "install" => InstallFromLibrary(line, layout, runner),
            "remove" => One(line, "a plugin id", id => RemoveFromLibrary(layout, runner, id)),
            "launch" => One(line, "a plugin id", id => LaunchFromLibrary(layout, runner, id)),
            "stop" => One(line, "a plugin id", id => StopFromLibrary(layout, runner, id)),
            "open" => One(line, "a link", link => OpenFromLibrary(layout, runner, link)),
            "log" => One(line, "a plugin id", id => LogFromLibrary(layout, runner, id)),
            var unknown => throw line.Unknown($"library {unknown}"),
        };

    private static Func<int> ListLibrary(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var installed = line.Flag("--installed");
        var notInstalled = line.Flag("--not-installed");

        if (installed && notInstalled)
        {
            throw new UsageException("--installed and --not-installed exclude each other");
        }

        var filter = new LibraryFilter(
            line.Option("--search"),
            line.Option("--category"),
            line.Option("--developer"),
            line.Option("--kind") is { } kind ? Kind(kind) : null,
            installed ? true : notInstalled ? false : null);

        return line.Then(json => ListLibrary(layout, runner, filter, json));
    }

    private static PluginKind Kind(string word) => word.ToLowerInvariant() switch
    {
        "windows" => PluginKind.Windows,
        "linux" or "native" => PluginKind.Native,
        _ => throw new UsageException($"--kind takes windows or linux, not {word}"),
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
            return Exit.Ok;
        }

        if (all.Count == 0 && retired.Count == 0)
        {
            Console.WriteLine("This build shipped no library.");
            return Exit.Ok;
        }

        if (entries.Count == 0 && retired.Count == 0)
        {
            Console.WriteLine("Nothing in the library matches that.");
            return Exit.Ok;
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

        return Exit.Ok;
    }

    private static string KindWord(LibraryEntry entry) =>
        entry.Kind == PluginKind.Native ? "linux" : "windows";

    private static Func<int> ShowFromLibrary(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var id = line.Word("a plugin id");
        return line.Then(json => ShowFromLibrary(layout, runner, id, json));
    }

    private static int ShowFromLibrary(
        Layout layout, IProcessRunner runner, string id, bool json)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);
        var installed = library.Installed();

        if (json)
        {
            Console.WriteLine(Json.Library([entry], installed, new HashSet<string>()));
            return Exit.Ok;
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
        return Exit.Ok;

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
            ? $"cabinet library install {entry.Id}{PrefixOption(prefix)}"
            : OwnCommand(entry, prefix);

    private static string OwnCommand(LibraryEntry entry, string? prefix = null) =>
        (entry.Source, entry.Kind) switch
        {
            (PluginSource.Byo, PluginKind.Native) => $"cabinet library install {entry.Id} <file>",
            (PluginSource.Byo, _) =>
                $"cabinet library install {entry.Id}{PrefixOption(prefix)} <installer.exe>",
            _ => $"cabinet library install {entry.Id}{PrefixOption(prefix)}",
        };

    private static string PrefixOption(string? prefix) =>
        prefix is null ? "" : $" --prefix {prefix}";

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

    private static Func<int> InstallFromLibrary(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(line.Word("a plugin id"));
        var prefix = line.Option("--prefix");
        var file = line.OptionalWord();

        if (prefix is not null && entry.Kind == PluginKind.Native)
        {
            throw new UsageException($"{entry.Name} is a Linux plugin, so it takes no --prefix");
        }

        if (entry.Source == PluginSource.Byo && file is null && entry.DemoUrl is null)
        {
            throw new UsageException(BringYourOwn(entry, prefix));
        }

        return line.Then(() =>
        {
            library.Install(entry, prefix, file, Console.WriteLine);

            Console.WriteLine();
            Console.WriteLine(entry.Kind == PluginKind.Native
                ? $"{entry.Name} is installed. Your DAW loads it directly — rescan to find it."
                : $"{entry.Name} is installed and bridged.");
            return Exit.Ok;
        });
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

        if (!Confirmed(entry.Data is { } data
                ? $"Remove {entry.Name}, the links your DAW scans, and ~/{data} with the presets "
                  + "in it? [y/N] "
                : $"Remove {entry.Name} and the links your DAW scans? [y/N] "))
        {
            return LeftAlone();
        }

        library.Remove(removal, onOutput: Console.WriteLine);
        return Exit.Ok;
    }

    private static int LaunchFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = Manager(library, id);

        library.Launch(entry, Console.WriteLine);
        return Exit.Ok;
    }

    private static int StopFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var outcome = library.Stop(Manager(library, id), onOutput: Console.WriteLine);

        return outcome.Result == StopResult.LeftRunning ? Exit.Failed : Exit.Ok;
    }

    private static LibraryEntry Manager(Library library, string id)
    {
        var entry = library.Find(id);

        return entry.Launch is not null
            ? entry
            : throw new InvalidOperationException($"{entry.Name} is a plugin — your DAW opens it, not Cabinet");
    }

    private static int OpenFromLibrary(Layout layout, IProcessRunner runner, string link)
    {
        new Library(layout, runner).Open(link, Console.WriteLine);
        return Exit.Ok;
    }

    private static int LogFromLibrary(Layout layout, IProcessRunner runner, string id)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (!library.Installed().ContainsKey(id))
        {
            throw new KeyNotFoundException($"{entry.Name} is not installed");
        }

        Console.Write(library.LaunchLog(entry)
                      ?? throw new FileNotFoundException($"no logs exist for {entry.Name}"));
        return Exit.Ok;
    }

    private static int RemoveManager(Library library, Removal removal)
    {
        var (entry, prefix) = (removal.Entry, removal.Prefix);

        Console.Error.WriteLine(
            $"{entry.Name}'s own uninstaller leaves everything it downloaded in prefix "
            + $"'{prefix}', so it is the prefix or nothing.");

        if (removal.Sharing.Count > 0)
        {
            Console.Error.WriteLine(
                $"Prefix '{prefix}' also holds {string.Join(" and ", removal.Sharing)}, "
                + "which go with it.");
        }

        if (!Confirmed($"Delete '{prefix}' and every library {entry.Name} put in it? [y/N] "))
        {
            return LeftAlone();
        }

        library.Remove(removal, takePrefix: true, onOutput: Console.WriteLine);
        return Exit.Ok;
    }

    private static int RemoveWindows(Library library, Removal removal)
    {
        var (entry, prefix) = (removal.Entry, removal.Prefix);

        if (removal.Kind == RemovalKind.PluginOrPrefix)
        {
            Console.Error.WriteLine(
                $"{entry.Name} is the only plugin Cabinet installed in prefix '{prefix}'.");

            if (Confirmed("Delete the prefix and everything in it? [y/N] "))
            {
                library.Remove(removal, takePrefix: true, onOutput: Console.WriteLine);
                return Exit.Ok;
            }
        }
        else
        {
            Console.Error.WriteLine(
                $"Prefix '{prefix}' also holds {string.Join(" and ", removal.Sharing)}, "
                + "so it stays.");
        }

        if (!Confirmed($"Run {entry.Name}'s own uninstaller? It may open a window. [y/N] "))
        {
            return LeftAlone();
        }

        var possible = library.PossibleUninstallers(removal);
        var chosen = possible.Count > 1 ? Which(entry, possible) : null;

        if (possible.Count > 1 && chosen is null)
        {
            return LeftAlone();
        }

        library.Remove(removal, uninstaller: chosen, onOutput: Console.WriteLine);
        return Exit.Ok;
    }

    private static UninstallEntry? Which(LibraryEntry entry, IReadOnlyList<UninstallEntry> possible)
    {
        Console.Error.WriteLine($"Any of these could be {entry.Name}'s uninstaller:");

        for (var index = 0; index < possible.Count; index++)
        {
            Console.Error.WriteLine($"  {index + 1}  {possible[index].Name}");
        }

        Console.Error.Write($"Which one runs? [1-{possible.Count}, or Enter to leave it alone] ");

        return int.TryParse(Console.ReadLine()?.Trim(), out var number)
               && number >= 1
               && number <= possible.Count
            ? possible[number - 1]
            : null;
    }

    private static bool Confirmed(string question)
    {
        Console.Error.Write(question);
        return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }

    private static int LeftAlone()
    {
        Console.Error.WriteLine("Left alone.");
        return Exit.Declined;
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

    private static Func<int> Run(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var command = line.PassedThrough.FirstOrDefault() ?? throw new UsageException("expected a command");
        var arguments = line.PassedThrough.Skip(1).ToArray();

        return line.Then(() =>
        {
            var prefixes = new Prefixes(layout, runner);
            var result = prefixes.Run(name, command, arguments, interactive: true);
            prefixes.Bridge(Console.Error.WriteLine);
            return result.ExitCode;
        });
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

    private static int Exited(string what, ProcessResult result)
    {
        if (result.Ok)
        {
            return Exit.Ok;
        }

        Console.Error.WriteLine($"cabinet: {what} exited with {result.ExitCode}");
        return Exit.Failed;
    }
}
