namespace Cabinet.Core;

public enum Status
{
    Ok,
    Warn,
    Fail,
}

public sealed record Check(string Name, Status Status, string Detail);

public sealed partial class Doctor(Layout layout, IProcessRunner runner)
{
    public IReadOnlyList<Check> Run()
    {
        var checks = new List<Check>
        {
            BundledYabridge(),
            YabridgectlCanFindIt(),
            Shim(),
            SocketDirectory(),
            SharedMemory(),
            MemoryLock(),
        };

        checks.AddRange(ThirtyTwoBit(Layout.FlatpakInfo));
        checks.AddRange(PrefixRunners());
        checks.AddRange(BrokenRunners());
        checks.AddRange(Unnamed());
        checks.AddRange(InstalledTwice());
        checks.AddRange(Unfinished());
        checks.AddRange(LeftOpen());
        checks.AddRange(PartialDxvk());
        checks.AddRange(Retired());
        checks.AddRange(PluginRunners());
        checks.AddRange(PluginSync());
        checks.AddRange(PluginEnv());
        checks.AddRange(EnrolledDaws());
        checks.AddRange(NativeDaw());
        return checks;
    }

    private IEnumerable<(string Prefix, string Runner)> PrefixesAndRunners()
    {
        var prefixes = new Prefixes(layout, runner);

        return prefixes.Names().Select(name => (name, prefixes.RunnerOf(name)));
    }

    private IEnumerable<Check> PrefixRunners()
    {
        var prefixes = PrefixesAndRunners().ToList();

        if (prefixes.Count == 0)
        {
            yield break;
        }

        var broken = prefixes
            .Where(entry => entry.Runner != Layout.BundledRunner
                            && !File.Exists(layout.RunnerWine(entry.Runner)))
            .Select(entry => $"{entry.Prefix} -> {entry.Runner}")
            .ToList();

        yield return broken.Count == 0
            ? new Check("prefix runners", Status.Ok, "every prefix resolves to a Wine")
            : new Check("prefix runners", Status.Fail,
                $"missing runner for {string.Join(", ", broken)} — install it or move the "
                + "prefix to another Wine");
    }

    private IEnumerable<Check> BrokenRunners()
    {
        var broken = new Runners(layout, runner).List()
            .Where(installed => !installed.Usable)
            .Select(installed => installed.Name)
            .ToList();

        if (broken.Count > 0)
        {
            yield return new Check("broken runners", Status.Warn,
                $"{string.Join(", ", broken)} in {layout.RunnersDir} "
                + $"{(broken.Count == 1 ? "has" : "have")} no {Path.Combine("bin", "wine")}, "
                + "so no prefix can run on "
                + $"{(broken.Count == 1 ? "it — delete it and install it" : "them — delete them and install them")} "
                + "again");
        }
    }

    private IEnumerable<Check> Unfinished()
    {
        var library = new Library(layout, runner);
        var names = Names(library);
        var unfinished = library.Unfinished()
            .Select(left => left.Prefix is { } prefix
                ? $"{names(left.Id)} in {prefix}"
                : names(left.Id))
            .ToList();

        if (unfinished.Count > 0)
        {
            yield return new Check("unfinished installs", Status.Warn,
                $"{string.Join(", ", unfinished)} stopped part-way through installing. "
                + "Installing it again finishes it and clears what the first try left.");
        }
    }

    private IEnumerable<Check> LeftOpen()
    {
        var library = new Library(layout, runner);
        var names = Names(library);
        var open = library.LeftOpen()
            .Select(left => $"{names(left.Id)} in {left.Prefix}")
            .ToList();

        if (open.Count > 0)
        {
            yield return new Check("apps left open", Status.Warn,
                $"Cabinet stopped while {string.Join(", ", open)} was open, so what it installed "
                + "may not be bridged. Open it and close it again, and Cabinet finishes what it "
                + "does when an app closes.");
        }
    }

    private IEnumerable<Check> PartialDxvk()
    {
        var dxvk = new Dxvk(layout, runner);
        var partial = new Prefixes(layout, runner).Names().Where(dxvk.Partial).ToList();

        if (partial.Count > 0)
        {
            yield return new Check("partial DXVK", Status.Warn,
                $"{string.Join(", ", partial)} {(partial.Count == 1 ? "holds" : "hold")} part of "
                + "DXVK but Cabinet does not count it as on. Turn DXVK on to finish it, or take "
                + "it out.");
        }
    }

