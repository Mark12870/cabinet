namespace Cabinet.Core;

public abstract record DownloadStep;

public sealed record DownloadReceiving(long Bytes) : DownloadStep;

public sealed record DownloadInstalling(string Line) : DownloadStep;

public sealed record DownloadInstalled(IReadOnlyList<string> Ready, string? Failure) : DownloadStep;

internal sealed class Downloads(string? source, string destination, IProcessRunner runner, bool atOnce = false)
{
    private static readonly TimeSpan Stalled = TimeSpan.FromMinutes(1);

    private readonly HashSet<string> kept = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (long Size, DateTime Written)> seen = new(StringComparer.Ordinal);

    public bool Arriving => Unkept().Any(Growing);

    public long Bytes => Unkept().Where(Growing).Sum(file => file.Length);

    public bool Hold(Action<string> onOutput, bool settled = false)
    {
        if (source is null || !Directory.Exists(source))
        {
            return false;
        }

        kept.RemoveWhere(name => !File.Exists(Path.Combine(source, name)));
        var held = false;

        foreach (var file in Unkept().ToList())
        {
            var name = file.Name;
            var now = (file.Length, file.LastWriteTimeUtc);
            var finished = seen.TryGetValue(name, out var before) && before == now && Path.HasExtension(name);
            seen[name] = now;

            if (!settled && !atOnce && !finished)
            {
                continue;
            }

            kept.Add(name);
            seen.Remove(name);
            Directory.CreateDirectory(destination);

            var linked = runner.Run("ln", ["-f", file.FullName, Path.Combine(destination, name)]);

            onOutput(linked.Ok
                ? $"  keeping {name}"
                : $"  could not keep {name} (exit code {linked.ExitCode})");
            held |= linked.Ok;
        }

        return held;
    }

    public static string Ready(string bundle) =>
        $"{Path.GetFileNameWithoutExtension(bundle)} ({Library.Format(bundle)})";

    private static bool Growing(FileInfo file) =>
        Path.HasExtension(file.Name) || DateTime.UtcNow - file.LastWriteTimeUtc < Stalled;

    private IEnumerable<FileInfo> Unkept() =>
        source is null || !Directory.Exists(source)
            ? []
            : new DirectoryInfo(source).EnumerateFiles().Where(file => file.Exists && !kept.Contains(file.Name));
}
