using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class RunnerIndexTests
{
    private const string Releases = """
        [
          {
            "tag_name": "9.21",
            "assets": [
              {"name": "sha256sums.txt", "browser_download_url": "https://x/9.21/sha256sums.txt"},
              {"name": "wine-9.21-amd64.tar.xz", "browser_download_url": "https://x/9.21/v"},
              {"name": "wine-9.21-amd64-wow64.tar.xz", "browser_download_url": "https://x/9.21/w"},
              {"name": "wine-9.21-staging-amd64-wow64.tar.xz", "browser_download_url": "https://x/9.21/sw"},
              {"name": "wine-9.21-staging-amd64.tar.xz", "browser_download_url": "https://x/9.21/plain"},
              {"name": "wine-9.21-staging-x86.tar.xz", "browser_download_url": "https://x/9.21/x"},
              {"name": "wine-9.21-staging-tkg-amd64-wow64.tar.xz", "browser_download_url": "https://x/9.21/tw"},
              {"name": "wine-9.21-staging-tkg-amd64.tar.xz", "browser_download_url": "https://x/9.21/s"}
            ]
          },
          {
            "tag_name": "9.22",
            "assets": [
              {"name": "wine-9.22-staging-tkg-amd64.tar.xz", "browser_download_url": "https://x/9.22/s"}
            ]
          },
          {
            "tag_name": "6.0",
            "assets": [{"name": "wine-6.0-amd64.tar.xz", "browser_download_url": "https://x/6.0/v"}]
          }
        ]
        """;

    [Fact]
    public void OnlyTheStagingTkgMultilibAssetIsOffered()
    {
        var release = RunnerIndex.ParseReleases(Releases).Single(r => r.Version == "9.21");

        Assert.Equal("wine-9.21-staging-tkg-amd64.tar.xz", release.Asset);
        Assert.Equal("https://x/9.21/s", release.Url);
    }

    [Fact]
    public void AReleaseWithoutThatAssetIsSkipped()
    {
        Assert.DoesNotContain(RunnerIndex.ParseReleases(Releases), r => r.Version == "6.0");
    }

    [Theory]
    [InlineData("9.21", false)]
    [InlineData("9.7", false)]
    [InlineData("9.22", true)]
    [InlineData("10.0", true)]
    [InlineData("11.15", true)]
    public void VersionsFromNineTwentyTwoOnAreFlagged(string version, bool breaks)
    {
        Assert.Equal(breaks, RunnerIndex.BreaksEditors(version));

        var release = RunnerIndex.ParseReleases(Releases).FirstOrDefault(r => r.Version == version);
        if (release is not null)
        {
            Assert.Equal(breaks, release.BreaksEditors);
        }
    }

    [Fact]
    public void TheChecksumIsReadForTheRightFile()
    {
        var sums = """
            aaa11  wine-9.21-amd64.tar.xz
            bbb22 *wine-9.21-staging-tkg-amd64.tar.xz
            ccc33  wine-9.21-staging-x86.tar.xz
            """;

        Assert.Equal("bbb22", RunnerIndex.ChecksumFor(sums, "wine-9.21-staging-tkg-amd64.tar.xz"));
        Assert.Null(RunnerIndex.ChecksumFor(sums, "wine-9.99-staging-amd64.tar.xz"));
    }

    [Fact]
    public void TheRunnerIsNamedAfterTheAssetWithoutItsSuffix()
    {
        var release = RunnerIndex.ParseReleases(Releases).Single(r => r.Version == "9.21");

        Assert.Equal("wine-9.21-staging-tkg", release.Name);
    }

    [Theory]
    [InlineData("/tmp/wine-9.21-staging-tkg-amd64.tar.xz", "wine-9.21-staging-tkg")]
    [InlineData("/tmp/soda-9.0-1-x86_64.tar.xz", "soda-9.0-1")]
    [InlineData("/tmp/vaniglia-10.19-x86_64.tar.gz", "vaniglia-10.19")]
    public void ALocalArchiveNamesItsRunner(string path, string expected)
    {
        Assert.Equal(expected, Runners.DeriveName(path));
    }
}
