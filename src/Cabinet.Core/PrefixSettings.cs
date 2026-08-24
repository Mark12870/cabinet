namespace Cabinet.Core;

public enum SyncMode
{
    System,
    Esync,
    Fsync,
    Ntsync,
}

public sealed class PrefixSettings(Layout layout)
{
    public static readonly IReadOnlyList<SyncMode> SyncModes =
        [SyncMode.System, SyncMode.Esync, SyncMode.Fsync, SyncMode.Ntsync];

    public static readonly IReadOnlyList<string> Owned =
        ["WINEPREFIX", "WINELOADER", "WINEDLLPATH", "YABRIDGE_TEMP_DIR"];

    public static string Word(SyncMode mode) => mode.ToString().ToLowerInvariant();

    public static SyncMode ParseSync(string word)
    {
        foreach (var mode in SyncModes)
        {
            if (Word(mode) == word.Trim().ToLowerInvariant())
            {
                return mode;
            }
        }

        throw new ArgumentException(
            $"not a sync mode: '{word}' — one of {string.Join(", ", SyncModes.Select(Word))}");
    }

    public static IReadOnlyDictionary<string, string> SyncVariables(SyncMode mode) =>
        mode == SyncMode.System
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["WINEESYNC"] = mode == SyncMode.Esync ? "1" : "0",
                ["WINEFSYNC"] = mode == SyncMode.Fsync ? "1" : "0",
                ["WINENTSYNC"] = mode == SyncMode.Ntsync ? "1" : "0",
            };

    public SyncMode Sync(string prefix)
    {
        var marker = layout.PrefixSyncFile(prefix);

        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() is not { Length: > 0 } recorded)
        {
            return SyncMode.System;
        }

        return SyncModes.FirstOrDefault(mode => Word(mode) == recorded, SyncMode.System);
    }

    public void SetSync(string prefix, SyncMode mode)
    {
        Ensure(prefix);
        var marker = layout.PrefixSyncFile(prefix);

        if (mode == SyncMode.System)
        {
            File.Delete(marker);
            return;
        }

        File.WriteAllText(marker, Word(mode) + Environment.NewLine);
    }

    public IReadOnlyDictionary<string, string> Variables(string prefix)
    {
        var file = layout.PrefixEnvFile(prefix);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!File.Exists(file))
        {
            return found;
        }

        foreach (var line in File.ReadAllLines(file))
        {
            var text = line.Trim();
            var at = text.IndexOf('=');

            if (at > 0 && !text.StartsWith('#'))
            {
                found[text[..at].TrimEnd()] = text[(at + 1)..];
            }
        }

        return found;
    }

    public void SetVariable(string prefix, string key, string? value)
    {
        Ensure(prefix);

        if (key.Trim() is not { Length: > 0 } name || name.Contains('=') || name.StartsWith('#'))
        {
            throw new ArgumentException($"not a variable name: '{key}'", nameof(key));
        }

        if (Owned.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException($"{name} is Cabinet's to set, not yours", nameof(key));
        }

        var kept = new Dictionary<string, string>(Variables(prefix), StringComparer.Ordinal);

        if (value is null)
        {
            kept.Remove(name);
        }
        else
        {
            kept[name] = value;
        }

        var file = layout.PrefixEnvFile(prefix);

        if (kept.Count == 0)
        {
            File.Delete(file);
            return;
        }

        File.WriteAllLines(
            file,
            kept.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value}"));
    }

    private void Ensure(string prefix)
    {
        if (!Directory.Exists(layout.PrefixPath(prefix)))
        {
            throw new DirectoryNotFoundException($"no such prefix: {prefix}");
        }
    }
}
