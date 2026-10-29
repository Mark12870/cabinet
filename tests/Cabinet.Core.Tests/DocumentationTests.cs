namespace Cabinet.Core.Tests;

public class DocumentationTests
{
    [Theory]
    [InlineData("README.md", 100)]
    [InlineData("AGENTS.md", 500)]
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
}
