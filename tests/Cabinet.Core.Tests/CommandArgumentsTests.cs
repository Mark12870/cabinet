using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class CommandArgumentsTests
{
    [Fact]
    public void SimpleArgumentsAreSeparated()
    {
        Assert.Equal(["cabinet", "run", "prefix"], CommandArguments.Parse("cabinet run prefix"));
    }

    [Fact]
    public void QuotesKeepSpacesInsideArguments()
    {
        Assert.Equal(
            ["one two", "three four"],
            CommandArguments.Parse("'one two' \"three four\""));
    }

    [Fact]
    public void BackslashesEscapeTheNextCharacter()
    {
        Assert.Equal(["one two", "three\"four", "five"],
            CommandArguments.Parse("one\\ two three\\\"four five"));
    }

    [Fact]
    public void EmptyQuotedArgumentsArePreserved()
    {
        Assert.Equal(["", "", "tail"], CommandArguments.Parse("'' \"\" tail"));
    }

    [Fact]
    public void QuotedWindowsPathsKeepOrdinaryBackslashes()
    {
        Assert.Equal(
            ["C:\\Program Files\\Vendor\\tool.exe", "quoted\"value", "one\\two"],
            CommandArguments.Parse(
                "\"C:\\Program Files\\Vendor\\tool.exe\" \"quoted\\\"value\" \"one\\two\""));
    }

    [Theory]
    [InlineData("   ", "empty")]
    [InlineData("'unfinished", "single quote")]
    [InlineData("\"unfinished", "double quote")]
    [InlineData("unfinished\\", "escape")]
    public void MalformedArgumentsExplainTheProblem(string input, string problem)
    {
        var failure = Assert.Throws<ArgumentException>(() => CommandArguments.Parse(input));

        Assert.Contains(problem, failure.Message);
    }
}
