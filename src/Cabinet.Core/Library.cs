namespace Cabinet.Core;

public enum PluginKind
{
    Windows,
    Native,
}

public enum PluginSource
{
    Download,
    Byo,
}

public sealed record LibraryEntry(
    string Id,
    string Name,
    PluginKind Kind,
    string Category,
    string Summary,
    string? Homepage,
    PluginSource Source,
    string? Url,
    string? Sha256,
    string Prefix,
    string? Runner,
    bool Dxvk,
    SyncMode Sync)
{
    public static LibraryEntry Parse(string id, string text)
    {
        var fields = Fields(text);
        var kind = ParseKind(id, Required(id, fields, "Kind"));
        var source = ParseSource(id, Value(fields, "Source") ?? "download");

        if (source == PluginSource.Download && Value(fields, "Url") is null)
        {
            throw new InvalidOperationException($"{id}.yml says Source: download but has no Url");
        }

        if (source == PluginSource.Download && Value(fields, "Sha256") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has no Sha256 — a download nobody checked is not one Cabinet will run");
        }

        if (kind == PluginKind.Native
            && new[] { "Prefix", "Runner", "Dxvk", "Sync" }.FirstOrDefault(fields.ContainsKey)
                is { } windowsOnly)
        {
            throw new InvalidOperationException(
                $"{id}.yml is native and carries {windowsOnly} — a native plugin has no prefix, "
                + "no Wine and no Direct3D to replace");
        }

        return new LibraryEntry(
            id,
            Required(id, fields, "Name"),
            kind,
            Value(fields, "Category") ?? "Plugin",
            Value(fields, "Summary") ?? "",
            Value(fields, "Homepage"),
            source,
            Value(fields, "Url"),
            Value(fields, "Sha256"),
            Value(fields, "Prefix") ?? id,
            Value(fields, "Runner"),
            Value(fields, "Dxvk") is { } dxvk && bool.Parse(dxvk),
            Value(fields, "Sync") is { } sync ? PrefixSettings.ParseSync(sync) : SyncMode.System);
    }

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var at = line.IndexOf(':');

            if (at > 0)
            {
                fields[line[..at].TrimEnd()] = line[(at + 1)..].Trim().Trim('\'', '"');
            }
        }

        return fields;
    }

    private static string? Value(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static string Required(
        string id, IReadOnlyDictionary<string, string> fields, string key) =>
        Value(fields, key) ?? throw new InvalidOperationException($"{id}.yml has no {key}");

    private static PluginKind ParseKind(string id, string word) => word.ToLowerInvariant() switch
    {
        "windows" => PluginKind.Windows,
        "native" => PluginKind.Native,
        _ => throw new InvalidOperationException(
            $"{id}.yml has Kind: {word} — windows or native"),
    };

    private static PluginSource ParseSource(string id, string word) => word.ToLowerInvariant() switch
    {
        "download" => PluginSource.Download,
        "byo" => PluginSource.Byo,
        _ => throw new InvalidOperationException(
            $"{id}.yml has Source: {word} — download or byo"),
    };
}

public sealed class Library(Layout layout, IProcessRunner runner)
{
    private readonly Http http = new(runner);

    public IReadOnlyList<LibraryEntry> Entries()
    {
        if (!Directory.Exists(layout.LibraryDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(layout.LibraryDir, "*.yml")
            .Select(path => LibraryEntry.Parse(
                Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public LibraryEntry Find(string id) =>
        Entries().FirstOrDefault(entry => entry.Id == id)
        ?? throw new InvalidOperationException(
            $"no plugin '{id}' in the library — `cabinet library` lists what there is");

    public IReadOnlyList<string> InstalledNative() =>
        Directory.Exists(layout.NativeDir)
            ? Directory.EnumerateDirectories(layout.NativeDir)
                .Select(path => Path.GetFileName(path))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList()
            : [];

    public void Install(
        LibraryEntry entry,
        string? prefix = null,
        string? installer = null,
        Action<string>? onOutput = null)
    {
        if (entry.Kind == PluginKind.Native)
        {
            if (prefix is not null)
            {
                throw new ArgumentException(
                    $"{entry.Name} is a Linux plugin, so it needs no prefix — your DAW loads it "
                    + "directly", nameof(prefix));
            }

            InstallNative(entry, onOutput);
            return;
        }

        InstallWindows(entry, prefix ?? entry.Prefix, installer, onOutput);
    }

    public void RemoveNative(string id, Action<string>? onOutput = null)
    {
        if (Entries().FirstOrDefault(entry => entry.Id == id) is { Kind: PluginKind.Windows } windows)
        {
            throw new InvalidOperationException(
                $"{windows.Name} runs under Wine, so it lives in a prefix — "
                + $"`cabinet delete {windows.Prefix}` is what removes it");
        }

        var root = Path.GetFullPath(layout.NativePath(id));

        if (Path.GetDirectoryName(root) != layout.NativeDir)
        {
            throw new ArgumentException($"not a plugin id: '{id}'", nameof(id));
        }

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"{id} is not installed");
        }

        foreach (var link in LinksInto(root).ToList())
        {
            File.Delete(link);
            onOutput?.Invoke($"  unlinked {Path.GetFileName(link)}");
        }

        Directory.Delete(root, recursive: true);
        onOutput?.Invoke($"{id} and everything it linked are gone.");
    }

    private void InstallWindows(
        LibraryEntry entry, string prefix, string? installer, Action<string>? onOutput)
    {
        if (entry.Source == PluginSource.Byo && installer is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} cannot be downloaded — pass the installer you already have: "
                + $"`cabinet library install {entry.Id} {prefix} <installer.exe>`");
        }

        var prefixes = new Prefixes(layout, runner);
        var existing = prefixes.List().FirstOrDefault(one => one.Name == prefix);

        if (existing is not null && entry.Runner is { } wanted && !Answers(existing.Runner, wanted))
        {
            onOutput?.Invoke(
                $"{prefix} keeps {existing.Runner}; {entry.Name} would rather have Wine {wanted}.");
        }

        prefixes.Create(
            prefix,
            existing is null && entry.Runner is { } spec ? EnsureRunner(spec, onOutput) : null,
            onOutput);

        var staging = Path.Combine(Path.GetTempPath(), "cabinet-library");

        try
        {
            var chosen = installer ?? Fetch(entry, staging, onOutput);
            var result = prefixes.Install(prefix, chosen, onOutput);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"the {entry.Name} installer exited with {result.ExitCode}");
            }
        }
        finally
        {
            Discard(staging);
        }

