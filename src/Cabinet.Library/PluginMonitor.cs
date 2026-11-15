namespace Cabinet.Core;

public sealed class PluginMonitor : IDisposable
{
    internal static readonly TimeSpan Rescan = TimeSpan.FromSeconds(2);

    private readonly string[] directories;
    private readonly TimeSpan quiet;
    private readonly AutoResetEvent signal = new(false);
    private readonly object gate = new();
    private readonly Func<string, FileSystemWatcher> watch;
    private FileSystemWatcher[] watchers = [];
    private bool rescanning;
    private bool recovering;
    private bool disposed;
    private readonly Func<WaitHandle[], TimeSpan, int> wait;

    public PluginMonitor(IEnumerable<string> directories, TimeSpan? quiet = null)
        : this(directories, quiet ?? TimeSpan.FromMilliseconds(250), Watch, WaitHandle.WaitAny)
    {
    }

    internal PluginMonitor(TimeSpan quiet, Func<WaitHandle[], TimeSpan, int> wait)
        : this([], quiet, Watch, wait)
    {
    }

    internal PluginMonitor(
        IEnumerable<string> directories,
        TimeSpan quiet,
        Func<string, FileSystemWatcher> watch,
        Func<WaitHandle[], TimeSpan, int> wait)
    {
        this.directories = [.. directories.Select(Path.GetFullPath)];
        this.quiet = quiet;
        this.watch = watch;
        this.wait = wait;

        try
        {
            watchers = CreateWatchers();
        }
        catch (IOException)
        {
            rescanning = true;
        }
    }

    public bool Wait(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var handles = new WaitHandle[] { signal, cancellationToken.WaitHandle };

        lock (gate)
        {
            if (rescanning && timeout == Timeout.InfiniteTimeSpan)
            {
                timeout = Rescan;
            }
        }

        var result = wait(handles, timeout);

        if (result == 1)
        {
            return false;
        }

        if (result == WaitHandle.WaitTimeout)
        {
            return true;
        }

        var settling = timeout == Timeout.InfiniteTimeSpan ? quiet : timeout;

        do
        {
            result = wait(handles, settling);
        }
        while (result == 0);

        return result == WaitHandle.WaitTimeout;
    }

    public void Dispose()
    {
        FileSystemWatcher[] toDispose;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            toDispose = watchers;
            watchers = [];
        }

        foreach (var watcher in toDispose)
        {
            watcher.Dispose();
        }

        signal.Dispose();
    }

    private FileSystemWatcher[] CreateWatchers()
    {
        var created = new List<FileSystemWatcher>();

        try
        {
            foreach (var directory in directories)
            {
                var watcher = watch(directory);
                created.Add(watcher);
                Follow(watcher);
            }

            return [.. created];
        }
        catch
        {
            foreach (var watcher in created)
            {
                watcher.Dispose();
            }

            throw;
        }
    }

    private static FileSystemWatcher Watch(string path) =>
        new(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };

    private void Follow(FileSystemWatcher watcher)
    {
        watcher.Created += (_, _) => Signal();
        watcher.Changed += (_, _) => Signal();
        watcher.Deleted += (_, _) => Signal();
        watcher.Renamed += (_, _) => Signal();
        watcher.Error += (_, _) => Recover();
        watcher.EnableRaisingEvents = true;
    }

    private void Recover()
    {
        FileSystemWatcher[] previous;

        lock (gate)
        {
            if (disposed || recovering)
            {
                return;
            }

            recovering = true;
            previous = watchers;
        }

        FileSystemWatcher[] replacement;

        try
        {
            replacement = CreateWatchers();
        }
        catch
        {
            lock (gate)
            {
                recovering = false;
                rescanning = true;
            }

            Signal();
            return;
        }

        var disposeReplacement = false;

        lock (gate)
        {
            if (disposed)
            {
                disposeReplacement = true;
            }
            else
            {
                watchers = replacement;
                recovering = false;
                rescanning = false;
            }
        }

        if (disposeReplacement)
        {
            foreach (var watcher in replacement)
            {
                watcher.Dispose();
            }
        }
        else
        {
            foreach (var watcher in previous)
            {
                watcher.Dispose();
            }
        }

        Signal();
    }

    private void Signal()
    {
        lock (gate)
        {
            if (!disposed)
            {
                signal.Set();
            }
        }
    }
}
