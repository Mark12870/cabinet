using System.Text;
using System.Text.Json;

namespace Cabinet.Core;

public enum Origin
{
    Published,
    Local,
    Unknown,
}

public sealed record Build(
    string Version,
    string Remote,
    string? Url,
    string Commit,
    Origin Origin,
    string Yabridge,
    string Wine,
    string? Homepage,
    string? BugTracker);

public sealed class About(Layout layout, IProcessRunner runner)
{
    public Build Read()
    {
        var metainfo = ReadMetainfo();
        var remote = ReadRemote();
        var url = remote is null ? null : ReadRemoteUrl(remote);

        return new Build(
            metainfo.Version,
            remote ?? "unknown",
            url,
            Layout.FlatpakInfo.Get("Instance", "app-commit") ?? "unknown",
            OriginOf(url),
            ReadYabridge(),
            ReadWine(),
            metainfo.Homepage,
            metainfo.BugTracker);
    }

    public static IReadOnlyList<Release> Changelog() => ReadMetainfo().Releases;

    private static Origin OriginOf(string? url) => url switch
    {
        null => Origin.Unknown,
        _ when url.StartsWith("file://", StringComparison.Ordinal) => Origin.Local,
        _ => Origin.Published,
    };

    private static Metainfo ReadMetainfo() =>
        File.Exists(Layout.MetainfoPath)
            ? Metainfo.Parse(File.ReadAllText(Layout.MetainfoPath))
            : Metainfo.Unknown;

    private string? ReadRemote()
    {
        if (!File.Exists(layout.DeployFile))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(layout.DeployFile);
        var end = Array.IndexOf(bytes, (byte)0);

        return end > 0 ? Encoding.UTF8.GetString(bytes, 0, end) : null;
    }

    private string? ReadRemoteUrl(string remote)
    {
        if (layout.RepoConfig is not { } config || !File.Exists(config))
        {
            return null;
        }

        return IniFile.Parse(File.ReadAllLines(config)).Get($"remote \"{remote}\"", "url");
    }

    private string ReadYabridge()
    {
        var version = new Yabridgectl(layout, runner).Version();

        return ReadYabridgeSource() is { } source ? $"{version} from {source}" : version;
    }

    private string? ReadYabridgeSource()
    {
        if (!File.Exists(layout.BuildManifest))
        {
            return null;
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(layout.BuildManifest));
        if (!manifest.RootElement.TryGetProperty("modules", out var modules))
        {
            return null;
        }

        return modules.EnumerateArray()
            .Where(module => module.ValueKind == JsonValueKind.Object
                             && module.TryGetProperty("name", out var name)
                             && name.GetString() == "yabridge"
                             && module.TryGetProperty("sources", out _))
            .SelectMany(module => module.GetProperty("sources").EnumerateArray())
            .Select(source => source.TryGetProperty("url", out var url) ? url.GetString()?.Split('/') : null)
            .Select(parts => parts is [_, _, "github.com", var owner, "yabridge", "archive", _]
                ? $"{owner}/yabridge"
                : null)
            .FirstOrDefault(origin => origin is not null);
    }

    private string ReadWine()
    {
        if (!File.Exists(Layout.Wine))
        {
            return "unknown";
        }

        var runners = new Runners(layout, runner);
        return runners.Version(runners.Bundled);
    }
}
