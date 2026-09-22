namespace Cabinet.Core;

public sealed class PrefixInUseException(string message) : InvalidOperationException(message);

public sealed record SessionPaths(
    string Socket, string Busy, string Apps, string Change, string Record, string Log)
{
    public static SessionPaths Parse(string printed)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in printed.Split('\n'))
        {
            var at = line.Trim().IndexOf(' ');

            if (at > 0)
            {
                found[line.Trim()[..at]] = line.Trim()[(at + 1)..];
            }
        }

        return new SessionPaths(
            Take(found, "socket"),
            Take(found, "busy"),
            Take(found, "apps"),
            Take(found, "change"),
            Take(found, "record"),
            Take(found, "log"));
    }

    private static string Take(IReadOnlyDictionary<string, string> found, string label) =>
        found.TryGetValue(label, out var path) && path.Length > 0
            ? path
            : throw new InvalidOperationException(
                $"the Wine session paths carry no {label} — this Cabinet's shim is too old");
}

public sealed class PrefixClaim : IDisposable
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Changing = new(StringComparer.Ordinal);

    private readonly string key;
    private readonly List<FileStream> holding;

    private PrefixClaim(string key, List<FileStream> holding)
    {
        this.key = key;
        this.holding = holding;
    }

    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(200);

    internal static PrefixClaim Take(
        SessionPaths paths, string prefix, string what, bool apps, TimeSpan settle,
        Func<bool> live)
    {
        lock (Gate)
        {
            if (Changing.TryGetValue(paths.Change, out var thread))
            {
                return thread == Environment.CurrentManagedThreadId
                    ? new PrefixClaim(paths.Change, [])
                    : throw Already(prefix, what);
            }

            Changing[paths.Change] = Environment.CurrentManagedThreadId;
        }

        var holding = new List<FileStream>();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.Change)!);
            holding.Add(Alone(paths.Change) ?? throw Already(prefix, what));
            holding.Add(Exclusive(paths.Busy) ?? throw new PrefixInUseException(
                $"A DAW is using plugins from {prefix}, so Cabinet will not {what} — "
                + "close those plugins in your DAW and try again."));

            if (apps)
            {
                holding.Add(Alone(paths.Apps) ?? throw new PrefixInUseException(
                    $"An app Cabinet opened is still running in {prefix}, so Cabinet will not "
                    + $"{what} — close it and try again."));
            }

            if (settle > TimeSpan.Zero && live() && !Retired(paths.Socket, settle))
            {
                throw new PrefixInUseException(
                    $"{prefix}'s Wine session is still finishing, so Cabinet will not {what} "
                    + "yet — try again in a few seconds.");
            }
        }
        catch
        {
            Release(paths.Change, holding);
            throw;
        }

        return new PrefixClaim(paths.Change, holding);
    }

    internal static PrefixApps Open(SessionPaths paths, string prefix, string what)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Apps)!);

        return new PrefixApps(
            Shared(paths.Apps) ?? throw new PrefixInUseException(
                $"Cabinet is changing {prefix} right now, so it will not {what} — "
                + "wait for that to finish and try again."));
    }

    private static PrefixInUseException Already(string prefix, string what) =>
        new($"Cabinet is already changing {prefix}, so it will not {what} yet — "
            + "wait for that change to finish.");

    private static bool Retired(string socket, TimeSpan settle)
    {
        var deadline = DateTime.UtcNow + settle;

        while (File.Exists(socket))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(Beat);
        }

        return true;
    }

    private static FileStream? Alone(string path) => Opened(path, FileShare.None);

    private static FileStream? Shared(string path) => Opened(path, FileShare.Read);

    private static FileStream? Opened(string path, FileShare share)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, share);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static FileStream? Exclusive(string path)
    {
        var file = Opened(path, FileShare.ReadWrite);

        if (file is null)
        {
            return null;
        }

        try
        {
            file.Lock(0, 0);
            return file;
        }
        catch (IOException)
        {
            file.Dispose();
            return null;
        }
    }

    private static void Release(string key, List<FileStream> holding)
    {
        foreach (var file in holding)
        {
            file.Dispose();
        }

        holding.Clear();

        lock (Gate)
        {
            if (Changing.TryGetValue(key, out var thread)
                && thread == Environment.CurrentManagedThreadId)
            {
                Changing.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        if (holding.Count > 0)
        {
            Release(key, holding);
        }
    }
}

public sealed class PrefixApps(FileStream holding) : IDisposable
{
    public void Dispose() => holding.Dispose();
}