        var dxvk = new Dxvk(layout, runner);

        if (entry.Dxvk && dxvk.InstalledIn(prefix) is null)
        {
            dxvk.Install(prefix, onOutput);
        }

        if (existing is null && entry.Sync != SyncMode.System)
        {
            new PrefixSettings(layout).SetSync(prefix, entry.Sync);
            onOutput?.Invoke($"Sync mode {PrefixSettings.Word(entry.Sync)}.");
        }

        Bridge(prefixes, onOutput);
    }

    private void InstallNative(LibraryEntry entry, Action<string>? onOutput)
    {
        var root = layout.NativePath(entry.Id);

        if (Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"{entry.Name} is installed already — `cabinet library remove {entry.Id}` first");
        }

        var staging = Path.Combine(Path.GetTempPath(), "cabinet-library");

        try
        {
            var archive = Fetch(entry, staging, onOutput);
            Directory.CreateDirectory(root);
            Unpack(archive, root, onOutput);
            Link(entry, root, onOutput);
        }
        catch
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            throw;
        }
        finally
        {
            Discard(staging);
        }
    }

    private string EnsureRunner(string spec, Action<string>? onOutput)
    {
        var runners = new Runners(layout, runner);

        if (runners.List().FirstOrDefault(one => Answers(one.Name, spec)) is { } already)
        {
            return already.Name;
        }

        onOutput?.Invoke($"Fetching Wine {spec}, which this plugin's editor needs.");
        return runners.Install(new RunnerIndex(runner).Find(spec), onOutput).Name;
    }

    private static bool Answers(string name, string spec) =>
        name == spec
        || RunnerIndex.Families.Any(family => Runners.DeriveName(family.AssetFor(spec)) == name);

    private string Fetch(LibraryEntry entry, string staging, Action<string>? onOutput)
    {
        var url = entry.Url!;
        var target = Path.Combine(staging, url[(url.LastIndexOf('/') + 1)..]);

        http.ToFile(url, target, onOutput);
        onOutput?.Invoke($"Checking sha256 {entry.Sha256![..12]}…");
        Checksum.Expect(target, entry.Sha256!);

        return target;
    }

    private void Unpack(string archive, string root, Action<string>? onOutput)
    {
        onOutput?.Invoke($"Unpacking {Path.GetFileName(archive)}");

        var result = archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? runner.Run("unzip", ["-q", "-o", archive, "-d", root], onOutput: onOutput)
            : runner.Run("tar", ["-xf", archive, "-C", root], onOutput: onOutput);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"could not unpack {Path.GetFileName(archive)}");
        }
    }

    private void Link(LibraryEntry entry, string root, Action<string>? onOutput)
    {
        var linked = 0;

        foreach (var bundle in Bundles(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var directory = layout.ScanDir(Path.GetExtension(bundle));
            Directory.CreateDirectory(directory);

            var link = Path.Combine(directory, Path.GetFileName(bundle));

            if (new FileInfo(link).LinkTarget is not null)
            {
                File.Delete(link);
            }
            else if (Path.Exists(link))
            {
                throw new InvalidOperationException(
                    $"{link} is already there and is not one of Cabinet's links — move it aside");
            }

            File.CreateSymbolicLink(link, bundle);
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} → {directory}");
            linked++;
        }

        if (linked == 0)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s archive holds no .vst3, .clap, .lv2 or .so where a DAW "
                + "would find one");
        }
    }

    private IEnumerable<string> LinksInto(string root)
    {
        foreach (var extension in Layout.PluginExtensions)
        {
            var directory = layout.ScanDir(extension);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var link in Directory.EnumerateFileSystemEntries(directory))
            {
                if (new FileInfo(link).LinkTarget is { } target
                    && Path.GetFullPath(target, directory)
                        .StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    yield return link;
                }
            }
        }
    }

    private void Bridge(Prefixes prefixes, Action<string>? onOutput)
    {
        onOutput?.Invoke("Bridging what is installed…");
        var result = new Yabridgectl(layout, runner).SyncPrefixes(prefixes.List());

        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            onOutput?.Invoke(line);
        }

        if (!result.Ok)
        {
            throw new InvalidOperationException($"yabridgectl exited with {result.ExitCode}");
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

    private static void Discard(string staging)
    {
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }
    }
}
