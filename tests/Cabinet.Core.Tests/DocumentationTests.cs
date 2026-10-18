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
            .SkipWhile(line => line != "## Managers & players")
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..]);
        var shipped = Catalogue.Entries()
            .Where(entry => entry.Manager)
            .Where(entry => !Directory.EnumerateFiles(Repo.Path($"data/library/{entry.Vendor}"), "*.md").Any())
            .Select(entry => entry.Name)
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(shipped, listed);
    }
}
