namespace Cabinet.Core;

public sealed class PluginMonitor : IDisposable
{
    private readonly string[] directories;
    private readonly TimeSpan quiet;
    private readonly AutoResetEvent signal = new(false);
    private readonly object gate = new();
    private FileSystemWatcher[] watchers = [];
    private bool recovering;
    private bool disposed;

    public PluginMonitor(IEnumerable<string> directories, TimeSpan? quiet = null)
    {
        this.directories = [.. directories.Select(Path.GetFullPath)];
        this.quiet = quiet ?? TimeSpan.FromMilliseconds(250);
        watchers = CreateWatchers();
    }

    public bool Wait(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var handles = new WaitHandle[] { signal, cancellationToken.WaitHandle };
        var result = WaitHandle.WaitAny(handles, timeout);

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
            result = WaitHandle.WaitAny(handles, settling);
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
                created.Add(Watch(directory));
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

    private FileSystemWatcher Watch(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };
        watcher.Created += (_, _) => Signal();
        watcher.Changed += (_, _) => Signal();
        watcher.Deleted += (_, _) => Signal();
        watcher.Renamed += (_, _) => Signal();
        watcher.Error += (_, _) => Recover();
        watcher.EnableRaisingEvents = true;
        return watcher;
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
