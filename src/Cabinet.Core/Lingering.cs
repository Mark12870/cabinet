using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;

namespace Cabinet.Core;

public sealed record Lingering(IReadOnlyList<string> Programs, Action Stop)
{
    private static readonly AsyncLocal<Action<Lingering?>?> Watching = new();

    public static Action<Lingering?>? Watcher
    {
        get => Watching.Value;
        set => Watching.Value = value;
    }

    internal static Lingering Find(params Stream[] streams)
    {
        var pipes = streams
            .OfType<PipeStream>()
            .Select(stream => Target($"/proc/self/fd/{stream.SafePipeHandle.DangerousGetHandle()}"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var holders = Holders(pipes).Where(holder => !holder.System).ToList();

        return new Lingering(
            holders.Select(holder => holder.Name).Distinct(StringComparer.Ordinal).ToList(),
            () => End(holders.Select(holder => holder.Id)));
    }

    private static IEnumerable<(int Id, string Name, bool System)> Holders(IReadOnlySet<string> pipes)
    {
        if (pipes.Count == 0)
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var id) || id == Environment.ProcessId)
            {
                continue;
            }

            string[] descriptors;

            try
            {
                descriptors = Directory.GetFiles(Path.Combine(directory, "fd"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!descriptors.Any(descriptor => Target(descriptor) is { } pipe && pipes.Contains(pipe)))
            {
                continue;
            }

            if (Command(directory) is { } command)
            {
                yield return (id, Name(command), IsSystem(command));
            }
        }
    }

    private static string? Target(string link)
    {
        try
        {
            return new FileInfo(link).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Command(string directory)
    {
        try
        {
            return File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0')[0] is { Length: > 0 } first
                ? first
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Name(string command) => command[(command.LastIndexOfAny(['/', '\\']) + 1)..];

    private static bool IsSystem(string command) =>
        command.StartsWith(@"C:\windows\", StringComparison.OrdinalIgnoreCase)
        || Name(command) is "wineserver" or "wine" or "wine64" or "wine-preloader" or "wine64-preloader";

    private static void End(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                                  or Win32Exception)
            {
            }
        }
    }
}
