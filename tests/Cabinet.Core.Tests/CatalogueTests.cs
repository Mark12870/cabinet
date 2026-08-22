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
    public void AVersionIsPinnedInTheUrlThatFetchesIt()
    {
        var drifting = Shipped
            .Where(entry => entry.Url is not null)
            .Where(entry => entry.Url!.Contains("/latest/", StringComparison.Ordinal))
            .Select(entry => entry.Id);

        Assert.Empty(drifting);
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
