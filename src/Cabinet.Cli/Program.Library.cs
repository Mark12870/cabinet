using Cabinet.Core;

namespace Cabinet.Cli;

internal static partial class Program
{
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
            Console.WriteLine(Wrapped(entry.Consent));
            Console.WriteLine();
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
}
