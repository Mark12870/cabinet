namespace Cabinet.Core;

public enum StopResult
{
    Closed,
    LeftRunning,
    Forced,
}

public sealed record StopOutcome(StopResult Result, string Told);

public sealed partial class Library
{
    private const int ServiceAlreadyRunning = 1056 & 0xff;

    private static readonly TimeSpan Stability = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    public void Launch(LibraryEntry entry, Action<string>? onOutput = null, string? link = null)
    {
        if (entry.Launch is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is a plugin, not an application Cabinet can open");
        }

        var where = Where(entry);

        var prefixes = new Prefixes(layout, runner);
        var log = layout.PrefixLaunchLog(where);

        void Say(string line)
        {
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
        }

        using var open = prefixes.OpenApp(where, $"open {entry.Name}");
        var pluginDirectories = layout.PrefixPluginDirs(where).ToList();

        foreach (var directory in pluginDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        var guarded = KeepDir(where, entry);
        var kept = new HashSet<string>(StringComparer.Ordinal);

        if (guarded is not null)
        {
            Directory.CreateDirectory(guarded);
        }

        var watch = new PluginWatch(Bundled(where));
        using var closed = new CancellationTokenSource();
        using var monitor = new PluginMonitor(
            guarded is null ? pluginDirectories : [.. pluginDirectories, guarded]);

        File.WriteAllText(log, "");
        Say($"Opening {entry.Name}. What it installs is bridged as it lands.");

        if (new VirtualDesktop(layout, runner).EnabledIn(where))
        {
            Say($"{where} draws on a desktop of its own, so {entry.Name} is confined to it, "
                + $"the pointer too, until {where}'s virtual desktop is turned off.");
        }

        if (entry.LaunchService is { } service)
        {
            Say($"Starting {service}.");
            var started = prefixes.RunJoined(where, ["sc", "start", service], logTo: log);

            if (!started.Ok && started.ExitCode != ServiceAlreadyRunning)
            {
                throw new InvalidOperationException(
                    $"{entry.Name}'s {service} service could not start (exit code {started.ExitCode})");
            }
        }

        var watching = Task.Run(() =>
        {
            while (monitor.Wait(
                closed.Token,
                watch.Pending ? Stability : Timeout.InfiniteTimeSpan))
            {
                try
                {
                    Hold(guarded, layout.PrefixKeptDir(where), kept, Say);

                    if (watch.Changed(Bundled(where)) is { } change)
                    {
                        Narrate(change, Say);
                        Bridge(prefixes, Say);
                        watch.Accept();
                    }
                }
                catch (Exception failure)
                {
                    Say(failure.Message);
                }
            }
        });

        using var opened = Underway.Begin(layout.PrefixOpen(where, entry.Id));
        opened?.Note(entry.Id);
        ProcessResult ran;

        try
        {
            ran = prefixes.RunJoined(
                where,
                [entry.Launch, .. entry.LaunchArgs, .. link is null ? [] : new[] { link }],
                logTo: log);

            if (entry.LaunchService is { } stopping)
            {
                prefixes.RunJoined(where, ["sc", "stop", stopping], logTo: log);
            }

            if (entry.LaunchHelper is { } helper && !Running(prefixes, where, entry.LaunchExe!))
            {
                prefixes.RunJoined(where, ["taskkill", "/f", "/im", helper], logTo: log);
            }
        }
        finally
        {
            open.Dispose();
            closed.Cancel();
            watching.Wait();
        }

        Hold(guarded, layout.PrefixKeptDir(where), kept, Say);

        if (watch.Closed(Bundled(where)) is { } change)
        {
            Narrate(change, Say);
        }

        Exception? recoverFailure = null;
        var recovered = true;

        if (entry.Recover is not null)
        {
            try
            {
                using var claim = prefixes.Claim(where, $"finish {entry.Name}'s install");

                new InstallScript(layout, runner).Recover(
                    entry,
                    layout.PrefixPath(where),
                    layout.PrefixKeptDir(where),
                    prefixes.Variables(where),
                    Say);

                Settle(prefixes, where, log);
            }
            catch (PrefixInUseException waiting)
            {
                recovered = false;
                Say($"{waiting.Message} What {entry.Name} downloaded is kept, and Cabinet "
                    + $"finishes the install the next time you open {entry.Name}.");
            }
            catch (Exception failure)
            {
                recoverFailure = failure;
            }
        }

        Exception? bridgeFailure = null;

        try
        {
            Bridge(prefixes, Say);
        }
        catch (Exception failure)
        {
            bridgeFailure = failure;
        }

        if (recovered && recoverFailure is null && bridgeFailure is null)
        {
            opened?.Finish();
        }

        var failures = new List<Exception>();

        if (!ran.Ok)
        {
            foreach (var line in Tail(log))
            {
                onOutput?.Invoke(line);
            }

            failures.Add(new InvalidOperationException($"{entry.Name} exited with {ran.ExitCode}"));
        }

        if (recoverFailure is not null)
        {
            failures.Add(recoverFailure);
        }

        if (bridgeFailure is not null)
        {
            failures.Add(bridgeFailure);
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }

        Say($"{entry.Name} closed.");
    }

    public StopOutcome Stop(
        LibraryEntry entry, TimeSpan? grace = null, Action<string>? onOutput = null)
    {
        if (entry.Launch is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} is a plugin, not an application Cabinet can open");
        }

        var where = Where(entry);
        var prefixes = new Prefixes(layout, runner);
        var log = layout.PrefixLaunchLog(where);
        var waiting = grace ?? StopGrace;

        void Say(string line)
        {
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
        }

        Say($"Closing {entry.Name}.");
        prefixes.RunJoined(where, ["taskkill", "/f", "/im", entry.LaunchExe!], logTo: log);

        if (entry.LaunchService is { } service)
        {
            Say($"Stopping {service}.");
            prefixes.RunJoined(where, ["sc", "stop", service], logTo: log);
        }

        var deadline = DateTime.UtcNow + waiting;

        while (Running(prefixes, where, entry.LaunchExe!))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return Force(entry, prefixes, where, waiting, log, Say);
            }

