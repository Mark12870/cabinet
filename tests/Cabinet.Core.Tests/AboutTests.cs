using System.Text;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class AboutTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AFileRemoteIsALocalBuild()
    {
        GiveInstall("cabinet-local", "file:///home/u/Projects/cabinet/repo");

        var build = Subject.Read();

        Assert.Equal("cabinet-local", build.Remote);
        Assert.Equal(Origin.Local, build.Origin);
    }

    [Fact]
    public void AnHttpsRemoteIsAPublishedBuild()
    {
        GiveInstall("cabinet", "https://mark12870.github.io/cabinet/repo/");

        var build = Subject.Read();

        Assert.Equal("https://mark12870.github.io/cabinet/repo/", build.Url);
        Assert.Equal(Origin.Published, build.Origin);
    }

    [Fact]
    public void AnUnreadableRepoConfigIsUnknownRatherThanLocal()
    {
        GiveInstall("cabinet", url: null);

        var build = Subject.Read();

        Assert.Equal("cabinet", build.Remote);
        Assert.Null(build.Url);
        Assert.Equal(Origin.Unknown, build.Origin);
    }

    [Fact]
    public void AnInstallWithNoDeployFileIsUnknown()
    {
        var build = Subject.Read();

        Assert.Equal("unknown", build.Remote);
        Assert.Equal(Origin.Unknown, build.Origin);
    }

    [Fact]
    public void TheRepoConfigIsFoundBesideASystemInstallToo()
    {
        var files = Path.Combine(
            "/var/lib/flatpak/app", Layout.AppId, "current", "active", "files");

        var layout = new Layout("/home/u", "/run/user/1000", null, files);

        Assert.Equal("/var/lib/flatpak/repo/config", layout.RepoConfig);
    }

    private About Subject => new(
        new Layout(root, "/run/user/1000", null, AppFiles), new UnusedRunner());

    private string AppFiles =>
        Path.Combine(root, ".local", "share", "flatpak", "app", Layout.AppId,
            "current", "active", "files");

    private void GiveInstall(string remote, string? url)
    {
        var active = Path.GetDirectoryName(AppFiles)!;
        Directory.CreateDirectory(active);

        File.WriteAllBytes(
            Path.Combine(active, "deploy"),
            [.. Encoding.UTF8.GetBytes(remote), 0, .. new byte[] { 1, 2, 3 }]);

        if (url is null)
        {
            return;
        }

        var repo = Path.Combine(root, ".local", "share", "flatpak", "repo");
        Directory.CreateDirectory(repo);
        File.WriteAllText(
            Path.Combine(repo, "config"),
            $"[core]\nrepo_version=1\n\n[remote \"{remote}\"]\nurl={url}\ngpg-verify=true\n");
    }
}
