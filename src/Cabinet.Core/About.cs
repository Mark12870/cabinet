using System.Text;

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
            new Yabridgectl(layout, runner).Version(),
            ReadWine(),
            metainfo.Homepage,
            metainfo.BugTracker);
    }

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