            Thread.Sleep(Beat);
        }

        Helper(entry, prefixes, where, log, Say);
        Say($"{entry.Name} is closed.");

        return new StopOutcome(StopResult.Closed, $"{entry.Name} is closed.");
    }

    private StopOutcome Force(
        LibraryEntry entry,
        Prefixes prefixes,
        string where,
        TimeSpan waiting,
        string log,
        Action<string> say)
    {
        var late = $"{entry.Name} was still running {waiting.TotalSeconds:0} seconds later.";

        try
        {
            using var guard = prefixes.Guard(where, "end its Wine");

            say($"{late} Ending every Wine process in {where}, Cabinet's own included.");
            prefixes.RunJoined(where, ["wineboot", "-k"], logTo: log);

            if (Running(prefixes, where, entry.LaunchExe!))
            {
                var left = $"{late} It survived being ended, so it is still running.";
                say(left);

                return new StopOutcome(StopResult.LeftRunning, left);
            }

            Helper(entry, prefixes, where, log, say);
            var ended = $"{entry.Name} would not close, so Cabinet ended Wine in {where}.";
            say(ended);

            return new StopOutcome(StopResult.Forced, ended);
        }
        catch (PrefixInUseException busy)
        {
            var left = $"{late} {busy.Message}";
            say(left);

            return new StopOutcome(StopResult.LeftRunning, left);
        }
    }

    private static void Helper(
        LibraryEntry entry, Prefixes prefixes, string where, string log, Action<string> say)
    {
        if (entry.LaunchHelper is { } helper)
        {
            say($"Closing {helper}.");
            prefixes.RunJoined(where, ["taskkill", "/f", "/im", helper], logTo: log);
        }
    }

    public LibraryEntry ForLink(string link)
    {
        var at = link.IndexOf(':');

        if (at < 1)
        {
            throw new InvalidOperationException($"{link} is not a link");
        }

        var scheme = link[..at].ToLowerInvariant();
        var entry = Entries().FirstOrDefault(candidate => candidate.Scheme == scheme)
            ?? throw new InvalidOperationException(
                $"no app in the library opens {scheme}: links");

        if (Installed().GetValueOrDefault(entry.Id) is null)
        {
            throw new InvalidOperationException(
                $"{entry.Name} opens {scheme}: links but is not installed");
        }

        return entry;
    }

    public void Open(string link, Action<string>? onOutput = null)
    {
        var entry = ForLink(link);
        var where = Where(entry);
        var prefixes = new Prefixes(layout, runner);

        if (prefixes.SessionLive(where) && Running(prefixes, where, entry.LaunchExe!))
        {
            var log = layout.PrefixLaunchLog(where);
            var line = $"Handing the link to {entry.Name}.";
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
            var handed = prefixes.RunJoined(where, ["start", link], logTo: log);

            if (!handed.Ok)
            {
                throw new InvalidOperationException(
                    $"{entry.Name} was not handed the link (exit code {handed.ExitCode})");
            }

            return;
        }

        Launch(entry, onOutput, link);
    }

    private static bool Running(Prefixes prefixes, string where, string exe) =>
        prefixes.RunJoined(where, ["tasklist", "/fo", "csv", "/nh"])
            .Stdout.Contains($"\"{exe}\"", StringComparison.OrdinalIgnoreCase);

    public string? LaunchLog(LibraryEntry entry)
    {
        var sections = new List<string>();

        if (LogFile.Read(layout.InstallLogPath(entry.Id)) is { } install)
        {
            sections.Add($"Cabinet installation log{Environment.NewLine}{install}");
        }

        if (Installed().GetValueOrDefault(entry.Id) is { } prefix
            && LogFile.Read(layout.PrefixLaunchLog(prefix)) is { } launch)
        {
            sections.Add($"Cabinet launch log{Environment.NewLine}{launch}");
        }

        if (LogFile.Read(layout.RuntimeLogPath) is { } runtime)
        {
            sections.Add($"yabridge runtime log (shared){Environment.NewLine}{runtime}");
        }

        return sections.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, sections)
            : null;
    }

    private static IEnumerable<string> Tail(string log) =>
        File.Exists(log)
            ? File.ReadLines(log)
                .Where(line => line.Trim().Length > 0)
                .TakeLast(20)
            : [];

    private string? KeepDir(string where, LibraryEntry entry) =>
        entry.Keep is { } keep
            ? Path.Combine(
                layout.PrefixPath(where), keep.Replace('/', Path.DirectorySeparatorChar))
            : null;

    private void Hold(
        string? source, string destination, ISet<string> kept, Action<string> onOutput)
    {
        if (source is null || !Directory.Exists(source))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);

            if (!kept.Add(name))
            {
                continue;
            }

            Directory.CreateDirectory(destination);

            var linked = runner.Run("ln", ["-f", file, Path.Combine(destination, name)]);

            onOutput(linked.Ok
                ? $"  keeping {name}"
                : $"  could not keep {name} (exit code {linked.ExitCode})");
        }
    }

    private static void Narrate(PluginChange change, Action<string>? onOutput)
    {
        foreach (var bundle in change.Appeared)
        {
            onOutput?.Invoke($"  {Path.GetFileName(bundle)} appeared");
        }

        foreach (var bundle in change.Gone)
        {
            onOutput?.Invoke($"  removed {Path.GetFileName(bundle)}");
        }
    }
}
