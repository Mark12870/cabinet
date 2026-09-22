namespace Cabinet.Core;

internal sealed record Pending(string Id, bool Created, IReadOnlyList<string> Keys)
{
    private const string Made = "created";
    private const string Found = "found";

    public static Pending? Parse(string? text) =>
        text?.Split('\t', StringSplitOptions.TrimEntries) is [{ Length: > 0 } id, var made, .. var keys]
            ? new Pending(id, made == Made, [.. keys.Where(key => key.Length > 0)])
            : null;

    public override string ToString() => string.Join('\t', [Id, Created ? Made : Found, .. Keys]);
}

public sealed partial class Library
{
    public void Install(
        LibraryEntry entry,
        string? prefix = null,
        string? installer = null,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null)
    {
        using var installing = Underway.Begin(layout.InstallLockPath(entry.Id))
                               ?? throw new InvalidOperationException(
                                   $"Cabinet is installing {entry.Name} right now — wait for "
                                   + "that to finish");
        var already = entry.Kind == PluginKind.Windows
            ? Installed().GetValueOrDefault(entry.Id)
            : null;
        var where = prefix
                    ?? already
                    ?? Unfinished().FirstOrDefault(left => left.Id == entry.Id).Prefix
                    ?? entry.Prefix;

        if (already is not null && already != where)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is installed in {already} already — install it again there, or "
                + "remove it first");
        }

        var installLog = layout.InstallLogPath(entry.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(installLog)!);
        File.WriteAllText(installLog, "");

        void Say(string line)
        {
            LogFile.Append(installLog, line);
            onOutput?.Invoke(line);
        }

        if (entry.Kind == PluginKind.Native)
        {
            if (prefix is not null)
            {
                throw new ArgumentException(
                    $"{entry.Name} is a Linux plugin, so it needs no prefix — your DAW loads it "
                    + "directly", nameof(prefix));
            }

            InstallNative(entry, installer, Say, onProgress);
            return;
        }

        InstallWindows(entry, where, installer, Say, onProgress);
    }

    private static void Settle(Prefixes prefixes, string where, string? logTo = null) =>
        prefixes.Run(where, "wineserver", ["-k"], logTo: logTo);

    private void InstallWindows(
        LibraryEntry entry,
        string prefix,
        string? installer,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        if (entry.Source == PluginSource.Byo && installer is null && entry.DemoUrl is null)
        {
            throw new InvalidOperationException(Undownloadable(entry));
        }

        if (installer is not null && !File.Exists(installer))
        {
            throw new FileNotFoundException($"no such file: {installer}", installer);
        }

        var prefixes = new Prefixes(layout, runner);
        var existing = prefixes.List().FirstOrDefault(one => one.Name == prefix);
        Directory.CreateDirectory(layout.PrefixPath(prefix));
        using var claim = prefixes.Claim(prefix, $"install {entry.Name} into {prefix}");
        using var underway = Underway.Begin(layout.PrefixInstalling(prefix))
                             ?? throw new PrefixInUseException(
                                 $"Cabinet is already installing into {prefix}, so it will not "
                                 + $"install {entry.Name} there yet — wait for that to finish.");
        var left = Pending.Parse(underway.Left);
        var created = existing is null || left is { Created: true } && !Recorded(prefix).Any();
        var pending = new Pending(entry.Id, created, left?.Id == entry.Id ? left.Keys : []);
        underway.Note(pending.ToString());

        if (!created && entry.Runner is { } wanted && !Answers(existing!.Runner, wanted))
        {
            onOutput?.Invoke(
                $"{prefix} keeps {existing.Runner}; {entry.Name} would rather have Wine {wanted}.");
        }

        prefixes.Prepare(
            prefix,
            created && entry.Runner is { } spec
                ? EnsureRunner(spec, onOutput, onProgress)
                : null,
            onOutput);

        if (entry.Env.Count > 0)
        {
            var settings = new PrefixSettings(layout);
            var current = settings.Variables(prefix);
            var added = entry.Env.Keys.Where(key => !current.ContainsKey(key)).ToList();

            foreach (var key in added)
            {
                settings.SetVariable(prefix, key, entry.Env[key]);
            }

            if (added.Count > 0)
            {
                onOutput?.Invoke($"Set {string.Join(", ", added)}.");
            }
        }

        if (entry.Winetricks.Count > 0)
        {
            var result = new Winetricks(layout, runner).Apply(prefix, entry.Winetricks, onOutput);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"{entry.Name}'s Winetricks dependencies exited with {result.ExitCode}");
            }
        }

        using (var staging = Staging.Create(layout.TempDir, "library"))
        {
            var chosen = installer ?? Fetch(entry, staging.Path, onOutput, onProgress);
            var before = Registered(prefix);

            if (entry.Script is null)
            {
                var result = prefixes.RunInstaller(prefix, chosen, onOutput);

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
                    staging.Path,
                    layout.PrefixPath(prefix),
                    prefixes.Variables(prefix),
                    onOutput);

                Settle(prefixes, prefix);
            }

            var appeared = Registered(prefix).Except(before, StringComparer.Ordinal).ToList();

            pending = pending with
            {
                Keys = appeared.Count > 0 ? appeared
                    : pending.Keys.Count > 0 ? pending.Keys
                    : [.. RecordedKeys(prefix, entry.Id)],
            };
            underway.Note(pending.ToString());
        }

        var dxvk = new Dxvk(layout, runner);

        if (entry.Dxvk && dxvk.InstalledIn(prefix) is null)
        {
            dxvk.Install(prefix, onOutput, onProgress);
        }

        var desktop = new VirtualDesktop(layout, runner);

        if (entry.Desktop && !desktop.EnabledIn(prefix))
        {
            desktop.Set(prefix, onOutput);
        }

        if (created && entry.Sync != SyncMode.System)
        {
            prefixes.SetSync(prefix, entry.Sync);
            onOutput?.Invoke($"Sync mode {PrefixSettings.Word(entry.Sync)}.");
        }

        Record(prefix, entry.Id, pending.Keys);
        Bridge(prefixes, onOutput);
        underway.Finish();
        ForgetUnfinished(entry, prefix);
    }

    private void ForgetUnfinished(LibraryEntry entry, string finished)
    {
        foreach (var (id, elsewhere) in Unfinished())
        {
            if (id == entry.Id && elsewhere is { } other && other != finished
                && Underway.Begin(layout.PrefixInstalling(other)) is { } left)
            {
                left.Finish();
            }
        }
    }

    private void InstallNative(
        LibraryEntry entry,
        string? supplied,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        if (entry.Source == PluginSource.Byo && supplied is null)
        {
            throw new InvalidOperationException(Undownloadable(entry));
        }

        if (supplied is not null && !File.Exists(supplied))
        {
            throw new FileNotFoundException($"no such file: {supplied}", supplied);
        }

        var root = layout.NativePath(entry.Id);
        var marker = layout.NativeInstalling(entry.Id);
        var data = entry.Data is { } relative ? layout.DataPath(relative) : null;
        var interrupted = Underway.Marked(marker);

        if (!interrupted && Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"{entry.Name} is installed already — remove it first");
        }

        using var underway = Underway.Begin(marker)
                             ?? throw new InvalidOperationException(
                                 $"Cabinet is already installing {entry.Name} — wait for that to "
                                 + "finish");
        var links = new List<(string Link, string? Replaced)>();
        var madeData = false;

        try
        {
            if (underway.Left is { } left)
            {
                onOutput?.Invoke($"Clearing what an unfinished install of {entry.Name} left.");
                Clear(root, left.Split('\n').ElementAtOrDefault(1) == data ? data : null);
            }

            underway.Note(entry.Id);

            if (data is not null && Directory.Exists(data))
            {
                throw new InvalidOperationException(
                    $"{data} is already there — {entry.Name} keeps its presets in it, so move it "
                    + "aside first");
            }

            using var staging = Staging.Create(layout.TempDir, "library");
            var archive = supplied ?? Fetch(entry, staging.Path, onOutput, onProgress);
            Directory.CreateDirectory(root);

            if (data is not null)
            {
                underway.Note($"{entry.Id}\n{data}");
                Directory.CreateDirectory(data);
                madeData = true;
                onOutput?.Invoke($"Its presets and resources go in {data}.");
            }

            Lay(entry, archive, root, data, staging.Path, onOutput);
            Relink(entry, root, onOutput);
            Link(entry, root, links, onOutput);
        }
        catch
        {
            Unlink(links);
            Discard(root);

            if (madeData)
            {
                Discard(data!);
            }

            underway.Finish();
            throw;
        }

        underway.Finish();
    }

    private void Clear(string root, string? data)
    {
        foreach (var link in LinksInto(root).ToList())
        {
            File.Delete(link);
        }

        Discard(root);

        if (data is not null)
        {
            Discard(data);
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
        || RunnerIndex.Families.Any(family => Runners.DeriveName(family.AssetFor(spec)) == name)
        || RunnerIndex.MatchesFixedRunner(name, spec);

    private string Fetch(
        LibraryEntry entry,
        string staging,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        var url = entry.DemoUrl ?? entry.Url!;
        var checksum = entry.DemoSha256 ?? entry.Sha256;
        var target = Path.Combine(staging, ArchiveName(url, entry.Kind));

        http.ToFile(url, target, onOutput, onProgress);

        if (checksum is { } expected)
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

    public static string ArchiveName(LibraryEntry entry) =>
        ArchiveName(entry.Url ?? entry.DemoUrl!, entry.Kind);

    private static string ArchiveName(string url, PluginKind kind)
    {
        var path = url.TrimEnd('/');
        var name = path[(path.LastIndexOf('/') + 1)..];

        return kind == PluginKind.Windows && !Path.HasExtension(name)
            ? name + ".exe"
            : name;
    }

    private static string Undownloadable(LibraryEntry entry) =>
        $"{entry.Name} cannot be downloaded, so it needs the file you have"
        + (entry.Account is { } account ? $" from {account}" : "");

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
}
