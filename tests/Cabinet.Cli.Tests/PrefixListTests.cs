namespace Cabinet.Cli.Tests;

public sealed class PrefixListTests : IDisposable
{
    private readonly Cli cli = new();

    public void Dispose() => cli.Dispose();

    [Fact]
    public void SearchKeepsOnlyThePrefixesItsWordsAppearIn()
    {
        cli.Prefix("gadget");
        cli.Prefix("widget");

        var outcome = cli.Run("list", "--search", "GAD");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("gadget", outcome.Out);
        Assert.DoesNotContain("widget", outcome.Out);
    }

    [Fact]
    public void SearchNarrowsTheJsonListing()
    {
        cli.Prefix("gadget");
        cli.Prefix("widget");

        var prefix = Assert.Single(Parsed.Objects(cli.Run("list", "--search", "widget", "--json").Out));

        Assert.Equal("widget", prefix.GetProperty("name").GetString());
    }

    [Fact]
    public void ASearchNothingAnswersSaysSo()
    {
        cli.Prefix("gadget");

        var outcome = cli.Run("list", "--search", "reverb");

        Assert.Equal(0, outcome.Exit);
        Assert.Equal("No prefix matches that." + Environment.NewLine, outcome.Out);
    }
}
