using System.Text;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class AboutTests : IDisposable
{
    private readonly string root = TestRoot.Create("about");

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
        GiveUnreadableInstall("cabinet");

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

    [Fact]
    public void TheBridgeNamesTheYabridgeSourceItWasBuiltFrom()
    {
        Directory.CreateDirectory(AppFiles);
        File.WriteAllText(Path.Combine(AppFiles, "manifest.json"), """
            {"modules": [
              "shared/dependency.json",
              {"name": "cabinet", "sources": [{"type": "patch", "path": "other.patch"}]},
              {"name": "yabridge", "sources": [
                {"type": "archive", "url": "https://github.com/example/yabridge/archive/0123456789abcdef0123456789abcdef01234567.tar.gz"},
                {"type": "patch", "path": "../patches/yabridge/first.patch"},
                {"type": "patch", "path": "../patches/yabridge/second.patch"},
                {"type": "archive", "url": "https://github.com/example/yabridge/releases/download/5.1.1/yabridge-5.1.1.tar.gz"}
              ]}
            ]}
            """);

        Assert.Equal("5.1.1 from example/yabridge", WithYabridgectl.Read().Yabridge);
    }

    [Fact]
    public void WithoutABuildManifestTheBridgeIsYabridgectlsVersion()
    {
        Assert.Equal("5.1.1", WithYabridgectl.Read().Yabridge);
    }

    private About WithYabridgectl
    {
        get
        {
            var yabridge = Path.Combine(root, "yabridge");
            Directory.CreateDirectory(yabridge);
            File.WriteAllText(Path.Combine(yabridge, "yabridgectl"), "");

            return new About(
                new Layout(root, "/run/user/1000", null, AppFiles, yabridgeDir: yabridge),
                new StubRunner(new ProcessResult(0, "yabridgectl 5.1.1\n", "")));
        }
    }

    private About Subject => new(
        new Layout(root, "/run/user/1000", null, AppFiles), new UnusedRunner());

    private string AppFiles =>
        Path.Combine(root, ".local", "share", "flatpak", "app", Layout.AppId,
            "current", "active", "files");

    private void GiveInstall(string remote, string url)
    {
        var active = Path.GetDirectoryName(AppFiles)!;
        Directory.CreateDirectory(active);

        File.WriteAllBytes(
            Path.Combine(active, "deploy"),
            [.. Encoding.UTF8.GetBytes(remote), 0, .. new byte[] { 1, 2, 3 }]);

        var repo = Path.Combine(root, ".local", "share", "flatpak", "repo");
        Directory.CreateDirectory(repo);
        File.WriteAllText(
            Path.Combine(repo, "config"),
             $"[core]\nrepo_version=1\n\n[remote \"{remote}\"]\nurl={url}\ngpg-verify=true\n");
    }

    private void GiveUnreadableInstall(string remote)
    {
        var active = Path.GetDirectoryName(AppFiles)!;
        Directory.CreateDirectory(active);

        File.WriteAllBytes(
            Path.Combine(active, "deploy"),
            [.. Encoding.UTF8.GetBytes(remote), 0, .. new byte[] { 1, 2, 3 }]);
    }
}
