namespace Cabinet.Cli.Tests;

public sealed class InstallationTests : IDisposable
{
    private const string Instructions = "Choose the complete Windows installer ZIP (Play_1_1_2.zip).";
    private readonly Cli cli = new();

    public void Dispose() => cli.Dispose();

    [Fact]
    public void InstallationInstructionsAppearBeforeAFileIsSupplied()
    {
        cli.Catalogue("thing", $"Name: Thing\nKind: windows\nSource: byo\nInstallInstructions: {Instructions}\n");

        var shown = cli.Run("library", "show", "thing");
        var missing = cli.Run("library", "install", "thing");
        var json = Assert.Single(Parsed.Objects(cli.Run("library", "show", "thing", "--json").Out));

        Assert.Equal(0, shown.Exit);
        Assert.Contains(Instructions, shown.Out);
        Assert.Contains("cabinet library install thing <file>", shown.Out.Replace(Environment.NewLine, " "));
        Assert.Equal(2, missing.Exit);
        Assert.Contains(Instructions, missing.Error);
        Assert.Equal(Instructions, json.GetProperty("installInstructions").GetString());
        Assert.Empty(cli.Runner.Calls);
        Assert.False(Directory.Exists(cli.Layout.PrefixPath("thing")));
    }
}
