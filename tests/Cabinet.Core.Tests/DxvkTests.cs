using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class DxvkTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private Dxvk Subject => new(Layout, new UnusedRunner());

    [Fact]
    public void APrefixReportsNoDxvkUntilItIsInstalled()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Assert.Null(Subject.InstalledIn("serum"));
    }

    [Fact]
    public void TheMarkerNamesTheVersionThatWasUnpacked()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        File.WriteAllText(Layout.PrefixDxvkFile("serum"), "2.7.1\n");

        Assert.Equal("2.7.1", Subject.InstalledIn("serum"));
    }

    [Fact]
    public void AnEmptyMarkerCountsAsNoDxvk()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        File.WriteAllText(Layout.PrefixDxvkFile("serum"), "  \n");

        Assert.Null(Subject.InstalledIn("serum"));
    }

    [Fact]
    public void APrefixThatWasNeverBootedIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Assert.Throws<DirectoryNotFoundException>(() => Subject.Install("serum"));
    }

    [Fact]
    public void TheDownloadIsPinnedToTheVersionItChecksums()
    {
        Assert.Contains($"v{Dxvk.Version}/", Dxvk.Url);
        Assert.Equal(64, Dxvk.Sha256.Length);
    }

    [Fact]
    public void EveryLibraryDxvkReplacesIsListed()
    {
        Assert.Equal(["d3d8", "d3d9", "d3d10core", "d3d11", "dxgi"], Dxvk.Libraries);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
