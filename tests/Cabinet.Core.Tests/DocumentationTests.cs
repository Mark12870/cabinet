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
}
