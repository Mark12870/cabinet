namespace Cabinet.Core.Tests;

public class DocumentationTests
{
    [Theory]
    [InlineData("README.md", 100)]
    [InlineData("AGENTS.md", 500)]
    [InlineData("docs/ARCHITECTURE.md", 500)]
    public void ADocumentStaysUnderItsLineLimit(string document, int limit)
    {
        Assert.InRange(Repo.Lines(document).Length, 0, limit - 1);
    }

    [Fact]
    public void TheReadmeNamesEveryManagerTheLibraryShips()
    {
        var listed = Repo.Lines("README.md")
            .SkipWhile(line => !line.Contains("<th>Managers &amp; players</th>", StringComparison.Ordinal))
            .First(line => line.StartsWith("<td>", StringComparison.Ordinal))["<td>".Length..^"</td>".Length]
            .Split("<br>");
        var shipped = Catalogue.Entries()
            .Where(entry => entry.Manager)
            .Where(entry => !Directory.EnumerateFiles(Repo.Path($"data/library/{entry.Vendor}"), "*.md").Any())
            .Select(entry => entry.Name)
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(shipped, listed);
    }

    [Fact]
    public void TheReadmeNamesEveryVendorTheLibraryShips()
    {
        var listed = Repo.Lines("README.md")
            .SkipWhile(line => line != "## Supported vendors")
            .TakeWhile(line => line != "</table>")
            .Where(line => line.StartsWith("<td>", StringComparison.Ordinal))
            .SelectMany(line => line["<td>".Length..^"</td>".Length].Split("<br>"))
            .SelectMany(cell => cell.Split([" (", ", ", ")"], StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        var unlisted = Catalogue.Entries()
            .Where(entry => !entry.Manager)
            .Where(entry => !Directory.EnumerateFiles(Repo.Path($"data/library/{entry.Vendor}"), "*.md").Any())
            .Where(entry => !listed.Contains(entry.Developer ?? "") && !listed.Contains(entry.Name))
            .Select(entry => entry.Id);

        Assert.Empty(unlisted);
    }

    [Fact]
    public void TheReadmeNamesEveryYabridgePatchCabinetCarries()
    {
        var listed = Repo.Lines("README.md")
            .SkipWhile(line => line != "## Yabridge patches")
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.StartsWith("- **", StringComparison.Ordinal))
            .Select(line => line["- **".Length..line.IndexOf("**", "- **".Length, StringComparison.Ordinal)])
            .Order(StringComparer.Ordinal);
        var carried = Directory.EnumerateFiles(Repo.Path("patches/yabridge"), "yabridge-*.patch")
            .Select(path => System.IO.Path.GetFileNameWithoutExtension(path)["yabridge-".Length..])
            .Order(StringComparer.Ordinal);

        Assert.Equal(carried, listed);
    }
}
