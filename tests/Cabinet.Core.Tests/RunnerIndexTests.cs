using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class RunnerIndexTests
{
    private const string Kron4ekReleases = """
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

    private const string BottlesReleases = """
        [
          {
            "tag_name": "mcsoda-11.0-4",
            "assets": [
              {"name": "mcsoda-11.0-4-x86_64.tar.xz", "browser_download_url": "https://b/mc"},
              {"name": "mcsoda-11.0-4-x86_64.tar.xz.sha256", "browser_download_url": "https://b/mcs"}
            ]
          },
          {
            "tag_name": "soda-11.0-5",
            "assets": [{"name": "soda-11.0-5-x86_64.tar.xz", "browser_download_url": "https://b/s"}]
          },
          {
            "tag_name": "protosoda-11.0-1",
            "assets": [{"name": "ProtoSoda-11.0-1.tar.gz", "browser_download_url": "https://b/p"}]
          },
          {
            "tag_name": "caffe-10.0",
            "assets": [{"name": "caffe-10.0-x86_64.tar.xz", "browser_download_url": "https://b/c"}]
          },
          {
            "tag_name": "soda-9.0-1",
            "assets": [{"name": "soda-9.0-1-x86_64.tar.xz", "browser_download_url": "https://b/o"}]
          }
        ]
        """;

    private static IReadOnlyList<RunnerRelease> Kron4ek =>
        RunnerIndex.ParseReleases(RunnerFamily.Kron4ek, Kron4ekReleases);

    private static IReadOnlyList<RunnerRelease> Soda =>
        RunnerIndex.ParseReleases(RunnerFamily.Soda, BottlesReleases);

    [Fact]
    public void OnlyTheStagingTkgMultilibAssetIsOffered()
    {
        var release = Kron4ek.Single(r => r.Version == "9.21");

        Assert.Equal("wine-9.21-staging-tkg-amd64.tar.xz", release.Asset);
        Assert.Equal("https://x/9.21/s", release.Url);
    }

    [Fact]
    public void AReleaseWithoutThatAssetIsSkipped()
    {
        Assert.DoesNotContain(Kron4ek, r => r.Version == "6.0");
    }

    [Fact]
    public void TheOtherBottlesFamiliesAreNotSoda()
    {
        Assert.Equal(["11.0-5", "9.0-1"], Soda.Select(release => release.Version));
    }

    [Fact]
    public void ASodaTagLosesItsPrefixButTheRunnerKeepsIt()
    {
        var release = Soda.Single(r => r.Version == "11.0-5");

        Assert.Equal("soda-11.0-5-x86_64.tar.xz", release.Asset);
        Assert.Equal("soda-11.0-5", release.Name);
        Assert.Null(release.Family.SumsFile);
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
        Assert.Equal("wine-9.21-staging-tkg", Kron4ek.Single(r => r.Version == "9.21").Name);
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
