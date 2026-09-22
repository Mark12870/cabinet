namespace Cabinet.Core;

public enum RemovalKind
{
    Native,
    TakesPrefix,
    PluginOrPrefix,
    KeepsPrefix,
}

public sealed record Removal(
    LibraryEntry Entry, RemovalKind Kind, string? Prefix, IReadOnlyList<string> Sharing)
{
    public bool Agrees(Removal other) =>
        other.Entry.Id == Entry.Id
        && other.Kind == Kind
        && other.Prefix == Prefix
        && other.Sharing.ToHashSet(StringComparer.Ordinal).SetEquals(Sharing);
}

public sealed partial class Library
{
    public IReadOnlyList<UninstallEntry> Uninstallers(string prefix) =>
        new PrefixRegistry(layout, runner).Uninstallers(prefix);

    public IReadOnlyList<UninstallEntry> PossibleUninstallers(Removal removal) =>
        removal.Prefix is { } prefix && !RecordedKeys(prefix, removal.Entry.Id).Any()
            ? Candidates(prefix, removal.Entry)
            : [];

    private IReadOnlyList<UninstallEntry> Candidates(string prefix, LibraryEntry entry)
    {
        var attributed = Lines(prefix)
            .Where(fields => fields[0] != entry.Id)
            .SelectMany(fields => fields.Skip(1))
            .ToHashSet(StringComparer.Ordinal);

        return Uninstallers(prefix)
            .Where(one => !attributed.Contains(one.Key)
                          && !IsWine(one.Name)
                          && Names(one.Name, entry.Name))
            .ToList();
    }

    private static bool IsWine(string name) =>
        name.StartsWith("Wine ", StringComparison.OrdinalIgnoreCase);

    private static bool Names(string uninstaller, string name)
    {
        var words = Words(uninstaller);
        var wanted = string.Concat(Words(name));

        for (var first = 0; first < words.Count; first++)
        {
            var joined = "";

            for (var last = first; last < words.Count && joined.Length < wanted.Length; last++)
            {
                joined += words[last];

                if (joined == wanted)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> Words(string text) =>
        new string([.. text.Select(character =>
                char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public IReadOnlyList<string> Sharing(string prefix, string id) =>
        [.. Recorded(prefix).Where(other => other != id)];

    public Removal RemovalOf(LibraryEntry entry)
    {
        if (entry.Kind == PluginKind.Native)
        {
            return Directory.Exists(layout.NativePath(entry.Id))
                ? new Removal(entry, RemovalKind.Native, null, [])
                : throw NotInstalled(entry);
        }

        var where = Where(entry);
        var sharing = Sharing(where, entry.Id);

        return new Removal(
            entry,
            entry.Manager ? RemovalKind.TakesPrefix
            : sharing.Count > 0 ? RemovalKind.KeepsPrefix
            : RemovalKind.PluginOrPrefix,
            where,
            sharing);
    }

    public void Remove(
        Removal agreed,
        bool takePrefix = false,
        UninstallEntry? uninstaller = null,
        Action<string>? onOutput = null)
    {
        var entry = agreed.Entry;
        using var claim = agreed.Prefix is { } held
            ? new Prefixes(layout, runner).Claim(held, $"take {entry.Name} out of {held}")
            : null;
        var current = RemovalOf(entry);

        if (!current.Agrees(agreed))
        {
            throw new InvalidOperationException(
                $"{entry.Name} or the prefix holding it changed since you were asked, so nothing "
                + "was removed — look again before removing it");
        }

        switch (current.Kind)
        {
            case RemovalKind.Native:
                RemoveNative(entry, onOutput);
                return;
            case RemovalKind.TakesPrefix when !takePrefix:
                throw new InvalidOperationException(
                    $"{entry.Name}'s own uninstaller leaves everything it downloaded behind, so "
                    + "it goes only with its prefix");
            case RemovalKind.KeepsPrefix when takePrefix:
                throw new InvalidOperationException(
                    $"{current.Prefix} also holds {string.Join(" and ", current.Sharing)}, so "
                    + $"it stays when {entry.Name} goes");
        }

        if (takePrefix)
        {
            new Prefixes(layout, runner).Delete(current.Prefix!, onOutput);
            onOutput?.Invoke($"{entry.Name} and the prefix that held it are gone.");
            return;
        }

        RemoveWindows(entry, current.Prefix!, uninstaller, onOutput);
    }

    private void RemoveWindows(
        LibraryEntry entry, string prefix, UninstallEntry? uninstaller, Action<string>? onOutput)
    {
        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"take {entry.Name} out of {prefix}");
        var recorded = RecordedKeys(prefix, entry.Id).ToList();
        var chosen = recorded.Count > 0
            ? Uninstallers(prefix)
                .Where(one => recorded.Contains(one.Key, StringComparer.Ordinal))
                .ToList()
            : Chosen(entry, prefix, Candidates(prefix, entry), uninstaller);

        if (chosen.Count == 0)
        {
            throw new InvalidOperationException(NotFound(entry, prefix));
        }

        var before = Bundled(prefix);

        foreach (var one in chosen)
        {
            onOutput?.Invoke($"Uninstalling {one.Name}…");
            Uninstall(prefixes, prefix, one.Command, onOutput);
        }

        var gone = before.Except(Bundled(prefix), StringComparer.Ordinal).ToList();

        if (gone.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s uninstaller left every plugin in {prefix} where it was, so "
                + "nothing has been removed — a cancelled uninstaller looks exactly like this");
        }

        foreach (var bundle in gone.OrderBy(path => path, StringComparer.Ordinal))
        {
            onOutput?.Invoke($"  removed {Path.GetFileName(bundle)}");
        }

        Forget(prefix, entry.Id);
        Bridge(prefixes, onOutput);
        onOutput?.Invoke($"{entry.Name} is gone from {prefix}, which stays.");
    }

    private const string Batch = "cabinet-uninstall.bat";

    private void Uninstall(
        Prefixes prefixes, string prefix, string command, Action<string>? onOutput)
    {
        var script = Path.Combine(layout.PrefixPath(prefix), "drive_c", Batch);
        File.WriteAllText(script, command + "\r\n");

        try
        {
            prefixes.Run(prefix, "wine", ["cmd", "/c", @"C:\" + Batch], onOutput);
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static IReadOnlyList<UninstallEntry> Chosen(
        LibraryEntry entry,
        string prefix,
        IReadOnlyList<UninstallEntry> possible,
        UninstallEntry? uninstaller)
    {
        if (uninstaller is not null)
        {
            return possible.FirstOrDefault(one => one.Key == uninstaller.Key) is { } current
                ? [current]
                : throw new InvalidOperationException(
                    $"{uninstaller.Name} is not an uninstaller that could be {entry.Name}'s");
        }

        return possible.Count > 1
            ? throw new InvalidOperationException(
                $"{string.Join(" and ", possible.Select(one => one.Name))} could each be "
                + $"{entry.Name}'s uninstaller in {prefix}, so Cabinet will not guess — choose one")
            : possible;
    }

    public static string NotFound(LibraryEntry entry, string prefix) =>
        $"Nothing in prefix {prefix} looks like {entry.Name}'s uninstaller, so there is no way "
        + $"to take it out on its own — deleting {prefix} removes it with everything in the "
        + "prefix";

    private IEnumerable<string> Registered(string prefix) =>
        Uninstallers(prefix).Select(one => one.Key).ToList();

    private void Bridge(Prefixes prefixes, Action<string>? onOutput) =>
        new Yabridgectl(layout, runner).Bridge(prefixes.List(), onOutput);
}
