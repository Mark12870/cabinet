using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class RunnerIndexTests
{
    private const string Index = """
        # -----------------------
        # THIS FILE HAS BEEN GENERATED AUTOMATICALLY
        # -----------------------

        # ./input_files/3-soda.yml
        soda-mcsoda-11.0-4:
          Category: runners
          Sub-category: wine
          Channel: stable
          Date: '1786812163'
        soda-11.0-5:
          Category: runners
          Sub-category: wine
          Channel: stable
          Date: '1786362735'
        soda-experimental_8.0:
          Category: runners
          Sub-category: wine
          Channel: stable
        soda-12.0-1:
          Category: runners
          Sub-category: wine
          Channel: unstable
          Date: '1786912163'
        soda-9.0-1:
          Category: runners
          Sub-category: wine
          Channel: stable
        kron4ek-wine-11.15-staging-tkg-amd64:
          Category: runners
          Sub-category: wine
          Channel: stable
          Date: '1786533303'
        kron4ek-wine-9.21-staging-tkg-amd64:
          Category: runners
          Sub-category: wine
          Channel: stable
          Date: '1731238804'
        kron4ek-wine-9.21-staging-amd64:
          Category: runners
          Sub-category: wine
          Channel: stable
          Date: '1731238804'
        kron4ek-wine-proton-9.0-3-amd64:
          Category: runners
          Sub-category: wine
          Channel: stable
        dxvk-2.7.1:
          Category: dxvk
          Channel: stable
        """;

    private const string Manifest = """
        Name: soda-11.0-5
        Provider: bottlesdevs
        Channel: stable
        File:
        - file_name: soda-11.0-5-x86_64.tar.xz
          url: https://github.com/bottlesdevs/wine/releases/download/soda-11.0-5/soda-11.0-5-x86_64.tar.xz
          file_checksum: 2dfc9cc56cee4b0874269e6dbc91c2f2
          file_size: 154580296
          rename: soda-11.0-5-x86_64.tar.xz
        Post:
        - action: rename
        """;

    private static IReadOnlyList<RunnerRelease> Releases(RunnerFamily family) =>
        RunnerIndex.ReleasesFrom(family, Components.Entries(Index));

    private static RunnerIndex Answering(string status, string body) =>
        new(new StubRunner(new ProcessResult(0, body, $"HTTP/2 {status} \ncontent-type: text/plain\n")));

    [Fact]
    public void TheOtherBottlesFamiliesAreNotSoda()
    {
        Assert.Equal(["11.0-5", "9.0-1"], Releases(RunnerFamily.Soda).Select(release => release.Version));
    }

    [Fact]
    public void OnlyTheStagingTkgMultilibBuildIsOffered()
    {
        Assert.Equal(
            ["11.15", "9.21"],
            Releases(RunnerFamily.Kron4ek).Select(release => release.Version));
    }

    [Fact]
    public void AnUnstableChannelIsNotOffered()
    {
        Assert.DoesNotContain(Releases(RunnerFamily.Soda), release => release.Version == "12.0-1");
    }

    [Fact]
    public void AReleaseKeepsTheNameItsArchiveGivesIt()
    {
        var release = Releases(RunnerFamily.Kron4ek).Single(found => found.Version == "9.21");

        Assert.Equal("wine-9.21-staging-tkg-amd64.tar.xz", release.Asset);
        Assert.Equal("wine-9.21-staging-tkg", release.Name);
        Assert.Equal(
            "https://raw.githubusercontent.com/bottlesdevs/components/main/runners/wine/"
            + "kron4ek-wine-9.21-staging-tkg-amd64.yml",
            release.ManifestUrl);
    }

    [Fact]
    public void ASodaEntryKeepsItsPrefixInTheArchive()
    {
        var release = Releases(RunnerFamily.Soda).Single(found => found.Version == "11.0-5");

        Assert.Equal("soda-11.0-5-x86_64.tar.xz", release.Asset);
        Assert.Equal("soda-11.0-5", release.Name);
        Assert.Null(release.Family.SumsFile);
    }

    [Fact]
    public void AManifestCarriesTheDownloadAndItsChecksum()
    {
        var listed = Components.Manifest(Manifest);

        Assert.Equal("soda-11.0-5-x86_64.tar.xz", listed.FileName);
        Assert.Equal(
            "https://github.com/bottlesdevs/wine/releases/download/soda-11.0-5/soda-11.0-5-x86_64.tar.xz",
            listed.Url);
        Assert.Equal("2dfc9cc56cee4b0874269e6dbc91c2f2", listed.Checksum);
    }

    [Fact]
    public void BothFamiliesComeFromOneRequest()
    {
        var available = Answering("200", Index).Available();

        Assert.Equal(5, available.Count);
        Assert.Equal("wine-9.21-staging-tkg", Answering("200", Index).Find("9.21").Name);
    }

    [Fact]
    public void TheD2D1DcompBuildIsOfferedWithoutTouchingBottlesAtAll()
    {
        var release = Answering("200", Index).Find("d2d1-11.0");

        Assert.Equal(RunnerFamily.D2D1Dcomp, release.Family);
        Assert.Equal("wine-d2d1-11.0-x86_64.tar.zst", release.Asset);
        Assert.Equal("wine-d2d1-11.0", release.Name);
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

    [Theory]
    [InlineData("/tmp/wine-9.21-staging-tkg-amd64.tar.xz", "wine-9.21-staging-tkg")]
    [InlineData("/tmp/soda-9.0-1-x86_64.tar.xz", "soda-9.0-1")]
    [InlineData("/tmp/vaniglia-10.19-x86_64.tar.gz", "vaniglia-10.19")]
    public void ALocalArchiveNamesItsRunner(string path, string expected)
    {
        Assert.Equal(expected, Runners.DeriveName(path));
    }
}
