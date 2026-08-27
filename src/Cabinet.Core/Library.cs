namespace Cabinet.Core;

public enum PluginKind
{
    Windows,
    Native,
}

public enum PluginSource
{
    Download,
    Rolling,
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
    string? Account,
    string? Sha256,
    string Prefix,
    string? Runner,
    bool Dxvk,
    SyncMode Sync,
    IReadOnlyDictionary<string, string> Env,
    string? Desktop,
    string? Script,
    string? Launch,
    string? Data,
    string? Developer,
    string? Version,
    string? Licence,
    string? Licensing,
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> Description,
    string Vendor)
{
    public static LibraryEntry Parse(string id, string text, string vendor = "")
    {
        var fields = Fields(text);
        var kind = ParseKind(id, Required(id, fields, "Kind"));
        var source = ParseSource(id, Value(fields, "Source") ?? "download");

        if (source != PluginSource.Byo && Value(fields, "Url") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml says Source: {source.ToString().ToLowerInvariant()} but has no Url");
        }

        if (source == PluginSource.Download && Value(fields, "Sha256") is null)
        {
            throw new InvalidOperationException(
                $"{id}.yml has no Sha256 — a download nobody checked is not one Cabinet will run");
        }

        if (source == PluginSource.Rolling && Value(fields, "Sha256") is not null)
        {
            throw new InvalidOperationException(
                $"{id}.yml is rolling and carries Sha256 — the vendor changes what is behind "
                + "that one URL, so a checksum here could only ever be the build whoever wrote "
                + "the entry happened to download");
        }

        if (kind == PluginKind.Windows && fields.ContainsKey("Data"))
        {
            throw new InvalidOperationException(
                $"{id}.yml is a Windows plugin and carries Data — what it writes stays in its "
                + "prefix");
        }

        if (kind == PluginKind.Native
            && new[] { "Prefix", "Runner", "Dxvk", "Sync", "Env", "Desktop", "Launch" }
                .FirstOrDefault(fields.ContainsKey)
                is { } windowsOnly)
        {
            throw new InvalidOperationException(
                $"{id}.yml is native and carries {windowsOnly} — a native plugin has no prefix, "
                + "no Wine, no Direct3D to replace, no environment to set and nothing to open");
        }

        if (source != PluginSource.Byo && fields.ContainsKey("Account"))
        {
            throw new InvalidOperationException(
                $"{id}.yml carries Account but Cabinet downloads it — a login page is only of "
                + "use where the file has to come from you");
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
            Value(fields, "Account"),
            Value(fields, "Sha256"),
            Value(fields, "Prefix") ?? id,
            Value(fields, "Runner"),
            Value(fields, "Dxvk") is { } dxvk && bool.Parse(dxvk),
            Value(fields, "Sync") is { } sync ? PrefixSettings.ParseSync(sync) : SyncMode.System,
            ParseEnv(id, Value(fields, "Env")),
            Value(fields, "Desktop") is { } desktop ? VirtualDesktop.ParseSize(desktop) : null,
            Value(fields, "Script") is { } script ? ParseScript(id, script) : null,
            Value(fields, "Launch") is { } launch ? ParseLaunch(id, launch) : null,
            Value(fields, "Data") is { } data ? ParseData(id, data) : null,
            Value(fields, "Developer"),
            Value(fields, "Version"),
            Value(fields, "Licence"),
            Sentence(Value(fields, "Licensing")),
            Split(Value(fields, "Formats")),
            Paragraphs(Value(fields, "Description")),
            vendor);
    }

    private static string ParseScript(string id, string name)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".sh", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Script: {name} — the name of a .sh file this build ships, not a "
                + "path");
        }

        return name;
    }

    private static string ParseLaunch(string id, string path)
    {
        if (path is not [var drive, ':', '\\', ..] || !char.IsAsciiLetter(drive))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Launch: {path} — the Windows path of an executable its own "
                + @"installer leaves in the prefix, such as C:\Program Files\Thing\Thing.exe");
        }

        return path;
    }

    private static IReadOnlyDictionary<string, string> ParseEnv(string id, string? text)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = line.IndexOf('=');

            if (at < 1 || line[..at].Trim() is not { Length: > 0 } key)
            {
                throw new InvalidOperationException(
                    $"{id}.yml has Env: {line.Trim()} — one KEY=VALUE a line, such as "
                    + "WINEDLLOVERRIDES=wbemprox=n");
            }

            if (PrefixSettings.Owned.Contains(key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{id}.yml sets {key}, which Cabinet sets itself — the shim drops it, so "
                    + "the entry would only look as though it took effect");
            }

            found[key] = line[(at + 1)..];
        }

        return found;
    }

    private static string ParseData(string id, string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (Path.IsPathRooted(relative)
            || parts.Length < 2
            || parts.Contains("..")
            || Layout.ScanDirectories.Contains(parts[0], StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{id}.yml has Data: {relative} — a directory of the plugin's own under your "
                + "home, such as .u-he/Podolski");
        }

        return string.Join('/', parts);
    }

    private static string? Sentence(string? value) =>
        value is null
            ? null
            : string.Join(' ', value.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static IReadOnlyList<string> Split(string? value) =>
        value is null
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> Paragraphs(string? value) =>
        value is null
            ? []
            : value.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => string.Join(' ', paragraph.Split(
                    '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                .Where(paragraph => paragraph.Length > 0)
                .ToList();

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var indented = new List<string>();
        string? block = null;

        void Close()
        {
            if (block is not null)
            {
                fields[block] = string.Join('\n', indented).Trim();
                indented.Clear();
                block = null;
            }
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (block is not null && (line.Length == 0 || char.IsWhiteSpace(raw[0])))
            {
                indented.Add(line);
                continue;
            }

            Close();

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var at = line.IndexOf(':');

            if (at <= 0)
            {
                continue;
            }

            var value = line[(at + 1)..].Trim().Trim('\'', '"');

            if (value.Length == 0)
            {
                block = line[..at].TrimEnd();
            }
            else
            {
                fields[line[..at].TrimEnd()] = value;
            }
        }

        Close();

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
        "rolling" => PluginSource.Rolling,
        "byo" => PluginSource.Byo,
        _ => throw new InvalidOperationException(
            $"{id}.yml has Source: {word} — download, rolling or byo"),
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

        var entries = Directory.EnumerateDirectories(layout.LibraryDir)
            .SelectMany(vendor => Directory.EnumerateFiles(vendor, "*.yml")
                .Select(path => LibraryEntry.Parse(
                    Path.GetFileNameWithoutExtension(path),
                    File.ReadAllText(path),
                    Path.GetFileName(vendor))))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .FirstOrDefault(same => same.Count() > 1) is { } clash)
        {
            throw new InvalidOperationException(
                $"two vendors both ship {clash.Key}.yml — "
                + $"{string.Join(" and ", clash.Select(entry => entry.Vendor))}");
        }

        return entries;
    }

    public LibraryEntry Find(string id) =>
        Entries().FirstOrDefault(entry => entry.Id == id)
        ?? throw new InvalidOperationException(
            $"no plugin '{id}' in the library — `cabinet library` lists what there is");

    public IReadOnlyDictionary<string, string?> Installed()
    {
        var installed = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var prefix in new Prefixes(layout, runner).List())
        {
            foreach (var id in Recorded(prefix.Name))
            {
                installed[id] = prefix.Name;
            }
        }

        if (Directory.Exists(layout.NativeDir))
        {
            foreach (var path in Directory.EnumerateDirectories(layout.NativeDir))
            {
                installed[Path.GetFileName(path)] = null;
            }
        }

        return installed;
    }

    public void Install(
        LibraryEntry entry,
        string? prefix = null,
        string? installer = null,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null)
    {
        if (entry.Kind == PluginKind.Native)
        {
            if (prefix is not null)
            {
                throw new ArgumentException(
                    $"{entry.Name} is a Linux plugin, so it needs no prefix — your DAW loads it "
                    + "directly", nameof(prefix));
            }

            InstallNative(entry, installer, onOutput, onProgress);
            return;
        }

        InstallWindows(entry, prefix ?? entry.Prefix, installer, onOutput, onProgress);
    }

    public IReadOnlyList<UninstallEntry> Uninstallers(string prefix) =>
        new PrefixRegistry(layout).Uninstallers(prefix);

    private IReadOnlyList<UninstallEntry> Candidates(string prefix, LibraryEntry entry)
    {
        var attributed = Lines(prefix)
            .Where(fields => fields[0] != entry.Id)
            .SelectMany(fields => fields.Skip(1))
            .ToHashSet(StringComparer.Ordinal);

        var plausible = Uninstallers(prefix)
            .Where(one => !attributed.Contains(one.Key) && !IsWine(one.Name))
            .ToList();

        return plausible.Where(one => Resembles(one.Name, entry.Name)).ToList() is { Count: > 0 } named
            ? named
            : plausible;
    }

    private static bool IsWine(string name) =>
        name.StartsWith("Wine ", StringComparison.OrdinalIgnoreCase);

    private static bool Resembles(string uninstaller, string name) =>
        Squashed(uninstaller).Contains(Squashed(name), StringComparison.Ordinal);

    private static string Squashed(string text) =>
        new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    public IReadOnlyList<string> Sharing(string prefix, string id) =>
        [.. Recorded(prefix).Where(other => other != id)];

    private static readonly TimeSpan Glance = TimeSpan.FromSeconds(15);

    public void Launch(
        LibraryEntry entry, string? prefix = null, Action<string>? onOutput = null)
    {
        if (entry.Launch is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is a plugin, not an application Cabinet can open");
        }

        var where = prefix ?? entry.Prefix;

        if (!Recorded(where).Contains(entry.Id, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{entry.Name} is not installed in {where}");
        }

        var prefixes = new Prefixes(layout, runner);
        var log = layout.PrefixLaunchLog(where);
        var watch = new PluginWatch(Bundled(where));
        using var closed = new CancellationTokenSource();

        void Say(string line)
        {
            File.AppendAllText(log, line + Environment.NewLine);
            onOutput?.Invoke(line);
        }

        File.WriteAllText(log, "");
        Say($"Opening {entry.Name}. What it installs is bridged as it lands.");

        var watching = Task.Run(() =>
        {
            while (!closed.Token.WaitHandle.WaitOne(Glance))
            {
                Bridge(prefixes, watch.Appeared(Bundled(where)), Say);
            }
        });

        ProcessResult ran;

        try
        {
            ran = prefixes.Run(where, "wine", [entry.Launch], logTo: log);

            var settled = prefixes.Run(where, "wineserver", ["-w"], logTo: log);

            if (!settled.Ok)
            {
                throw new InvalidOperationException(
                    $"Wine processes did not finish for '{entry.Name}' (exit code {settled.ExitCode})");
            }
        }
        finally
        {
            closed.Cancel();
            watching.Wait();
        }

        Bridge(prefixes, watch.Closed(Bundled(where)), Say);

        if (!ran.Ok)
        {
            foreach (var line in Tail(log))
            {
                onOutput?.Invoke(line);
            }

            throw new InvalidOperationException(
                $"{entry.Name} exited with {ran.ExitCode}");
        }

        Say($"{entry.Name} closed.");
    }

    public string? LaunchLog(LibraryEntry entry, string? prefix = null) =>
        layout.PrefixLaunchLog(prefix ?? entry.Prefix) is { } log && File.Exists(log)
            ? File.ReadAllText(log) is { Length: > 0 } text ? text : null
            : null;

    private static IEnumerable<string> Tail(string log) =>
        File.Exists(log)
            ? File.ReadLines(log)
                .Where(line => line.Trim().Length > 0)
                .TakeLast(20)
            : [];

    private void Bridge(
        Prefixes prefixes, IReadOnlyList<string>? appeared, Action<string>? onOutput)
    {
        if (appeared is null)
        {
            return;
        }

        foreach (var bundle in appeared)
        {
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} appeared");
        }

        Bridge(prefixes, onOutput);
    }

    public void Remove(
        LibraryEntry entry,
        string? prefix = null,
        bool takePrefix = false,
        Action<string>? onOutput = null)
    {
        if (entry.Kind == PluginKind.Native)
        {
            if (prefix is not null)
            {
                throw new ArgumentException(
                    $"{entry.Name} is a Linux plugin, so it is in no prefix", nameof(prefix));
            }

            RemoveNative(entry, onOutput);
            return;
        }

        var where = prefix ?? entry.Prefix;

        if (takePrefix)
        {
            new Prefixes(layout, runner).Delete(where, onOutput);
            onOutput?.Invoke($"{entry.Name} and the prefix that held it are gone.");
            return;
        }

        RemoveWindows(entry, where, onOutput);
    }

    private void RemoveWindows(LibraryEntry entry, string prefix, Action<string>? onOutput)
    {
        if (!Directory.Exists(layout.PrefixPath(prefix)))
        {
            throw new DirectoryNotFoundException($"no such prefix: {prefix}");
        }

        if (!Recorded(prefix).Contains(entry.Id, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{entry.Name} is not installed in {prefix}");
        }

        var recorded = RecordedKeys(prefix, entry.Id).ToList();
        var chosen = recorded.Count > 0
            ? Uninstallers(prefix)
                .Where(one => recorded.Contains(one.Key, StringComparer.Ordinal))
                .ToList()
            : Candidates(prefix, entry);

        if (chosen.Count == 0)
        {
            throw new InvalidOperationException(NotFound(entry, prefix));
        }

        var before = Bundled(prefix);
        var prefixes = new Prefixes(layout, runner);

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

    public static string NotFound(LibraryEntry entry, string prefix) =>
        $"Nothing in prefix {prefix} looks like {entry.Name}'s uninstaller, so there is no way "
        + $"to take it out on its own — `cabinet delete {prefix}` removes the prefix and "
        + "everything in it";

    private IEnumerable<string> Registered(string prefix) =>
        Uninstallers(prefix).Select(one => one.Key).ToList();

    private IReadOnlySet<string> Bundled(string prefix) =>
        layout.PrefixPluginDirs(prefix)
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFileSystemEntries)
            .ToHashSet(StringComparer.Ordinal);

    private void RemoveNative(LibraryEntry entry, Action<string>? onOutput)
    {
        var id = entry.Id;
        var root = Path.GetFullPath(layout.NativePath(id));

        if (Path.GetDirectoryName(root) != layout.NativeDir)
        {
            throw new ArgumentException($"not a plugin id: '{id}'", nameof(entry));
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

        if (entry.Data is { } relative)
        {
            var data = layout.DataPath(relative);

            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
                onOutput?.Invoke($"  removed {data}");
            }
        }

        onOutput?.Invoke($"{id} and everything it linked are gone.");
    }

    private void InstallWindows(
        LibraryEntry entry,
        string prefix,
        string? installer,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        if (entry.Source == PluginSource.Byo && installer is null)
        {
            throw new InvalidOperationException(BringYourOwn(entry, prefix));
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
            existing is null && entry.Runner is { } spec
                ? EnsureRunner(spec, onOutput, onProgress)
                : null,
            onOutput);

        if (existing is null && entry.Env.Count > 0)
        {
            var settings = new PrefixSettings(layout);

            foreach (var (key, value) in entry.Env)
            {
                settings.SetVariable(prefix, key, value);
            }

            onOutput?.Invoke($"Set {string.Join(", ", entry.Env.Keys)}.");
        }

        var staging = Path.Combine(Path.GetTempPath(), "cabinet-library");

        try
        {
            var chosen = installer ?? Fetch(entry, staging, onOutput, onProgress);
            var before = Registered(prefix);

            if (entry.Script is null)
            {
                var result = prefixes.Install(prefix, chosen, onOutput);

                if (!result.Ok)
                {
                    throw new InvalidOperationException(
                        $"the {entry.Name} installer exited with {result.ExitCode}");
                }
            }
            else
            {
                new InstallScript(layout, runner).Run(
                    entry,
                    chosen,
                    staging,
                    layout.PrefixPath(prefix),
                    prefixes.Variables(prefix),
                    onOutput);
            }

            var appeared = Registered(prefix).Except(before, StringComparer.Ordinal).ToList();

            Record(
                prefix,
                entry.Id,
                appeared.Count > 0 ? appeared : RecordedKeys(prefix, entry.Id));
        }
        finally
        {
            Discard(staging);
        }

        var dxvk = new Dxvk(layout, runner);

        if (entry.Dxvk && dxvk.InstalledIn(prefix) is null)
        {
            dxvk.Install(prefix, onOutput, onProgress);
        }

        var desktop = new VirtualDesktop(layout, runner);

        if (entry.Desktop is { } size && desktop.SizeIn(prefix) is null)
        {
            desktop.Set(prefix, size, onOutput);
        }

        if (existing is null && entry.Sync != SyncMode.System)
        {
            new PrefixSettings(layout).SetSync(prefix, entry.Sync);
            onOutput?.Invoke($"Sync mode {PrefixSettings.Word(entry.Sync)}.");
        }

        Bridge(prefixes, onOutput);
    }

    private void InstallNative(
        LibraryEntry entry,
        string? supplied,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        if (entry.Source == PluginSource.Byo && supplied is null)
        {
            throw new InvalidOperationException(BringYourOwn(entry));
        }

        if (supplied is not null && !File.Exists(supplied))
        {
            throw new FileNotFoundException($"no such file: {supplied}", supplied);
        }

        var root = layout.NativePath(entry.Id);

        if (Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"{entry.Name} is installed already — `cabinet library remove {entry.Id}` first");
        }

        var data = entry.Data is { } relative ? layout.DataPath(relative) : null;

        if (data is not null && Directory.Exists(data))
        {
            throw new InvalidOperationException(
                $"{data} is already there — {entry.Name} keeps its presets in it, so move it "
                + "aside first");
        }

        var staging = Path.Combine(Path.GetTempPath(), "cabinet-library");

        try
        {
            var archive = supplied ?? Fetch(entry, staging, onOutput, onProgress);
            Directory.CreateDirectory(root);

            if (data is not null)
            {
                Directory.CreateDirectory(data);
                onOutput?.Invoke($"Its presets and resources go in {data}.");
            }

            Lay(entry, archive, root, data, staging, onOutput);
            Link(entry, root, onOutput);
        }
        catch
        {
            Discard(root);

            if (data is not null)
            {
                Discard(data);
            }

            throw;
        }
        finally
        {
            Discard(staging);
        }
    }

    private string EnsureRunner(
        string spec, Action<string>? onOutput, Action<double>? onProgress)
    {
        var runners = new Runners(layout, runner);

        if (runners.List().FirstOrDefault(one => Answers(one.Name, spec)) is { } already)
        {
            return already.Name;
        }

        onOutput?.Invoke($"Fetching Wine {spec}, which this plugin's editor needs.");
        return runners.Install(new RunnerIndex(runner).Find(spec), onOutput, onProgress).Name;
    }

    public static bool Answers(string name, string spec) =>
        name == spec
        || RunnerIndex.Families.Any(family => Runners.DeriveName(family.AssetFor(spec)) == name);

    private string Fetch(
        LibraryEntry entry,
        string staging,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        var url = entry.Url!;
        var target = Path.Combine(staging, ArchiveName(entry));

        http.ToFile(url, target, onOutput, onProgress);

        if (entry.Sha256 is { } expected)
        {
            onOutput?.Invoke($"Checking sha256 {expected[..12]}…");
            Checksum.Expect(target, expected);
        }
        else
        {
            onOutput?.Invoke(Unverifiable(url));
        }

        return target;
    }

    public static string ArchiveName(LibraryEntry entry)
    {
        var path = entry.Url!.TrimEnd('/');
        var name = path[(path.LastIndexOf('/') + 1)..];

        return entry.Kind == PluginKind.Windows && !Path.HasExtension(name)
            ? name + ".exe"
            : name;
    }

    public static string Command(LibraryEntry entry, string? prefix = null) =>
        (entry.Source, entry.Kind) switch
        {
            (PluginSource.Byo, PluginKind.Native) => $"cabinet library install {entry.Id} <file>",
            (PluginSource.Byo, _) =>
                $"cabinet library install {entry.Id} {prefix ?? "<prefix>"} <installer.exe>",
            _ => $"cabinet library install {entry.Id}",
        };

    public static string BringYourOwn(LibraryEntry entry, string? prefix = null) =>
        $"{entry.Name} cannot be downloaded — "
        + (entry.Account is { } account
            ? $"log in at {account}, download it, then `{Command(entry, prefix)}`"
            : $"pass the installer you already have: `{Command(entry, prefix)}`");

    public static string Unverifiable(string url) =>
        $"{new Uri(url).Host} publishes no checksum and changes this download with every "
        + "release, so nothing here can verify what arrives — only that it came from the "
        + "vendor over HTTPS.";

    private void Lay(
        LibraryEntry entry,
        string archive,
        string root,
        string? data,
        string staging,
        Action<string>? onOutput)
    {
        if (entry.Script is null)
        {
            Unpack(archive, root, onOutput);
            return;
        }

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CABINET_DEST"] = root,
        };

        if (data is not null)
        {
            variables["CABINET_DATA"] = data;
        }

        new InstallScript(layout, runner).Run(entry, archive, staging, root, variables, onOutput);
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

    private IEnumerable<string[]> Lines(string prefix) =>
        File.Exists(layout.PrefixPluginsFile(prefix))
            ? File.ReadAllLines(layout.PrefixPluginsFile(prefix))
                .Select(line => line.Split(
                    '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(fields => fields.Length > 0)
            : [];

    public IEnumerable<string> Recorded(string prefix) =>
        Lines(prefix).Select(fields => fields[0]);

    private IEnumerable<string> RecordedKeys(string prefix, string id) =>
        Lines(prefix).Where(fields => fields[0] == id).SelectMany(fields => fields.Skip(1));

    private void Record(string prefix, string id, IEnumerable<string> keys)
    {
        var kept = Lines(prefix).Where(fields => fields[0] != id).ToList();
        kept.Add([id, .. keys]);
        Write(prefix, kept);
    }

    private void Forget(string prefix, string id) =>
        Write(prefix, Lines(prefix).Where(fields => fields[0] != id));

    private void Write(string prefix, IEnumerable<string[]> lines) =>
        File.WriteAllLines(
            layout.PrefixPluginsFile(prefix), lines.Select(fields => string.Join('\t', fields)));

    private void Bridge(Prefixes prefixes, Action<string>? onOutput) =>
        new Yabridgectl(layout, runner).Bridge(prefixes.List(), onOutput);

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

    private static void Discard(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
