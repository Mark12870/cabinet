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
            "update" => UpdateFromLibrary(line, layout, runner),
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
        var updates = line.Flag("--updates");

        if (installed && notInstalled)
        {
            throw new UsageException("--installed and --not-installed exclude each other");
        }

        if (updates && notInstalled)
        {
            throw new UsageException("--updates and --not-installed exclude each other");
        }

        var filter = new LibraryFilter(
            line.Option("--search"),
            line.Option("--category"),
            line.Option("--developer"),
            line.Option("--kind") is { } kind ? Kind(kind) : null,
            installed ? true : notInstalled ? false : null);

        return line.Then(json => ListLibrary(layout, runner, filter, updates, json));
    }

    private static PluginKind Kind(string word) => word.ToLowerInvariant() switch
    {
        "windows" => PluginKind.Windows,
        "linux" or "native" => PluginKind.Native,
        _ => throw new UsageException($"--kind takes windows or linux, not {word}"),
    };

    private static int ListLibrary(
        Layout layout, IProcessRunner runner, LibraryFilter filter, bool onlyUpdates, bool json)
    {
        var library = new Library(layout, runner);
        var all = library.Entries();
        var installed = library.Installed();
        var reviews = library.PrefixReviews();
        var updates = reviews.Where(pair => pair.Value.Available).ToDictionary(pair => pair.Key, pair => pair.Value);

        var entries = all
            .Where(entry => filter.Matches(entry, installed.ContainsKey(entry.Id)))
            .Where(entry => !onlyUpdates || updates.ContainsKey(entry.Id))
            .ToList();
        var retired = library.Retired()
            .Where(entry => !onlyUpdates && filter.Matches(entry, true))
            .ToList();

        if (json)
        {
            Console.WriteLine(Json.Library(
                [.. entries, .. retired],
                installed,
                retired.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal), reviews));
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
            var mark = updates.ContainsKey(entry.Id) ? "up" : installed.ContainsKey(entry.Id) ? "ok" : "  ";

            Console.WriteLine(
                $"{mark}  {entry.Id.PadRight(width)}  {KindWord(entry),-8}  {cost,-5}  "
                + $"{entry.Category,-10}  {entry.Name}");
        }

        if (entries.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Install one with `cabinet library install <id>`.");
        }

        if (entries.Any(entry => updates.ContainsKey(entry.Id)))
        {
            Console.WriteLine(
                "up: A prefix update is available. `cabinet library update <id>` previews it, "
                + "`--all` every one.");
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

    private const int SetupWidth = 28;

    private static int ShowFromLibrary(
        Layout layout, IProcessRunner runner, string id, bool json)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);
        var installed = library.Installed();

        if (json)
        {
            Console.WriteLine(Json.Library([entry], installed, new HashSet<string>(), library.PrefixReviews()));
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
            ? where is null ? "yes" : null
            : "no");

        if (library.PrefixUpdateOf(entry) is { } review)
        {
            Console.WriteLine();
            Console.WriteLine("Prefix");
            Field("Installed in", $"{review.Prefix}  (`cabinet show {review.Prefix}`)", SetupWidth);
            Field("Installed version", review.Software, SetupWidth);
            Field("Config", $"{review.ConfigState} ({review.State.ToLowerInvariant()})", SetupWidth);
            Field(PrefixUpdate.DiffersFromConfig, review.Edits.Count > 0 ? string.Join("; ", review.Edits) : null, SetupWidth);
            Field("Shares this config", review.Members.Count > 1 ? string.Join(", ", review.Members) : null, SetupWidth);
            Field("Other plugins in this prefix", review.Sharing.Count > 0 ? string.Join(", ", review.Sharing) : null,
                SetupWidth);

            if (review.Available)
            {
                Console.WriteLine();
                Console.WriteLine(review.Title);
                Console.WriteLine(WrappedUpdate(review));
                Console.WriteLine($"`cabinet library update {id}` reviews and applies this setup.");
            }
        }

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

        static void Field(string name, string? value, int width = 10)
        {
            if (value is not null)
            {
                Console.WriteLine($"  {name.PadRight(width)}  {value}");
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
                $"cabinet library install {entry.Id}{PrefixOption(prefix)} <file>",
            _ => $"cabinet library install {entry.Id}{PrefixOption(prefix)}",
        };

    private static string PrefixOption(string? prefix) =>
        prefix is null ? "" : $" --prefix {prefix}";

    private static string BringYourOwn(LibraryEntry entry, string? prefix = null) =>
        (entry.InstallInstructions is { } instructions ? instructions + "\n\n" : "")
        + (entry.DemoUrl is not null
            ? $"{entry.Name} offers a demo — install it with `{Command(entry, prefix)}`, or "
              + (entry.Account is { } account
                  ? $"log in at {account}, download your copy, then "
                    + $"`{OwnCommand(entry, prefix)}`"
                  : $"pass the installer you already have: `{OwnCommand(entry, prefix)}`")
            : $"{entry.Name} cannot be downloaded — "
              + (entry.Account is { } accountPage
                  ? $"log in at {accountPage}, download it, then `{OwnCommand(entry, prefix)}`"
                  : $"pass the installer you already have: `{OwnCommand(entry, prefix)}`"));

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

    private static string WrappedUpdate(PrefixUpdate update) =>
        string.Join(Environment.NewLine, update.Description.Split('\n').Select(Wrapped));

    private static Func<int> UpdateFromLibrary(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var all = line.Flag("--all");
        var keepChanges = line.Flag("--keep-changes");
        var id = line.OptionalWord();

        if (all == (id is not null))
        {
            throw new UsageException("expected a plugin id or --all");
        }

        return line.Then(() => id is null
            ? UpdateEveryPrefix(layout, runner, keepChanges)
            : UpdateFromLibrary(layout, runner, id, keepChanges));
    }

    private static void KeepChangesHint(IReadOnlyList<PrefixUpdate> pending, bool keepChanges)
    {
        if (pending.Any(update => update.Resets.Count > 0))
        {
            Console.WriteLine(keepChanges
                ? "--keep-changes leaves your own changes as they are."
                : "The full config change is recommended; pass --keep-changes to leave your own changes as they are.");
            Console.WriteLine();
        }
    }

    private static int UpdateEveryPrefix(Layout layout, IProcessRunner runner, bool keepChanges)
    {
        var library = new Library(layout, runner);
        var pending = library.PendingPrefixUpdates();

        if (pending.Count == 0)
        {
            Console.WriteLine("Every prefix setup is up to date.");
            return Exit.Ok;
        }

        foreach (var update in pending)
        {
            Console.WriteLine($"{update.Title}: {update.Prefix}");
            Console.WriteLine();
            Console.WriteLine(WrappedUpdate(update));
            Console.WriteLine();
        }

        KeepChangesHint(pending, keepChanges);

        if (!Confirmed($"Apply {pending.Count} prefix update(s)? [y/N] "))
        {
            return LeftAlone();
        }

        foreach (var update in pending)
        {
            library.UpdatePrefix(update, Console.WriteLine, keepCustom: keepChanges);
        }

        Console.WriteLine("Every prefix setup is up to date.");
        return Exit.Ok;
    }

    private static int UpdateFromLibrary(Layout layout, IProcessRunner runner, string id, bool keepChanges)
    {
        var library = new Library(layout, runner);
        var entry = library.Find(id);

        if (entry.Kind == PluginKind.Native)
        {
            throw new InvalidOperationException($"{entry.Name} is a Linux plugin, so it has no Wine prefix to update");
        }

        var update = library.PrefixUpdateOf(entry)
                     ?? throw new KeyNotFoundException($"{entry.Name} is not installed");

        if (!update.Available)
        {
            Console.WriteLine($"{entry.Name}'s prefix setup is up to date.");
            return Exit.Ok;
        }

        Console.WriteLine(update.Title);
        Console.WriteLine();
        Console.WriteLine(WrappedUpdate(update));
        Console.WriteLine();
        KeepChangesHint([update], keepChanges);

        if (!Confirmed($"Apply this setup to prefix '{update.Prefix}'? [y/N] "))
        {
            return LeftAlone();
        }

        library.UpdatePrefix(update, Console.WriteLine, keepCustom: keepChanges);
        Console.WriteLine($"{entry.Name}'s prefix setup is up to date.");
        return Exit.Ok;
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

        Prepare(library, entry);
        library.Launch(entry, Console.WriteLine);
        return Exit.Ok;
    }

    private static void Prepare(Library library, LibraryEntry entry)
    {
        try
        {
            library.Prepare(entry, Console.WriteLine);
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"cabinet: {failure.Message}");
        }
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
        var library = new Library(layout, runner);

        Prepare(library, library.ForLink(link));
        library.Open(link, Console.WriteLine);
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
