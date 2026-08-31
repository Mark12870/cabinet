using System.IO.Compression;
using System.Security.Cryptography;

using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class CatalogueTests
{
    private static readonly IReadOnlyList<LibraryEntry> Shipped = Catalogue.Entries();

    private static readonly string[] Manifest =
        Repo.Lines("io.github.mark12870.cabinet.yml");

    [Fact]
    public void EveryShippedEntryParsesAndNoTwoVendorsClaimAnId()
    {
        Assert.NotEmpty(Shipped);
    }

    [Fact]
    public void EveryScriptAnEntryNamesIsShippedBesideIt()
    {
        var layout = Catalogue.Layout();

        var missing = Shipped
            .Where(entry => entry.Script is not null)
            .Where(entry => !File.Exists(layout.LibraryScript(entry.Vendor, entry.Script!)))
            .Select(entry => $"{entry.Id} -> {entry.Vendor}/{entry.Script}");

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryWindowsEntryNamesAnInstallScript()
    {
        var interactive = Shipped
            .Where(entry => entry.Kind == PluginKind.Windows && entry.Script is null)
            .Select(entry => entry.Id);

        Assert.Empty(interactive);
    }

    [Fact]
    public void NoInstallScriptSetsAnEnvironmentAnEntryCouldDeclare()
    {
        var layout = Catalogue.Layout();

        var poking = Shipped
            .Where(entry => entry.Script is not null)
            .SelectMany(
                entry => File.ReadAllLines(layout.LibraryScript(entry.Vendor, entry.Script!))
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("export ", StringComparison.Ordinal)
                                   || line.Contains(@"Wine\\DllOverrides", StringComparison.Ordinal)),
                (entry, line) => $"{entry.Vendor}/{entry.Script} has {line} — that is Env:");

        Assert.Empty(poking);
    }

    [Fact]
    public void EveryRelinkIsANativeEntryWritingAShorterSonameOverALongerOne()
    {
        var wrong = Shipped
            .Where(entry => entry.Relink.Count > 0)
            .SelectMany(
                entry => entry.Relink.Where(
                    one => entry.Kind != PluginKind.Native || one.Value.Length > one.Key.Length),
                (entry, one) => $"{entry.Id} relinks {one.Key} to {one.Value}");

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryDataDirectoryAnEntryClaimsIsGrantedByTheManifest()
    {
        var granted = Manifest
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- --filesystem=~/", StringComparison.Ordinal))
            .Select(line => line["- --filesystem=~/".Length..].Split(':')[0])
            .ToHashSet(StringComparer.Ordinal);

        var ungranted = Shipped
            .Where(entry => entry.Data is not null)
            .Select(entry => (entry.Id, Dir: entry.Data!.Split('/')[0]))
            .Where(claim => !granted.Contains(claim.Dir))
            .Select(claim => $"{claim.Id} writes ~/{claim.Dir} with no --filesystem grant");

        Assert.Empty(ungranted);
    }

    [Fact]
    public void ARollingEntryPinsNothingAndADownloadPinsEverything()
    {
        Assert.Empty(Shipped
            .Where(entry => entry.Source == PluginSource.Rolling)
            .Where(entry => entry.Sha256 is not null || entry.Version is not null)
            .Select(entry => entry.Id));

        Assert.Empty(Shipped
            .Where(entry => entry.Source == PluginSource.Download)
            .Where(entry => entry.Sha256 is null || entry.Url is null)
            .Select(entry => entry.Id));
    }

    [Fact]
    public void AnEntryCabinetCannotDownloadNamesThePageToGetItFrom()
    {
        Assert.Empty(Shipped
            .Where(entry => entry.Source == PluginSource.Byo)
            .Where(entry => entry.Url is not null)
            .Select(entry => entry.Id));
    }

    [Fact]
    public void AByoDemoIsAWindowsDownloadWithAChecksum()
    {
        var demos = Shipped.Where(entry => entry.DemoUrl is not null).ToList();

        Assert.NotEmpty(demos);
        Assert.All(demos, entry =>
        {
            Assert.Equal(PluginKind.Windows, entry.Kind);
            Assert.Equal(PluginSource.Byo, entry.Source);
            Assert.NotNull(entry.DemoSha256);
        });
    }

    [Fact]
    public void EveryEntryCarriesWhatBothFrontEndsRender()
    {
        var bare = Shipped
            .Where(entry => entry.Summary.Length == 0
                            || entry.Developer is null
                            || entry.Licence is null
                            || entry.Formats.Count == 0
                            || entry.Description.Count == 0)
            .Select(entry => entry.Id);

        Assert.Empty(bare);
    }

    [Fact]
    public void EveryAppCabinetOpensIsAWindowsOneAndSaysWhereItLands()
    {
        var wrong = Shipped
            .Where(entry => entry.Launch is not null)
            .Where(entry => entry.Kind != PluginKind.Windows
                            || entry.Launch is not [var drive, ':', '\\', ..]
                            || !char.IsAsciiLetter(drive))
            .Select(entry => $"{entry.Id} -> {entry.Launch}");

        Assert.Empty(wrong);
    }

    [Fact]
    public void AVendorTheManifestHoldsBackSaysWhyBesideItsEntries()
    {
        var held = Shipped
            .Select(entry => entry.Vendor)
            .Distinct(StringComparer.Ordinal)
            .Where(vendor => Directory
                .EnumerateFiles(Repo.Path($"data/library/{vendor}"), "*.md")
                .Any());

        var installs = Manifest.Any(line =>
            line.Contains("\"$vendor\"*.md", StringComparison.Ordinal)
            && line.Contains("continue", StringComparison.Ordinal));

        Assert.True(
            !held.Any() || installs,
            $"{string.Join(", ", held)} carries a .md but the manifest installs every vendor");
    }

    [Fact]
    public void AVersionIsPinnedInTheUrlThatFetchesIt()
    {
        var drifting = Shipped
            .Where(entry => entry.Url is not null)
            .Where(entry => entry.Url!.Contains("/latest/", StringComparison.Ordinal))
            .Select(entry => entry.Id);

        Assert.Empty(drifting);
    }

    [Fact]
    public void TheKlevgrandHelperShipsAValidArchiveRuntime()
    {
        var path = Repo.Path("data/library/klevgrand/klevgrand-helper-runtime.zip");
        Assert.True(File.Exists(path));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        const string expectedHash =
            "50faa6b30310fbe8cd5807405153fcdf61a020321fb58428cc74a98bf2e39790";

        Assert.Equal(expectedHash, hash);
        Assert.Contains(expectedHash, Repo.Read("SOURCES.md"), StringComparison.Ordinal);

        using var archive = ZipFile.OpenRead(path);
        var files = archive.Entries
            .Where(entry => entry.Length > 0)
            .ToDictionary(entry => entry.FullName, StringComparer.Ordinal);

        string[] required =
        [
            "tar.exe",
            "libarchive-13.dll",
            "libb2-1.dll",
            "libbz2-1.dll",
            "libexpat-1.dll",
            "libiconv-2.dll",
            "liblz4.dll",
            "liblzma-5.dll",
            "libpcre2-8-0.dll",
            "libpcre2-posix-3.dll",
            "libzstd.dll",
            "zlib1.dll",
        ];

        var missing = required.Where(file => !files.ContainsKey(file));
        Assert.Empty(missing);

        string[] licences =
        [
            "licenses/SOURCES.txt",
            "licenses/bzip2.txt",
            "licenses/expat.txt",
            "licenses/libarchive-LICENSE.txt",
            "licenses/libb2.txt",
            "licenses/libcharset-COPYING.LIB.txt",
            "licenses/libiconv-COPYING.LIB.txt",
            "licenses/libiconv-COPYING.txt",
            "licenses/lz4-LICENSE.txt",
            "licenses/pcre2-LICENCE.md",
            "licenses/xz-COPYING.0BSD.txt",
            "licenses/xz-COPYING.GPLv2.txt",
            "licenses/xz-COPYING.GPLv3.txt",
            "licenses/xz-COPYING.LGPLv2.1.txt",
            "licenses/xz-COPYING.txt",
            "licenses/zlib.txt",
            "licenses/zstd.txt",
        ];

        var actualLicences = files.Keys
            .Where(file => file.StartsWith("licenses/", StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal);

        Assert.Equal(licences.OrderBy(file => file, StringComparer.Ordinal), actualLicences);

        foreach (var entry in files.Values)
        {
            using var content = entry.Open();
            content.CopyTo(Stream.Null);
        }
    }
}

internal static class Catalogue
{
    public static Layout Layout() =>
        new("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent",
            Repo.Path("data/library"));

    public static IReadOnlyList<LibraryEntry> Entries() =>
        new Library(Layout(), new UnusedRunner()).Entries();
}
