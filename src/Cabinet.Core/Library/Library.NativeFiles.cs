namespace Cabinet.Core;

public sealed partial class Library
{
    private void RemoveNative(LibraryEntry entry, Action<string>? onOutput)
    {
        var id = entry.Id;
        var root = layout.NativePath(id);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"{id} is not installed");
        }

        using var installing = Underway.Begin(layout.NativeInstalling(id))
                               ?? throw new InvalidOperationException(
                                   $"Cabinet is installing {entry.Name} right now — wait for "
                                   + "that to finish");

        foreach (var placed in Published(root).ToList())
        {
            Relocation.Delete(placed);
            onOutput?.Invoke($"  removed {placed}");
        }

        foreach (var link in LinksInto(root).ToList())
        {
            File.Delete(link);
            onOutput?.Invoke($"  unlinked {Path.GetFileName(link)}");
        }

        if (entry.Data is { } relative)
        {
            var data = layout.DataPath(relative);

            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
                onOutput?.Invoke($"  removed {data}");
            }
        }

        using (var removing = Staging.Create(layout.NativeDir, "plugin"))
        {
            Directory.Move(root, Path.Combine(removing.Path, id));
        }

        installing.Finish();
        onOutput?.Invoke($"{id} and everything it linked are gone.");
    }

    private static void Relink(LibraryEntry entry, string root, Action<string>? onOutput)
    {
        if (entry.Relink.Count == 0)
        {
            return;
        }

        var wanted = entry.Relink.OrderBy(one => one.Key, StringComparer.Ordinal).ToList();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(file => new FileInfo(file).LinkTarget is null)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            foreach (var (soname, replacement) in wanted)
            {
                if (Elf.Relink(file, soname, replacement))
                {
                    onOutput?.Invoke(
                        $"  {Path.GetRelativePath(root, file)}: {soname} → {replacement}");
                }
            }
        }
    }

    private void Publish(
        LibraryEntry entry,
        string root,
        List<(string Placed, string? Replaced)> made,
        Action<string>? onOutput)
    {
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var bundle in Bundles(root)
                     .OrderBy(bundle => new FileInfo(bundle).LinkTarget is not null)
                     .ThenBy(bundle => bundle, StringComparer.Ordinal)
                     .ToList())
        {
            var placed = layout.NativePlacement(bundle);
            Directory.CreateDirectory(Path.GetDirectoryName(placed)!);
            var replaced = new FileInfo(placed).LinkTarget;

            if (replaced is null
                    ? Path.Exists(placed)
                    : !Inside(placed, replaced, root)
                      && !(Inside(placed, replaced, layout.NativeDir) && !Path.Exists(placed)))
            {
                throw new InvalidOperationException(
                    replaced is not null && Inside(placed, replaced, layout.NativeDir)
                        ? $"{placed} is another Cabinet plugin's — remove that one first"
                        : $"{placed} is already there and is not one of Cabinet's plugins — move it aside");
            }

            if (replaced is not null)
            {
                File.Delete(placed);
            }

            if (Repointed(bundle, moved) is { } target)
            {
                File.CreateSymbolicLink(placed, target);
                made.Add((placed, replaced));
                File.Delete(bundle);
            }
            else
            {
                Relocation.Move(bundle, placed);
                made.Add((placed, replaced));
                moved[bundle] = placed;
            }

            File.CreateSymbolicLink(bundle, placed);
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} → {Path.GetDirectoryName(placed)}");
        }

        if (made.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s archive holds no .vst3, .clap, .lv2 or .so where a DAW "
                + "would find one");
        }
    }

    internal static string? Repointed(string bundle, IReadOnlyDictionary<string, string> moved)
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

    private static void Unpublish(IEnumerable<(string Placed, string? Replaced)> made)
    {
        foreach (var (placed, replaced) in made.Reverse())
        {
            Relocation.Delete(placed);

            if (replaced is not null)
            {
                File.CreateSymbolicLink(placed, replaced);
            }
        }
    }

    private IEnumerable<string> Published(string root) =>
        Bundles(root)
            .Where(bundle => new FileInfo(bundle).LinkTarget is { } target
                             && Path.GetFullPath(target, Path.GetDirectoryName(bundle)!)
                             == layout.NativePlacement(bundle))
            .Select(layout.NativePlacement);

    private static bool Inside(string link, string target, string directory) =>
        Path.GetFullPath(target, Path.GetDirectoryName(link)!)
            .StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private IEnumerable<string> LinksInto(string root)
    {
        foreach (var extension in Layout.PluginExtensions)
        {
            var directory = layout.NativeScanDir(extension);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var link in Directory.EnumerateFileSystemEntries(directory))
            {
                if (new FileInfo(link).LinkTarget is { } target && Inside(link, target, root))
                {
                    yield return link;
                }
            }
        }
    }

    private static readonly IReadOnlyList<string> BundleDirectories =
        [".vst3", ".clap", ".vst", ".lv2", ".lxvst"];

    internal static IEnumerable<string> Bundles(string root)
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

    private static bool IsPlugin(string path) =>
        Layout.PluginExtensions.Contains(Path.GetExtension(path));

    private static bool IsBundle(string path) =>
        BundleDirectories.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private IReadOnlySet<string> Bundled(string prefix) =>
        layout.PrefixPluginDirs(prefix)
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFileSystemEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static void Discard(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