    private IEnumerable<Check> Retired()
    {
        var retired = new Library(layout, runner).Retired().Select(entry => entry.Id).ToList();

        if (retired.Count > 0)
        {
            yield return new Check("retired plugins", Status.Warn,
                $"{string.Join(", ", retired)} {(retired.Count == 1 ? "is" : "are")} installed "
                + "but no longer in this build's catalogue. They keep working, and the Library "
                + "lists them under No longer in the catalogue, where they can be removed.");
        }
    }

    private static Func<string, string> Names(Library library)
    {
        var entries = library.Entries()
            .ToDictionary(entry => entry.Id, entry => entry.Name, StringComparer.Ordinal);

        return id => entries.GetValueOrDefault(id, id);
    }

    private IEnumerable<Check> Unnamed()
    {
        var unnamed = new Prefixes(layout, runner).Unnamed();

        if (unnamed.Count > 0)
        {
            yield return new Check("prefix names", Status.Warn,
                $"{string.Join(", ", unnamed.Select(name => $"'{name}'"))} in "
                + $"{layout.PrefixesDir} cannot name a prefix, so Cabinet leaves them out — "
                + "rename them to one word of a path, not starting with a dot");
        }
    }

    private IEnumerable<Check> InstalledTwice()
    {
        var library = new Library(layout, runner);
        var installed = library.Installed();
        var twice = library.InstalledMoreThanOnce()
            .OrderBy(held => held.Key, StringComparer.Ordinal)
            .Select(held =>
                $"{held.Key} is recorded in {string.Join(" and ", held.Value)}, and Cabinet "
                + $"acts only on the one in {installed[held.Key]}")
            .ToList();

        if (twice.Count == 0)
        {
            yield break;
        }

        yield return new Check("installed twice", Status.Warn,
            string.Join("; ", twice)
            + ". Remove it once for each copy and install it again where you want it; a plugin "
            + "is installed in one prefix.");
    }

    private IEnumerable<Check> PluginRunners()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        var drifted = PrefixesAndRunners()
            .SelectMany(
                prefix => library.Recorded(prefix.Prefix),
                (prefix, id) => (prefix.Prefix, prefix.Runner, Id: id))
            .Select(held => (held.Prefix, held.Runner,
                Entry: entries.GetValueOrDefault(held.Id)))
            .Where(held => held.Entry?.Runner is { } wanted
                           && !Library.Answers(held.Runner, wanted))
            .Select(held =>
                $"{held.Prefix} keeps {held.Runner}, where {held.Entry!.Name} asks for "
                + $"Wine {held.Entry.Runner}")
            .ToList();

        if (drifted.Count == 0)
        {
            yield break;
        }

        yield return new Check("plugin runners", Status.Warn,
            string.Join("; ", drifted)
            + ". A plugin's entry pins the Wine its editor was tried on, and moving a prefix "
            + "to it needs the DAW closed.");
    }

    private IEnumerable<Check> PluginSync()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var settings = new PrefixSettings(layout);

        var drifted = PrefixesAndRunners()
            .SelectMany(
                prefix => library.Recorded(prefix.Prefix),
                (prefix, id) => (prefix.Prefix, Mode: settings.Sync(prefix.Prefix),
                    Entry: entries.GetValueOrDefault(id)))
            .Where(held => held.Entry is { Sync: not SyncMode.System } entry
                           && entry.Sync != held.Mode)
            .Select(held =>
                $"{held.Prefix} runs on {PrefixSettings.Word(held.Mode)}, where "
                + $"{held.Entry!.Name} asks for {PrefixSettings.Word(held.Entry.Sync)}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (drifted.Count == 0)
        {
            yield break;
        }

        yield return new Check("plugin sync", Status.Warn,
            string.Join("; ", drifted)
            + ". A prefix that already existed when the plugin was installed keeps the sync "
            + "mode it was made with, until you change it.");
    }

    private IEnumerable<Check> PluginEnv()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var settings = new PrefixSettings(layout);

        var missing = PrefixesAndRunners()
            .SelectMany(
                prefix => library.Recorded(prefix.Prefix),
                (prefix, id) => (prefix.Prefix, Held: settings.Variables(prefix.Prefix),
                    Entry: entries.GetValueOrDefault(id)))
            .Where(held => held.Entry is not null)
            .SelectMany(
                held => held.Entry!.Env.Where(
                    wanted => held.Held.GetValueOrDefault(wanted.Key) != wanted.Value),
                (held, wanted) =>
                    $"{held.Prefix} does not set {wanted.Key}={wanted.Value}, which "
                    + $"{held.Entry!.Name} asks for")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (missing.Count == 0)
        {
            yield break;
        }

        yield return new Check("plugin env", Status.Warn,
            string.Join("; ", missing)
            + ". A prefix that already existed when the plugin was installed keeps the "
            + "environment it was made with, until you set those variables on it.");
    }
}
