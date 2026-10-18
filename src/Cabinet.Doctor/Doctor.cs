namespace Cabinet.Core;

public enum Status
{
    Ok,
    Warn,
    Fail,
}

public sealed record Check(string Name, Status Status, string Detail, string? Daw = null);

public sealed partial class Doctor(Layout layout, IProcessRunner runner)
{
    public const string PrefixUpdatesCheck = "prefix updates";

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
        checks.AddRange(PrefixUpdates());
        checks.AddRange(MixedPrefixes());
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

    private IEnumerable<Check> PrefixUpdates()
    {
        var pending = new Library(layout, runner).PendingPrefixUpdates();

        if (pending.Count == 0)
        {
            yield break;
        }

        yield return new Check(PrefixUpdatesCheck, Status.Warn,
            string.Join("; ", pending.Select(update =>
                $"{update.Prefix} has a changed setup for {string.Join(", ", update.Members)}")));
    }

    private IEnumerable<Check> MixedPrefixes()
    {
        var library = new Library(layout, runner);
        var entries = library.Entries().ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        var mixed = new Prefixes(layout, runner).Names()
            .Select(prefix => (Prefix: prefix, Held: library.Recorded(prefix)
                .Distinct(StringComparer.Ordinal)
                .Where(entries.ContainsKey)
                .GroupBy(id => entries[id].Prefix, StringComparer.Ordinal)
                .ToList()))
            .Where(held => held.Held.Count > 1)
            .Select(held =>
                $"{held.Prefix} holds {string.Join(", ", held.Held.SelectMany(group => group))}")
            .ToList();

        if (mixed.Count == 0)
        {
            yield break;
        }

        yield return new Check("mixed prefixes", Status.Warn,
            string.Join("; ", mixed)
            + ". These plugins come with different prefix setups, so an update keeps the "
            + "prefix's Wine, sync and other shared settings as they are.");
    }
}
