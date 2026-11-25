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

    public void Launch(
        LibraryEntry entry,
        Action<string>? onOutput = null,
        string? link = null,
        Action<DownloadStep>? onDownload = null)
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

        if (IsOpen(prefixes, where, entry))
        {
            Say($"{entry.Name} is already open, so Cabinet brings its window back.");
            Show(entry);
            return;
        }

        using var open = prefixes.OpenApp(where, $"open {entry.Name}");
        var pluginDirectories = layout.PrefixPluginDirs(where).ToList();

        foreach (var directory in pluginDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        var guarded = KeepDir(where, entry);
        var downloads = new Downloads(guarded, layout.PrefixKeptDir(where), runner, atOnce: entry.Unpack is null);

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

        var receiving = false;
        var installing = false;
        var ready = new List<string>();
        string? failed = null;

        void Done(string? stopped)
        {
            onDownload?.Invoke(new DownloadInstalled(ready.Distinct().ToList(), failed ?? stopped));
            receiving = false;
            installing = false;
            ready.Clear();
            failed = null;
        }

        var watching = Task.Run(() =>
        {
            var lastSaid = "";

            void Installing(string line)
            {
                lastSaid = line.Trim();
                Say(line);
                onDownload?.Invoke(new DownloadInstalling(line));
            }

            while (monitor.Wait(
                closed.Token,
                watch.Pending || downloads.Arriving ? Stability : Timeout.InfiniteTimeSpan))
            {
                if (entry.Unpack is not null && downloads.Arriving)
                {
                    receiving = true;
                    onDownload?.Invoke(new DownloadReceiving(downloads.Bytes));
                }

                try
                {
                    if (downloads.Hold(Say) && entry.Unpack is not null)
                    {
                        installing = true;
                        Unpack(entry, prefixes, where, Installing);
                    }
                }
                catch (Exception failure)
                {
                    failed = failure.Message;

                    if (failure.Message != lastSaid)
                    {
                        Say(failure.Message);
                    }
                }

                try
                {
                    if (watch.Changed(Bundled(where)) is { } change)
                    {
                        Narrate(change, Say);
                        Bridge(prefixes, Say);
                        watch.Accept();
                        ready.AddRange(change.Appeared.Select(Downloads.Ready));
                    }
                }
                catch (Exception failure)
                {
                    failed = receiving || installing ? failure.Message : failed;
                    Say(failure.Message);
                }

                if (installing && !watch.Pending && !downloads.Arriving)
                {
                    Done(null);
                }
                else if (receiving && !installing && !downloads.Arriving)
                {
                    Done("The download stopped before it finished.");
                }
            }
        });

        var stopped = layout.PrefixStopped(where, entry.Id);
        File.Delete(stopped);
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
            closed.Cancel();
            watching.Wait();
            open.Dispose();
        }

        var unfinished = receiving && !installing && downloads.Arriving;
        downloads.Hold(Say, settled: true);

        Exception? recoverFailure = null;
        var recovered = true;

        if (entry.Finish is not null)
        {
            try
            {
                Recover(entry, prefixes, where, log, Say);
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

        if (watch.Closed(Bundled(where)) is { } change)
        {
            Narrate(change, Say);
            ready.AddRange(change.Appeared.Select(Downloads.Ready));
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

        if (receiving || installing)
        {
            Done(recoverFailure?.Message
                 ?? bridgeFailure?.Message
                 ?? (unfinished ? $"{entry.Name} closed before the download finished." : null));
        }

        var failures = new List<Exception>();

        var stoppedByCabinet = File.Exists(stopped);
        File.Delete(stopped);

        if (!ran.Ok && !stoppedByCabinet)
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

    public void Prepare(LibraryEntry entry, Action<string>? onOutput = null)
    {
        if (entry.Finish is null)
        {
            return;
        }

        var where = Where(entry);
        var prefixes = new Prefixes(layout, runner);

        if (IsOpen(prefixes, where, entry))
        {
            return;
        }

        onOutput?.Invoke($"Bringing what {entry.Name} installed up to date.");
        Recover(entry, prefixes, where, layout.PrefixLaunchLog(where), onOutput ?? (_ => { }));
    }

    private void Recover(LibraryEntry entry, Prefixes prefixes, string where, string log, Action<string> say)
    {
        using var claim = prefixes.Claim(where, $"finish {entry.Name}'s install");

        new InstallScript(layout, runner).Finish(
            entry,
            layout.PrefixPath(where),
            layout.PrefixKeptDir(where),
            prefixes.Variables(where),
            say);

        Settle(prefixes, where, log);
    }

    private void Unpack(LibraryEntry entry, Prefixes prefixes, string where, Action<string> say) =>
        new InstallScript(layout, runner).Finish(
            entry,
            layout.PrefixPath(where),
            layout.PrefixKeptDir(where),
            prefixes.Variables(where),
            say);

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
        File.WriteAllText(layout.PrefixStopped(where, entry.Id), "");
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
            prefixes.RunJoined(where, ["wineboot", "--kill", "--shutdown"], logTo: log);

            if (Running(prefixes, where, entry.LaunchExe!))
            {
                return LeftRunning(entry, where, $"{late} It survived being ended, so it is still running.", say);
            }

            Helper(entry, prefixes, where, log, say);
            var ended = $"{entry.Name} would not close, so Cabinet ended Wine in {where}.";
            say(ended);

            return new StopOutcome(StopResult.Forced, ended);
        }
        catch (PrefixInUseException busy)
        {
            return LeftRunning(entry, where, $"{late} {busy.Message}", say);
        }
    }

    private StopOutcome LeftRunning(LibraryEntry entry, string where, string left, Action<string> say)
    {
        File.Delete(layout.PrefixStopped(where, entry.Id));
        say(left);

        return new StopOutcome(StopResult.LeftRunning, left);
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

    public void StopServices(string prefix)
    {
        var services = Entries()
            .Where(entry => entry.Prefix == prefix && entry.LaunchService is not null)
            .Select(entry => entry.LaunchService!)
            .ToList();

        if (services.Count == 0)
        {
            return;
        }

        var prefixes = new Prefixes(layout, runner);

        try
        {
            using var claim = prefixes.Claim(prefix, $"stop {string.Join(" and ", services)}");
            Settle(prefixes, prefix);
        }
        catch (PrefixInUseException)
        {
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

    public void Open(string link, Action<string>? onOutput = null, Action<DownloadStep>? onDownload = null)
    {
        var entry = ForLink(link);
        var where = Where(entry);
        var prefixes = new Prefixes(layout, runner);

        if (IsOpen(prefixes, where, entry))
        {
            var log = layout.PrefixLaunchLog(where);
            var line = $"Handing the link to {entry.Name}.";
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
            prefixes.RunJoined(
                where,
                [
                    "reg", "add", $@"HKLM\Software\Classes\{entry.Scheme}\shell\open\command",
                    "/ve", "/d", $"\"{entry.Launch}\" \"%1\"", "/f",
                ],
                logTo: log);
            var handed = prefixes.RunJoined(where, ["start", link], logTo: log);

            if (!handed.Ok)
            {
                throw new InvalidOperationException(
                    $"{entry.Name} was not handed the link (exit code {handed.ExitCode})");
            }

            return;
        }

        Launch(entry, onOutput, link, onDownload);
    }

    public void Show(LibraryEntry entry)
    {
        var where = Where(entry);
        new Prefixes(layout, runner).RunJoined(
            where, [entry.Launch!, .. entry.LaunchArgs], logTo: layout.PrefixLaunchLog(where));
    }

    private static bool IsOpen(Prefixes prefixes, string where, LibraryEntry entry) =>
        prefixes.SessionLive(where) && Running(prefixes, where, entry.LaunchExe!);

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

        if (Installed().GetValueOrDefault(entry.Id) is { } prefix)
        {
            if (LogFile.Read(layout.PrefixLaunchLog(prefix)) is { } launch)
            {
                sections.Add($"Cabinet launch log{Environment.NewLine}{launch}");
            }

            if (LogFile.Read(layout.PrefixWineLog(prefix)) is { } wine)
            {
                sections.Add($"Plugin host log{Environment.NewLine}{wine}");
            }

            if (LogFile.Read(layout.PrefixPreviousWineLog(prefix)) is { } previous)
            {
                sections.Add($"Plugin host log, previous session{Environment.NewLine}{previous}");
            }
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
