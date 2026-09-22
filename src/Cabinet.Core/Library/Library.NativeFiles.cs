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

    private void Link(
        LibraryEntry entry,
        string root,
        List<(string Link, string? Replaced)> made,
        Action<string>? onOutput)
    {
        foreach (var bundle in Bundles(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var directory = layout.NativeScanDir(Path.GetExtension(bundle));
            Directory.CreateDirectory(directory);

            var link = Path.Combine(directory, Path.GetFileName(bundle));
            var replaced = new FileInfo(link).LinkTarget;

            if (replaced is null
                    ? Path.Exists(link)
                    : !Inside(link, replaced, root)
                      && !(Inside(link, replaced, layout.NativeDir) && !Path.Exists(link)))
            {
                throw new InvalidOperationException(
                    $"{link} is already there and is not one of Cabinet's links — move it aside");
            }

            made.Add((link, replaced));

            if (replaced is not null)
            {
                File.Delete(link);
            }

            File.CreateSymbolicLink(link, bundle);
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} → {directory}");
        }

        if (made.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s archive holds no .vst3, .clap, .lv2 or .so where a DAW "
                + "would find one");
        }
    }

    private static void Unlink(IEnumerable<(string Link, string? Replaced)> made)
    {
        foreach (var (link, replaced) in made.Reverse())
        {
            File.Delete(link);

            if (replaced is not null)
            {
                File.CreateSymbolicLink(link, replaced);
            }
        }
    }

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

    private static IEnumerable<string> Bundles(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
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
