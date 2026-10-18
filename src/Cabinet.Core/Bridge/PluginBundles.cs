namespace Cabinet.Core;

public static class PluginBundles
{
    private static readonly IReadOnlyList<string> BundleDirectories =
        [".vst3", ".clap", ".vst", ".lv2", ".lxvst"];

    public static IEnumerable<string> In(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root)
                     .Where(entry => !Staging.Owns(Path.GetFileName(entry))))
        {
            if (IsPlugin(entry))
            {
                yield return entry;
                continue;
            }

            if (!Directory.Exists(entry) || IsBundle(entry))
            {
                continue;
            }

            foreach (var nested in Directory.EnumerateFileSystemEntries(entry))
            {
                if (IsPlugin(nested))
                {
                    yield return nested;
                }
            }
        }
    }

    public static string? Repointed(string bundle, IReadOnlyDictionary<string, string> moved)
    {
        if (new FileInfo(bundle).LinkTarget is not { } link)
        {
            return null;
        }

        var target = Path.GetFullPath(link, Path.GetDirectoryName(bundle)!);

        return moved
            .Where(pair => target == pair.Key
                           || target.StartsWith(pair.Key + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(pair => pair.Value + target[pair.Key.Length..])
            .FirstOrDefault();
    }

    private static bool IsPlugin(string path) =>
        Layout.PluginExtensions.Contains(Path.GetExtension(path));

    private static bool IsBundle(string path) =>
        BundleDirectories.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
